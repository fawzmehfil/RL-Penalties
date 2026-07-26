"""Repeatable ML-Agents connection and baseline probe for Stage 2."""

from __future__ import annotations

import argparse
import json
import platform
from dataclasses import asdict, dataclass
from importlib.metadata import version
from pathlib import Path

import numpy as np
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel

from penalty_shootout.baselines import RandomLegal, StandCenter


BEHAVIOR_NAME = "GoalkeeperState-v0"
OBSERVATION_SHAPES = [[24]]
DISCRETE_BRANCHES = [9]


@dataclass
class ProbeRun:
    worker_id: int
    behavior_name: str
    observation_shapes: list[list[int]]
    discrete_branches: list[int]
    baseline: str
    decisions_seen: int
    terminal_steps_seen: int
    rewards_seen: list[float]
    passed: bool


def run_once(
    build_path: Path,
    worker_id: int,
    baseline_name: str,
    max_steps: int = 500,
) -> ProbeRun:
    env = UnityEnvironment(
        file_name=str(build_path),
        worker_id=worker_id,
        seed=20260724,
        no_graphics=True,
        timeout_wait=90,
        additional_args=["-batchmode", "-nographics"],
        side_channels=[StatsSideChannel()],
    )
    policy = StandCenter() if baseline_name == "stand_center" else RandomLegal(worker_id)
    try:
        env.reset()
        behavior_names = list(env.behavior_specs)
        if (
            len(behavior_names) != 1
            or behavior_names[0].split("?", maxsplit=1)[0] != BEHAVIOR_NAME
        ):
            raise RuntimeError(f"Unexpected behavior names: {behavior_names}")

        behavior_name = behavior_names[0]
        specification = env.behavior_specs[behavior_name]
        branches = list(specification.action_spec.discrete_branches)
        if specification.action_spec.continuous_size != 0 or branches != DISCRETE_BRANCHES:
            raise RuntimeError(f"Unexpected Stage 2 action specification: {specification.action_spec}")

        observation_shapes = [list(observation.shape) for observation in specification.observation_specs]
        if observation_shapes != OBSERVATION_SHAPES:
            raise RuntimeError(f"Unexpected Stage 2 observation shapes: {observation_shapes}")

        decisions_seen = 0
        terminal_steps_seen = 0
        rewards_seen: list[float] = []
        for _ in range(max_steps):
            decision_steps, terminal_steps = env.get_steps(behavior_name)
            if len(terminal_steps):
                terminal_steps_seen += len(terminal_steps)
                rewards_seen.extend(float(reward) for reward in terminal_steps.reward)
                if not set(rewards_seen).issubset({-1.0, 0.0, 1.0}):
                    raise RuntimeError(f"Unexpected sparse rewards: {rewards_seen}")
                return ProbeRun(
                    worker_id=worker_id,
                    behavior_name=behavior_name,
                    observation_shapes=observation_shapes,
                    discrete_branches=branches,
                    baseline=baseline_name,
                    decisions_seen=decisions_seen,
                    terminal_steps_seen=terminal_steps_seen,
                    rewards_seen=rewards_seen,
                    passed=True,
                )

            if len(decision_steps):
                decisions_seen += len(decision_steps)
                actions = np.zeros((len(decision_steps), 1), dtype=np.int32)
                mask = decision_steps.action_mask[0] if decision_steps.action_mask else None
                for index in range(len(decision_steps)):
                    row_mask = None if mask is None else mask[index]
                    actions[index, 0] = policy.act(row_mask)
                env.set_actions(behavior_name, ActionTuple(discrete=actions))
            env.step()

        raise RuntimeError(f"No terminal step after {max_steps} environment steps")
    finally:
        env.close()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--platform", choices=("macos", "linux"), required=True)
    parser.add_argument("--repeats", type=int, default=2)
    parser.add_argument("--worker-id-start", type=int, default=60)
    parser.add_argument("--report", type=Path, default=Path("docs/stage2-connection-report.json"))
    args = parser.parse_args()

    build_path = args.build.resolve()
    if not build_path.exists():
        raise FileNotFoundError(build_path)

    runs: list[ProbeRun] = []
    for index in range(args.repeats):
        baseline = "stand_center" if index % 2 == 0 else "random_legal"
        runs.append(run_once(build_path, args.worker_id_start + index, baseline))

    try:
        reported_build = str(build_path.relative_to(Path.cwd().resolve()))
    except ValueError:
        reported_build = str(build_path)

    report = {
        "environment_id": "penalty-shootout-kernel-v1",
        "behavior_name": BEHAVIOR_NAME,
        "observation_spec_id": "state-v0",
        "reward_spec_id": "goalkeeper-sparse-v0",
        "platform": args.platform,
        "build": reported_build,
        "python": platform.python_version(),
        "python_architecture": platform.machine(),
        "mlagents": version("mlagents"),
        "mlagents_envs": version("mlagents-envs"),
        "repeats": args.repeats,
        "all_passed": all(run.passed for run in runs),
        "runs": [asdict(run) for run in runs],
    }

    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if report["all_passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
