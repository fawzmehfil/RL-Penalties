"""Repeatable ML-Agents connection probe for the Stage 1 kernel build."""

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


BEHAVIOR_NAME = "GoalkeeperKernel-v0"


@dataclass
class ProbeRun:
    worker_id: int
    behavior_name: str
    observation_shapes: list[list[int]]
    discrete_branches: list[int]
    decisions_seen: int
    terminal_steps_seen: int
    passed: bool


def run_once(build_path: Path, worker_id: int, max_steps: int = 300) -> ProbeRun:
    env = UnityEnvironment(
        file_name=str(build_path),
        worker_id=worker_id,
        seed=20260723,
        no_graphics=True,
        timeout_wait=90,
        additional_args=["-batchmode", "-nographics"],
    )
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
        if specification.action_spec.continuous_size != 0 or branches != [9]:
            raise RuntimeError(
                f"Unexpected Stage 1 action specification: {specification.action_spec}"
            )

        observation_shapes = [
            list(observation.shape)
            for observation in specification.observation_specs
        ]
        if observation_shapes != [[1]]:
            raise RuntimeError(
                "Stage 1 must publish only its one-value transport probe before "
                "the Stage 2 observation contract: "
                f"{observation_shapes}"
            )

        decisions_seen = 0
        terminal_steps_seen = 0
        for step in range(max_steps):
            decision_steps, terminal_steps = env.get_steps(behavior_name)
            terminal_steps_seen += len(terminal_steps)
            if len(terminal_steps):
                return ProbeRun(
                    worker_id=worker_id,
                    behavior_name=behavior_name,
                    observation_shapes=observation_shapes,
                    discrete_branches=branches,
                    decisions_seen=decisions_seen,
                    terminal_steps_seen=terminal_steps_seen,
                    passed=True,
                )

            if len(decision_steps):
                decisions_seen += len(decision_steps)
                action = np.full(
                    (len(decision_steps), 1),
                    step % 9,
                    dtype=np.int32,
                )
                env.set_actions(
                    behavior_name,
                    ActionTuple(discrete=action),
                )
            env.step()

        raise RuntimeError(f"No terminal step after {max_steps} environment steps")
    finally:
        env.close()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--platform", choices=("macos", "linux"), required=True)
    parser.add_argument("--repeats", type=int, default=3)
    parser.add_argument("--worker-id-start", type=int, default=40)
    parser.add_argument(
        "--report",
        type=Path,
        default=Path("docs/stage1-connection-report.json"),
    )
    args = parser.parse_args()

    build_path = args.build.resolve()
    if not build_path.exists():
        raise FileNotFoundError(build_path)
    if args.repeats < 1:
        raise ValueError("--repeats must be positive")

    runs = [
        run_once(build_path, args.worker_id_start + index)
        for index in range(args.repeats)
    ]
    try:
        reported_build = str(build_path.relative_to(Path.cwd().resolve()))
    except ValueError:
        reported_build = str(build_path)

    report = {
        "environment_id": "penalty-shootout-kernel-v1",
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
    args.report.write_text(
        json.dumps(report, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(report, indent=2))
    return 0 if report["all_passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
