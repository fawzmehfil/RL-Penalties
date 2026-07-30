from __future__ import annotations

import argparse
import hashlib
import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Sequence

import numpy as np
from mlagents.trainers.demo_loader import load_demonstration


DEFAULT_CONTRACT = Path(
    "configs/demonstrations/"
    "goalkeeper-control-v2-reactive-demo-v1.json"
)
CAN_COMMIT_OBSERVATION_INDEX = 29


@dataclass(frozen=True)
class DemonstrationValidation:
    manifest: dict[str, Any]
    errors: tuple[str, ...]

    @property
    def passed(self) -> bool:
        return not self.errors


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def validate_demonstrations(
    demo_dir: Path,
    contract_path: Path = DEFAULT_CONTRACT,
    teacher_report_path: Path | None = None,
) -> DemonstrationValidation:
    demo_dir = demo_dir.expanduser().resolve()
    contract_path = contract_path.expanduser().resolve()
    teacher_report_path = (
        teacher_report_path.expanduser().resolve()
        if teacher_report_path is not None
        else demo_dir / "teacher-report.json"
    )
    contract = load_json(contract_path)
    errors: list[str] = []
    demo_files = sorted(demo_dir.glob("*.demo"))
    if not demo_files:
        errors.append(f"no .demo files found under {demo_dir}")
        return DemonstrationValidation(
            _empty_manifest(contract, demo_dir, errors),
            tuple(errors),
        )

    try:
        behavior_spec, pairs, metadata_steps = load_demonstration(
            str(demo_dir)
        )
    except Exception as exception:
        errors.append(f"ML-Agents could not load demonstrations: {exception}")
        return DemonstrationValidation(
            _empty_manifest(contract, demo_dir, errors),
            tuple(errors),
        )

    expected_files = int(contract["arena_count"])
    if len(demo_files) != expected_files:
        errors.append(
            f"expected {expected_files} demonstration files, "
            f"found {len(demo_files)}"
        )
    expected_names = {
        f"GKCtrlV2A{arena_id:03d}.demo"
        for arena_id in range(expected_files)
    }
    actual_names = {path.name for path in demo_files}
    if actual_names != expected_names:
        errors.append(
            "demonstration filenames do not match arena IDs: "
            f"missing={sorted(expected_names - actual_names)}, "
            f"unexpected={sorted(actual_names - expected_names)}"
        )

    observation_shapes = [
        list(spec.shape)
        for spec in behavior_spec.observation_specs
    ]
    expected_shapes = contract["observation_shapes"]
    if observation_shapes != expected_shapes:
        errors.append(
            f"observation shapes {observation_shapes} != {expected_shapes}"
        )

    action_spec = behavior_spec.action_spec
    continuous_actions = int(action_spec.continuous_size)
    discrete_branches = [
        int(value)
        for value in action_spec.discrete_branches
    ]
    if continuous_actions != int(contract["continuous_actions"]):
        errors.append(
            f"continuous actions {continuous_actions} != "
            f"{contract['continuous_actions']}"
        )
    if discrete_branches != contract["discrete_branches"]:
        errors.append(
            f"discrete branches {discrete_branches} != "
            f"{contract['discrete_branches']}"
        )

    expected_commits = int(
        contract["dataset_requirements"]["commits_per_episode"]
    )
    stats = _inspect_pairs(
        pairs,
        continuous_actions,
        expected_commits,
    )
    expected_episodes = int(contract["total_attempts"])
    if stats["terminal_episodes"] != expected_episodes:
        errors.append(
            f"terminal episodes {stats['terminal_episodes']} != "
            f"{expected_episodes}"
        )
    if stats["episodes_with_wrong_commit_count"] != 0:
        errors.append(
            f"{stats['episodes_with_wrong_commit_count']} episodes did not "
            f"contain exactly {expected_commits} commit"
        )
    if stats["illegal_commit_count"] != 0:
        errors.append(
            f"found {stats['illegal_commit_count']} commits while masked"
        )
    if stats["nonfinite_action_count"] != 0:
        errors.append(
            f"found {stats['nonfinite_action_count']} non-finite actions"
        )
    if stats["out_of_range_action_count"] != 0:
        errors.append(
            f"found {stats['out_of_range_action_count']} actions outside "
            "[-1, 1]"
        )

    requirements = contract["dataset_requirements"]
    coverage = stats["continuous_action_coverage"]
    _require_minimum(
        errors,
        "aim_x minimum",
        coverage["aim_x"]["minimum"],
        float(requirements["minimum_aim_x"]),
        maximum=False,
    )
    _require_minimum(
        errors,
        "aim_x maximum",
        coverage["aim_x"]["maximum"],
        float(requirements["maximum_aim_x"]),
        maximum=True,
    )
    _require_minimum(
        errors,
        "aim_y minimum",
        coverage["aim_y"]["minimum"],
        float(requirements["minimum_aim_y"]),
        maximum=False,
    )
    _require_minimum(
        errors,
        "aim_y maximum",
        coverage["aim_y"]["maximum"],
        float(requirements["maximum_aim_y"]),
        maximum=True,
    )

    teacher_report: dict[str, Any] = {}
    if not teacher_report_path.exists():
        errors.append(f"missing teacher report: {teacher_report_path}")
    else:
        teacher_report = load_json(teacher_report_path)
        _validate_teacher_report(
            teacher_report,
            contract,
            errors,
        )

    file_entries = [
        {
            "path": path.name,
            "bytes": path.stat().st_size,
            "sha256": _sha256(path),
        }
        for path in demo_files
    ]
    manifest = {
        "schema_version": 1,
        "demonstration_contract_id":
            contract["demonstration_contract_id"],
        "status": "passed" if not errors else "failed",
        "demo_directory": str(demo_dir),
        "behavior_name": contract["behavior_name"],
        "observation_spec_id": contract["observation_spec_id"],
        "action_spec_id": contract["action_spec_id"],
        "scenario_suite_id": contract["scenario_suite_id"],
        "observation_shapes": observation_shapes,
        "continuous_actions": continuous_actions,
        "discrete_branches": discrete_branches,
        "demonstration_files": file_entries,
        "metadata_steps": int(metadata_steps),
        "decision_steps": stats["decision_steps"],
        "terminal_episodes": stats["terminal_episodes"],
        "commit_actions": stats["commit_actions"],
        "commit_action_rate": stats["commit_action_rate"],
        "episodes_with_wrong_commit_count":
            stats["episodes_with_wrong_commit_count"],
        "illegal_commit_count": stats["illegal_commit_count"],
        "nonfinite_action_count": stats["nonfinite_action_count"],
        "out_of_range_action_count":
            stats["out_of_range_action_count"],
        "continuous_action_coverage": coverage,
        "teacher_quality": {
            key: teacher_report.get(key)
            for key in (
                "total_attempts",
                "save_rate",
                "glove_contact_rate",
                "high_shot_save_rate",
                "invalids",
                "timeouts",
                "action_mask_violations",
                "control_command_clamps",
                "policy_decision_duplicate_requests",
                "policy_decision_missing_actions",
            )
        },
        "errors": errors,
    }
    return DemonstrationValidation(manifest, tuple(errors))


def _inspect_pairs(
    pairs: Sequence[Any],
    continuous_actions: int,
    expected_commits_per_episode: int = 1,
) -> dict[str, Any]:
    action_rows: list[list[float]] = []
    terminal_episodes = 0
    commit_actions = 0
    commits_in_episode = 0
    episodes_with_wrong_commit_count = 0
    illegal_commit_count = 0
    nonfinite_action_count = 0
    out_of_range_action_count = 0
    for pair in pairs:
        info = pair.agent_info
        if bool(info.done):
            terminal_episodes += 1
            if commits_in_episode != expected_commits_per_episode:
                episodes_with_wrong_commit_count += 1
            commits_in_episode = 0
            continue

        continuous = list(pair.action_info.continuous_actions)
        discrete = list(pair.action_info.discrete_actions)
        if len(continuous) != continuous_actions or len(discrete) != 1:
            nonfinite_action_count += 1
            continue
        action_rows.append([float(value) for value in continuous])
        if not all(math.isfinite(float(value)) for value in continuous):
            nonfinite_action_count += 1
        if any(abs(float(value)) > 1.000001 for value in continuous):
            out_of_range_action_count += 1
        commit = int(discrete[0])
        if commit not in (0, 1):
            out_of_range_action_count += 1
            continue
        if commit == 1:
            commit_actions += 1
            commits_in_episode += 1
            observation = _vector_observation(info)
            if (
                len(observation) <= CAN_COMMIT_OBSERVATION_INDEX
                or observation[CAN_COMMIT_OBSERVATION_INDEX] < 0.5
            ):
                illegal_commit_count += 1

    if commits_in_episode != 0:
        episodes_with_wrong_commit_count += 1
    actions = np.asarray(action_rows, dtype=np.float64)
    labels = ("move_x", "aim_x", "aim_y", "reach")
    coverage = {}
    for index, label in enumerate(labels):
        values = (
            actions[:, index]
            if actions.size
            else np.asarray([], dtype=np.float64)
        )
        coverage[label] = {
            "minimum": float(np.min(values)) if values.size else 0.0,
            "maximum": float(np.max(values)) if values.size else 0.0,
            "mean": float(np.mean(values)) if values.size else 0.0,
            "standard_deviation": (
                float(np.std(values)) if values.size else 0.0
            ),
        }
    decision_steps = len(action_rows)
    return {
        "decision_steps": decision_steps,
        "terminal_episodes": terminal_episodes,
        "commit_actions": commit_actions,
        "commit_action_rate": (
            commit_actions / decision_steps
            if decision_steps
            else 0.0
        ),
        "episodes_with_wrong_commit_count":
            episodes_with_wrong_commit_count,
        "illegal_commit_count": illegal_commit_count,
        "nonfinite_action_count": nonfinite_action_count,
        "out_of_range_action_count": out_of_range_action_count,
        "continuous_action_coverage": coverage,
    }


def _vector_observation(info: Any) -> list[float]:
    if not info.observations:
        return []
    return [
        float(value)
        for value in info.observations[0].float_data.data
    ]


def _validate_teacher_report(
    report: dict[str, Any],
    contract: dict[str, Any],
    errors: list[str],
) -> None:
    identity_fields = (
        "demonstration_contract_id",
        "behavior_name",
        "observation_spec_id",
        "action_spec_id",
        "scenario_suite_id",
    )
    for key in identity_fields:
        if report.get(key) != contract.get(key):
            errors.append(
                f"teacher report {key} {report.get(key)!r} != "
                f"{contract.get(key)!r}"
            )
    if str(report.get("master_seed")) != str(contract["master_seed"]):
        errors.append(
            f"teacher report master_seed {report.get('master_seed')} != "
            f"{contract['master_seed']}"
        )
    for key in ("arena_count", "attempts_per_arena"):
        if int(report.get(key, -1)) != int(contract[key]):
            errors.append(
                f"teacher report {key} {report.get(key)} != "
                f"{contract[key]}"
            )

    expected_attempts = int(contract["total_attempts"])
    if int(report.get("total_attempts", -1)) != expected_attempts:
        errors.append(
            f"teacher report attempts {report.get('total_attempts')} != "
            f"{expected_attempts}"
        )
    quality = contract["minimum_teacher_quality"]
    for key, minimum in quality.items():
        value = float(report.get(key, -1.0))
        if value < float(minimum):
            errors.append(
                f"teacher {key} {value:.6f} is below {minimum:.6f}"
            )
    requirements = contract["dataset_requirements"]
    zero_fields = {
        "invalids": "maximum_invalids",
        "timeouts": "maximum_timeouts",
        "off_target": "maximum_off_target",
        "action_mask_violations": "maximum_action_mask_violations",
        "control_command_clamps": "maximum_control_command_clamps",
        "policy_decision_duplicate_requests":
            "maximum_duplicate_decision_requests",
        "policy_decision_missing_actions": "maximum_missing_actions",
    }
    for report_key, requirement_key in zero_fields.items():
        value = int(report.get(report_key, -1))
        maximum = int(requirements[requirement_key])
        if value < 0 or value > maximum:
            errors.append(
                f"teacher {report_key} {value} exceeds {maximum}"
            )

    expected_arena_ids = set(range(int(contract["arena_count"])))
    arenas = report.get("arenas", [])
    actual_arena_ids = {
        int(arena.get("arena_id", -1))
        for arena in arenas
    }
    if actual_arena_ids != expected_arena_ids:
        errors.append(
            "teacher report arena IDs do not match contract"
        )
    expected_per_arena = int(contract["attempts_per_arena"])
    for arena in arenas:
        if int(arena.get("attempts", -1)) != expected_per_arena:
            errors.append(
                f"teacher arena {arena.get('arena_id')} attempts "
                f"{arena.get('attempts')} != {expected_per_arena}"
            )


def _require_minimum(
    errors: list[str],
    label: str,
    actual: float,
    required: float,
    *,
    maximum: bool,
) -> None:
    passed = actual >= required if maximum else actual <= required
    if not passed:
        relation = ">=" if maximum else "<="
        errors.append(
            f"{label} {actual:.6f} must be {relation} {required:.6f}"
        )


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _empty_manifest(
    contract: dict[str, Any],
    demo_dir: Path,
    errors: list[str],
) -> dict[str, Any]:
    return {
        "schema_version": 1,
        "demonstration_contract_id": contract.get(
            "demonstration_contract_id"
        ),
        "status": "failed",
        "demo_directory": str(demo_dir),
        "errors": errors,
    }


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Validate Stage 5 reactive ML-Agents demonstrations."
    )
    parser.add_argument(
        "--demo-dir",
        type=Path,
        required=True,
    )
    parser.add_argument(
        "--contract",
        type=Path,
        default=DEFAULT_CONTRACT,
    )
    parser.add_argument(
        "--teacher-report",
        type=Path,
    )
    parser.add_argument(
        "--manifest",
        type=Path,
    )
    return parser


def main() -> int:
    args = build_parser().parse_args()
    validation = validate_demonstrations(
        args.demo_dir,
        args.contract,
        args.teacher_report,
    )
    manifest_path = (
        args.manifest.expanduser().resolve()
        if args.manifest is not None
        else args.demo_dir.expanduser().resolve() / "manifest.json"
    )
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(validation.manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    if validation.errors:
        for error in validation.errors:
            print(f"FAIL: {error}")
        return 1
    print(
        "Validated "
        f"{validation.manifest['terminal_episodes']} episodes and "
        f"{validation.manifest['decision_steps']} decisions."
    )
    print(manifest_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
