"""Stage 3 goalkeeper benchmark runner and report aggregation."""

from __future__ import annotations

import argparse
import csv
import json
import math
import platform
import time
import uuid
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

import numpy as np
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.side_channel.environment_parameters_channel import (
    EnvironmentParametersChannel,
)
from mlagents_envs.side_channel.raw_bytes_channel import RawBytesChannel
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel


BEHAVIOR_NAME = "GoalkeeperState-v0"
OBSERVATION_SHAPES = [[24]]
DISCRETE_BRANCHES = [9]
STAGE3_TELEMETRY_CHANNEL_ID = uuid.UUID("b8d7b5b3-bfa6-4c46-9a3f-2e34d9fd7a31")
STAGE3_TELEMETRY_FLAG = "--stage3-benchmark-telemetry"

ACTION_NAMES = [
    "Hold",
    "ShuffleLeft",
    "ShuffleRight",
    "DiveLeftLow",
    "DiveLeftMiddle",
    "DiveLeftHigh",
    "DiveRightLow",
    "DiveRightMiddle",
    "DiveRightHigh",
]

GOAL_HALF_WIDTH = 3.66
BALL_RADIUS = 0.11
CROSSBAR_LOWER_EDGE = 2.44
TARGET_X_EXTENT = GOAL_HALF_WIDTH - BALL_RADIUS
TARGET_Y_MIN = BALL_RADIUS
TARGET_Y_MAX = CROSSBAR_LOWER_EDGE - BALL_RADIUS
GRAVITY_Y = -9.81


@dataclass(frozen=True)
class BenchmarkConfig:
    schema_version: int
    benchmark_id: str
    environment_id: str
    behavior_name: str
    observation_spec_id: str
    reward_spec_id: str
    action_spec_id: str
    scenario_suite_id: str
    arena_count: int
    attempts_per_arena: int
    total_attempts: int
    stage2_lesson: int
    master_seed: int
    primary_metric: str
    save_outcomes: tuple[str, ...]
    failure_outcomes: tuple[str, ...]


class GoalkeeperPolicy:
    name = "policy"
    policy_type = "scripted"

    def act_batch(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
    ) -> np.ndarray:
        raise NotImplementedError

    @staticmethod
    def _legal_actions(action_mask: np.ndarray | None, row: int) -> np.ndarray:
        legal = np.arange(len(ACTION_NAMES), dtype=np.int32)
        if action_mask is not None:
            legal = legal[~np.asarray(action_mask[row], dtype=bool)]
        if len(legal) == 0:
            return np.asarray([0], dtype=np.int32)
        return legal

    @staticmethod
    def _sanitize(
        actions: Iterable[int],
        action_mask: np.ndarray | None,
    ) -> np.ndarray:
        output = np.asarray(list(actions), dtype=np.int32).reshape(-1)
        if action_mask is None:
            output[(output < 0) | (output >= len(ACTION_NAMES))] = 0
            return output.reshape(-1, 1)

        for row, action in enumerate(output):
            if (
                action < 0
                or action >= len(ACTION_NAMES)
                or bool(action_mask[row, action])
            ):
                output[row] = 0
        return output.reshape(-1, 1)


class StandCenterPolicy(GoalkeeperPolicy):
    name = "stand_center"

    def act_batch(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
    ) -> np.ndarray:
        return np.zeros((len(observations), 1), dtype=np.int32)


class RandomLegalPolicy(GoalkeeperPolicy):
    name = "random_legal"

    def __init__(self, seed: int = 20260724) -> None:
        self._rng = np.random.default_rng(seed)

    def act_batch(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
    ) -> np.ndarray:
        actions = []
        for row in range(len(observations)):
            legal = self._legal_actions(action_mask, row)
            actions.append(int(self._rng.choice(legal)))
        return np.asarray(actions, dtype=np.int32).reshape(-1, 1)


class ReactiveSidePolicy(GoalkeeperPolicy):
    name = "reactive_side"

    def act_batch(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
    ) -> np.ndarray:
        actions = []
        for row, obs in enumerate(observations):
            ball_x = float(obs[0]) * 5.0
            ball_y = float(obs[1]) * 4.0
            keeper_x = float(obs[9]) * 3.1
            flight_time = float(obs[21])
            side_delta = ball_x - keeper_x
            if flight_time > 0.12 and abs(side_delta) > 0.25:
                actions.append(dive_action_for_target(ball_x, ball_y))
            elif side_delta < -0.15:
                actions.append(1)
            elif side_delta > 0.15:
                actions.append(2)
            else:
                actions.append(0)
        return self._sanitize(actions, action_mask)


class LinearInterceptPolicy(GoalkeeperPolicy):
    name = "linear_intercept"

    def act_batch(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
    ) -> np.ndarray:
        actions = []
        for obs in observations:
            position = np.asarray(
                [float(obs[0]) * 5.0, float(obs[1]) * 4.0, float(obs[2]) * 11.0],
                dtype=np.float32,
            )
            velocity = np.asarray(
                [
                    float(obs[3]) * 25.0,
                    float(obs[4]) * 25.0,
                    float(obs[5]) * 25.0,
                ],
                dtype=np.float32,
            )
            if velocity[2] < -0.1:
                t = -position[2] / velocity[2]
                if 0.0 <= t <= 1.5:
                    target_x = position[0] + velocity[0] * t
                    target_y = position[1] + velocity[1] * t + 0.5 * GRAVITY_Y * t * t
                    actions.append(dive_action_for_target(float(target_x), float(target_y)))
                    continue
            actions.append(0)
        return self._sanitize(actions, action_mask)


class OnnxPolicy(GoalkeeperPolicy):
    policy_type = "onnx"

    def __init__(self, model_path: Path) -> None:
        import onnxruntime as ort

        self.model_path = model_path
        self.name = f"onnx:{model_path.stem}"
        self._session = ort.InferenceSession(
            str(model_path),
            providers=["CPUExecutionProvider"],
        )
        self._output_name = "deterministic_discrete_actions"
        output_names = {output.name for output in self._session.get_outputs()}
        if self._output_name not in output_names:
            raise RuntimeError(
                f"ONNX model {model_path} does not expose {self._output_name}"
            )

    def act_batch(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
    ) -> np.ndarray:
        mask = (
            np.zeros((len(observations), len(ACTION_NAMES)), dtype=np.float32)
            if action_mask is None
            else np.asarray(action_mask, dtype=np.float32)
        )
        result = self._session.run(
            [self._output_name],
            {
                "obs_0": np.asarray(observations, dtype=np.float32),
                "action_masks": mask,
            },
        )[0]
        return self._sanitize(np.asarray(result).reshape(-1), action_mask)


def dive_action_for_target(target_x: float, target_y: float) -> int:
    height = height_band_for_y(target_y)
    if target_x < 0.0:
        return {"low": 3, "middle": 4, "high": 5}[height]
    return {"low": 6, "middle": 7, "high": 8}[height]


def load_benchmark_config(path: Path) -> BenchmarkConfig:
    raw = json.loads(path.read_text(encoding="utf-8"))
    config = BenchmarkConfig(
        schema_version=int(raw["schema_version"]),
        benchmark_id=str(raw["benchmark_id"]),
        environment_id=str(raw["environment_id"]),
        behavior_name=str(raw["behavior_name"]),
        observation_spec_id=str(raw["observation_spec_id"]),
        reward_spec_id=str(raw["reward_spec_id"]),
        action_spec_id=str(raw["action_spec_id"]),
        scenario_suite_id=str(raw["scenario_suite_id"]),
        arena_count=int(raw["arena_count"]),
        attempts_per_arena=int(raw["attempts_per_arena"]),
        total_attempts=int(raw["total_attempts"]),
        stage2_lesson=int(raw["stage2_lesson"]),
        master_seed=int(raw["master_seed"]),
        primary_metric=str(raw["primary_metric"]),
        save_outcomes=tuple(raw["save_outcomes"]),
        failure_outcomes=tuple(raw["failure_outcomes"]),
    )
    validate_benchmark_config(config)
    return config


def validate_benchmark_config(config: BenchmarkConfig) -> None:
    if config.schema_version != 1:
        raise ValueError(f"Unsupported benchmark schema: {config.schema_version}")
    if config.behavior_name != BEHAVIOR_NAME:
        raise ValueError(f"Unsupported behavior: {config.behavior_name}")
    if config.arena_count <= 0 or config.attempts_per_arena <= 0:
        raise ValueError("arena_count and attempts_per_arena must be positive")
    if config.total_attempts != config.arena_count * config.attempts_per_arena:
        raise ValueError("total_attempts must equal arena_count * attempts_per_arena")
    if config.stage2_lesson != 3:
        raise ValueError("Stage 3 v0 benchmarks the full Stage 2 lesson 3 range")


def make_policy(spec: str, seed: int) -> GoalkeeperPolicy:
    if spec == "stand_center":
        return StandCenterPolicy()
    if spec == "random_legal":
        return RandomLegalPolicy(seed)
    if spec == "reactive_side":
        return ReactiveSidePolicy()
    if spec == "linear_intercept":
        return LinearInterceptPolicy()
    if spec.startswith("onnx:"):
        model_path = Path(spec.split(":", maxsplit=1)[1]).expanduser().resolve()
        if not model_path.exists():
            raise FileNotFoundError(model_path)
        return OnnxPolicy(model_path)
    raise ValueError(f"Unknown policy spec: {spec}")


def run_policy(
    *,
    build_path: Path,
    config: BenchmarkConfig,
    policy: GoalkeeperPolicy,
    attempts_per_arena: int,
    worker_id: int,
    player_log_path: Path | None = None,
    timeout_wait: int = 120,
    max_environment_steps: int = 2_000_000,
) -> list[dict[str, Any]]:
    telemetry = RawBytesChannel(STAGE3_TELEMETRY_CHANNEL_ID)
    parameters = EnvironmentParametersChannel()
    stats = StatsSideChannel()
    parameters.set_float_parameter("stage2.lesson", float(config.stage2_lesson))
    additional_args = [
        "-batchmode",
        "-nographics",
        STAGE3_TELEMETRY_FLAG,
        f"--stage3-master-seed={config.master_seed}",
    ]
    if player_log_path is not None:
        player_log_path.parent.mkdir(parents=True, exist_ok=True)
        additional_args.extend(["-logFile", str(player_log_path)])

    env = UnityEnvironment(
        file_name=str(build_path),
        worker_id=worker_id,
        seed=config.master_seed,
        no_graphics=True,
        timeout_wait=timeout_wait,
        additional_args=additional_args,
        side_channels=[telemetry, parameters, stats],
    )

    per_arena: dict[int, int] = defaultdict(int)
    episodes: list[dict[str, Any]] = []
    seen_keys: set[tuple[int, int]] = set()

    try:
        env.reset()
        behavior_name = require_stage3_behavior(env)
        for _ in range(max_environment_steps):
            drain_telemetry(
                telemetry,
                policy.name,
                attempts_per_arena,
                per_arena,
                seen_keys,
                episodes,
            )
            if quotas_met(per_arena, config.arena_count, attempts_per_arena):
                break

            decision_steps, _ = env.get_steps(behavior_name)
            if len(decision_steps):
                mask = decision_steps.action_mask[0] if decision_steps.action_mask else None
                actions = policy.act_batch(decision_steps.obs[0], mask)
                env.set_actions(behavior_name, ActionTuple(discrete=actions))
            env.step()
        else:
            raise TimeoutError(
                f"{policy.name} did not reach {attempts_per_arena} attempts per arena"
            )

        drain_telemetry(
            telemetry,
            policy.name,
            attempts_per_arena,
            per_arena,
            seen_keys,
            episodes,
        )
        return sorted(episodes, key=lambda item: (item["arena_id"], item["attempt_id"]))
    finally:
        env.close()


def require_stage3_behavior(env: UnityEnvironment) -> str:
    behavior_names = list(env.behavior_specs)
    if len(behavior_names) != 1 or behavior_names[0].split("?", maxsplit=1)[0] != BEHAVIOR_NAME:
        raise RuntimeError(f"Unexpected behavior names: {behavior_names}")
    behavior_name = behavior_names[0]
    specification = env.behavior_specs[behavior_name]
    branches = list(specification.action_spec.discrete_branches)
    if specification.action_spec.continuous_size != 0 or branches != DISCRETE_BRANCHES:
        raise RuntimeError(f"Unexpected action specification: {specification.action_spec}")
    observation_shapes = [
        list(observation.shape) for observation in specification.observation_specs
    ]
    if observation_shapes != OBSERVATION_SHAPES:
        raise RuntimeError(f"Unexpected observation shapes: {observation_shapes}")
    return behavior_name


def drain_telemetry(
    telemetry: RawBytesChannel,
    policy_name: str,
    attempts_per_arena: int,
    per_arena: dict[int, int],
    seen_keys: set[tuple[int, int]],
    episodes: list[dict[str, Any]],
) -> None:
    for raw in telemetry.get_and_clear_received_messages():
        item = json.loads(bytes(raw).decode("utf-8"))
        arena_id = int(item["arena_id"])
        attempt_id = int(item["attempt_id"])
        key = (arena_id, attempt_id)
        if attempt_id > attempts_per_arena or key in seen_keys:
            continue
        seen_keys.add(key)
        per_arena[arena_id] += 1
        item["policy"] = policy_name
        episodes.append(item)


def quotas_met(
    per_arena: dict[int, int],
    arena_count: int,
    attempts_per_arena: int,
) -> bool:
    return all(per_arena.get(arena_id, 0) >= attempts_per_arena for arena_id in range(arena_count))


def aggregate_policy(
    policy: GoalkeeperPolicy,
    episodes: list[dict[str, Any]],
    config: BenchmarkConfig,
    attempts_per_arena: int,
) -> dict[str, Any]:
    total = len(episodes)
    outcomes = Counter(str(item["outcome"]) for item in episodes)
    saves = sum(1 for item in episodes if item["outcome"] in config.save_outcomes)
    goals = outcomes["Goal"]
    invalid = outcomes["Invalid"]
    timeouts = outcomes["Timeout"]
    action_counts = [0] * len(ACTION_NAMES)
    for item in episodes:
        for index, count in enumerate(item.get("accepted_action_counts", [])):
            if index < len(action_counts):
                action_counts[index] += int(count)

    first_dive_episodes = [item for item in episodes if item.get("has_first_dive")]
    wrong_side = sum(1 for item in first_dive_episodes if is_wrong_side(item))
    wrong_height = sum(1 for item in first_dive_episodes if is_wrong_height(item))
    contact_then_goal = sum(
        1 for item in episodes if item["outcome"] == "Goal" and item["goalkeeper_contact"]
    )

    expected_total = config.arena_count * attempts_per_arena
    return {
        "policy": policy.name,
        "policy_type": policy.policy_type,
        "attempts": total,
        "expected_attempts": expected_total,
        "complete": total == expected_total,
        "outcomes": dict(sorted(outcomes.items())),
        "save_rate": rate(saves, total),
        "goal_rate": rate(goals, total),
        "invalid_rate": rate(invalid, total),
        "timeout_rate": rate(timeouts, total),
        "glove_contact_rate": rate(
            sum(1 for item in episodes if item["glove_contact"]), total
        ),
        "goalkeeper_contact_rate": rate(
            sum(1 for item in episodes if item["goalkeeper_contact"]), total
        ),
        "contact_then_goal_rate": rate(contact_then_goal, total),
        "wrong_side_rate": rate(wrong_side, len(first_dive_episodes)),
        "wrong_height_rate": rate(wrong_height, len(first_dive_episodes)),
        "action_mask_violations": sum(
            int(item["action_mask_violations"]) for item in episodes
        ),
        "duplicate_terminal_events": sum(
            int(item["duplicate_terminal_events"]) for item in episodes
        ),
        "action_usage": action_usage(action_counts),
        "by_quadrant": aggregate_by(episodes, quadrant),
        "by_height_band": aggregate_by(episodes, height_band),
        "by_horizontal_band": aggregate_by(episodes, horizontal_band),
        "by_flight_time_band": aggregate_by(episodes, flight_time_band),
        "by_first_dive_action": aggregate_by(episodes, first_dive_action),
    }


def aggregate_by(
    episodes: list[dict[str, Any]],
    classifier: Any,
) -> dict[str, dict[str, Any]]:
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for item in episodes:
        grouped[classifier(item)].append(item)
    return {
        key: {
            "attempts": len(items),
            "save_rate": rate(
                sum(item["outcome"] in {"Saved", "BlockedThenOut"} for item in items),
                len(items),
            ),
            "goal_rate": rate(sum(item["outcome"] == "Goal" for item in items), len(items)),
        }
        for key, items in sorted(grouped.items())
    }


def action_usage(action_counts: list[int]) -> dict[str, dict[str, float | int]]:
    total = sum(action_counts)
    return {
        ACTION_NAMES[index]: {
            "count": count,
            "rate": 0.0 if total == 0 else count / total,
        }
        for index, count in enumerate(action_counts)
    }


def rate(successes: int, total: int) -> dict[str, float | int]:
    interval = wilson_interval(successes, total)
    return {
        "successes": successes,
        "total": total,
        "value": 0.0 if total == 0 else successes / total,
        "ci95_low": interval[0],
        "ci95_high": interval[1],
    }


def wilson_interval(successes: int, total: int, z: float = 1.959963984540054) -> tuple[float, float]:
    if total <= 0:
        return (0.0, 0.0)
    phat = successes / total
    denominator = 1.0 + z * z / total
    centre = phat + z * z / (2.0 * total)
    spread = z * math.sqrt((phat * (1.0 - phat) + z * z / (4.0 * total)) / total)
    return ((centre - spread) / denominator, (centre + spread) / denominator)


def target_vector(item: dict[str, Any]) -> dict[str, float]:
    return item["requested_target_local"]


def target_y_normalized(target_y: float) -> float:
    return max(0.0, min(1.0, (target_y - TARGET_Y_MIN) / (TARGET_Y_MAX - TARGET_Y_MIN)))


def height_band_for_y(target_y: float) -> str:
    normalized = target_y_normalized(target_y)
    if normalized < 1.0 / 3.0:
        return "low"
    if normalized < 2.0 / 3.0:
        return "middle"
    return "high"


def height_band(item: dict[str, Any]) -> str:
    return height_band_for_y(float(target_vector(item)["y"]))


def horizontal_band(item: dict[str, Any]) -> str:
    normalized = max(-1.0, min(1.0, float(target_vector(item)["x"]) / TARGET_X_EXTENT))
    if normalized < -0.5:
        return "left"
    if normalized < 0.0:
        return "left-center"
    if normalized < 0.5:
        return "right-center"
    return "right"


def quadrant(item: dict[str, Any]) -> str:
    side = "left" if float(target_vector(item)["x"]) < 0.0 else "right"
    normalized_y = target_y_normalized(float(target_vector(item)["y"]))
    vertical = "low" if normalized_y < 0.5 else "high"
    return f"{vertical}-{side}"


def flight_time_band(item: dict[str, Any]) -> str:
    value = float(item["ball_flight_time"])
    if value <= 0.55:
        return "fast"
    if value <= 0.70:
        return "medium"
    return "slow"


def first_dive_action(item: dict[str, Any]) -> str:
    return str(item["first_accepted_dive_action"] or "none")


def is_wrong_side(item: dict[str, Any]) -> bool:
    target_side = "left" if float(target_vector(item)["x"]) < 0.0 else "right"
    action = first_dive_action(item)
    if action == "none":
        return False
    action_side = "left" if "Left" in action else "right" if "Right" in action else "none"
    return action_side != target_side


def is_wrong_height(item: dict[str, Any]) -> bool:
    action = first_dive_action(item)
    if action == "none":
        return False
    expected = height_band(item)
    actual = (
        "low"
        if action.endswith("Low")
        else "middle"
        if action.endswith("Middle")
        else "high"
        if action.endswith("High")
        else "none"
    )
    return actual != expected


def flatten_episode(item: dict[str, Any]) -> dict[str, Any]:
    row = {}
    for key, value in item.items():
        if isinstance(value, dict) and {"x", "y", "z"} <= set(value):
            row[f"{key}_x"] = value["x"]
            row[f"{key}_y"] = value["y"]
            row[f"{key}_z"] = value["z"]
        elif key == "accepted_action_counts":
            for index, count in enumerate(value):
                row[f"accepted_action_count_{index}"] = count
        else:
            row[key] = value
    return row


def write_episodes_csv(path: Path, episodes: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    rows = [flatten_episode(item) for item in episodes]
    if not rows:
        path.write_text("", encoding="utf-8")
        return
    fieldnames = sorted({key for row in rows for key in row})
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def write_summary(path: Path, report: dict[str, Any]) -> None:
    lines = [
        f"# {report['benchmark_id']}",
        "",
        f"Run: `{report['run_id']}`",
        f"Generated: `{report['generated_at']}`",
        "",
        "| Policy | Attempts | Save Rate | Goal Rate | Invalid | Timeout |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for policy in report["policies"]:
        lines.append(
            "| {policy} | {attempts} | {save:.3f} | {goal:.3f} | {invalid:.3f} | {timeout:.3f} |".format(
                policy=policy["policy"],
                attempts=policy["attempts"],
                save=policy["save_rate"]["value"],
                goal=policy["goal_rate"]["value"],
                invalid=policy["invalid_rate"]["value"],
                timeout=policy["timeout_rate"]["value"],
            )
        )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def build_report(
    *,
    config: BenchmarkConfig,
    run_id: str,
    build_path: Path,
    attempts_per_arena: int,
    policy_reports: list[dict[str, Any]],
    started_at: float,
) -> dict[str, Any]:
    full = attempts_per_arena == config.attempts_per_arena
    comparisons = compare_trained_to_baselines(policy_reports)
    passed = bool(comparisons) and all(item["passed"] for item in comparisons) and all(
        policy["invalid_rate"]["successes"] == 0 and policy["timeout_rate"]["successes"] == 0
        for policy in policy_reports
    )
    status = (
        "passed"
        if passed and full
        else "smoke run; full Stage 3 gate not evaluated"
        if not full
        else "no trained checkpoint policy evaluated"
        if not comparisons
        else "failed"
    )
    return {
        "schema_version": 1,
        "benchmark_id": config.benchmark_id,
        "environment_id": config.environment_id,
        "behavior_name": config.behavior_name,
        "observation_spec_id": config.observation_spec_id,
        "reward_spec_id": config.reward_spec_id,
        "build": str(build_path),
        "run_id": run_id,
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "duration_seconds": time.time() - started_at,
        "python": platform.python_version(),
        "python_architecture": platform.machine(),
        "arena_count": config.arena_count,
        "attempts_per_arena": attempts_per_arena,
        "total_attempts": config.arena_count * attempts_per_arena,
        "full_benchmark": full,
        "primary_metric": config.primary_metric,
        "minimum_trained_margin_vs_baselines": 0.05,
        "comparisons": comparisons,
        "passed": passed and full,
        "status": status,
        "policies": policy_reports,
    }


def compare_trained_to_baselines(policy_reports: list[dict[str, Any]]) -> list[dict[str, Any]]:
    baselines = {
        item["policy"]: item
        for item in policy_reports
        if item["policy"] in {"stand_center", "random_legal"}
    }
    trained = [item for item in policy_reports if item["policy_type"] == "onnx"]
    comparisons = []
    for policy in trained:
        for baseline_name, baseline in sorted(baselines.items()):
            margin = policy["save_rate"]["value"] - baseline["save_rate"]["value"]
            comparisons.append(
                {
                    "policy": policy["policy"],
                    "baseline": baseline_name,
                    "save_rate_margin": margin,
                    "passed": margin >= 0.05,
                }
            )
    return comparisons


def run_benchmark(args: argparse.Namespace) -> dict[str, Any]:
    started_at = time.time()
    config = load_benchmark_config(args.benchmark)
    attempts_per_arena = args.attempts_per_arena or config.attempts_per_arena
    build_path = args.build.expanduser().resolve()
    if not build_path.exists():
        raise FileNotFoundError(build_path)

    run_id = args.run_id or f"stage3-{datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S')}"
    output_dir = args.output_root / run_id
    output_dir.mkdir(parents=True, exist_ok=True)

    all_episodes: list[dict[str, Any]] = []
    policy_reports: list[dict[str, Any]] = []
    for index, policy_spec in enumerate(args.policy):
        policy = make_policy(policy_spec, config.master_seed + index)
        episodes = run_policy(
            build_path=build_path,
            config=config,
            policy=policy,
            attempts_per_arena=attempts_per_arena,
            worker_id=args.worker_id_start + index,
            player_log_path=output_dir / "player_logs" / f"{safe_name(policy.name)}.log",
            timeout_wait=args.timeout_wait,
            max_environment_steps=args.max_environment_steps,
        )
        all_episodes.extend(episodes)
        policy_reports.append(
            aggregate_policy(policy, episodes, config, attempts_per_arena)
        )

    report = build_report(
        config=config,
        run_id=run_id,
        build_path=build_path,
        attempts_per_arena=attempts_per_arena,
        policy_reports=policy_reports,
        started_at=started_at,
    )
    write_episodes_csv(output_dir / "episodes.csv", all_episodes)
    (output_dir / "report.json").write_text(
        json.dumps(report, indent=2) + "\n",
        encoding="utf-8",
    )
    write_summary(output_dir / "summary.md", report)
    if args.canonical_report is not None:
        args.canonical_report.parent.mkdir(parents=True, exist_ok=True)
        args.canonical_report.write_text(
            json.dumps(compact_report(report), indent=2) + "\n",
            encoding="utf-8",
        )
    return report


def compact_report(report: dict[str, Any]) -> dict[str, Any]:
    return {
        "schema_version": report["schema_version"],
        "benchmark_id": report["benchmark_id"],
        "environment_id": report["environment_id"],
        "behavior_name": report["behavior_name"],
        "observation_spec_id": report["observation_spec_id"],
        "reward_spec_id": report["reward_spec_id"],
        "run_id": report["run_id"],
        "generated_at": report["generated_at"],
        "full_benchmark": report["full_benchmark"],
        "arena_count": report["arena_count"],
        "attempts_per_arena": report["attempts_per_arena"],
        "total_attempts": report["total_attempts"],
        "primary_metric": report["primary_metric"],
        "comparisons": report["comparisons"],
        "passed": report["passed"],
        "status": report["status"],
        "policies": [
            {
                "policy": policy["policy"],
                "policy_type": policy["policy_type"],
                "attempts": policy["attempts"],
                "expected_attempts": policy["expected_attempts"],
                "complete": policy["complete"],
                "outcomes": policy["outcomes"],
                "save_rate": policy["save_rate"],
                "goal_rate": policy["goal_rate"],
                "invalid_rate": policy["invalid_rate"],
                "timeout_rate": policy["timeout_rate"],
                "glove_contact_rate": policy["glove_contact_rate"],
                "goalkeeper_contact_rate": policy["goalkeeper_contact_rate"],
                "contact_then_goal_rate": policy["contact_then_goal_rate"],
                "wrong_side_rate": policy["wrong_side_rate"],
                "wrong_height_rate": policy["wrong_height_rate"],
                "action_mask_violations": policy["action_mask_violations"],
                "duplicate_terminal_events": policy["duplicate_terminal_events"],
                "action_usage": policy["action_usage"],
                "by_quadrant": policy["by_quadrant"],
                "by_height_band": policy["by_height_band"],
                "by_horizontal_band": policy["by_horizontal_band"],
                "by_flight_time_band": policy["by_flight_time_band"],
                "by_first_dive_action": policy["by_first_dive_action"],
            }
            for policy in report["policies"]
        ],
    }


def safe_name(value: str) -> str:
    return "".join(character if character.isalnum() else "_" for character in value)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--benchmark",
        type=Path,
        default=Path("configs/benchmarks/goalkeeper-state-v0-id-20k.json"),
    )
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument(
        "--policy",
        action="append",
        default=[],
        help="Policy spec: stand_center, random_legal, reactive_side, linear_intercept, or onnx:path",
    )
    parser.add_argument("--attempts-per-arena", type=int)
    parser.add_argument("--run-id")
    parser.add_argument("--output-root", type=Path, default=Path("results/evaluations"))
    parser.add_argument("--canonical-report", type=Path)
    parser.add_argument("--worker-id-start", type=int, default=120)
    parser.add_argument("--timeout-wait", type=int, default=120)
    parser.add_argument("--max-environment-steps", type=int, default=2_000_000)
    args = parser.parse_args()
    if not args.policy:
        args.policy = ["stand_center", "random_legal"]
    return args


def main() -> int:
    report = run_benchmark(parse_args())
    print(json.dumps(compact_report(report), indent=2))
    return 0 if report["status"] != "failed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
