"""Validate the paired Stage 8 heatmap source evaluation."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
from collections import Counter
from pathlib import Path
from typing import Any


SCHEMA_ID = "stage8-heatmap-source-v1"
REQUIRED_POLICIES = (
    "reactive_curve_v1",
    "native_split_v1:seed-001",
)
SHOT_IDENTITY_FIELDS = (
    "seed",
    "shot_style",
    "mixture_component_id",
    "expected_on_target",
    "expected_target_class",
    "intended_target_local_x",
    "intended_target_local_y",
    "intended_target_local_z",
    "predicted_unopposed_crossing_local_x",
    "predicted_unopposed_crossing_local_y",
    "predicted_unopposed_crossing_local_z",
    "launch_speed_mps",
    "launch_angular_velocity_local_x",
    "launch_angular_velocity_local_y",
    "launch_angular_velocity_local_z",
    "curve_displacement",
)
SAFETY_COUNT_FIELDS = (
    "action_mask_violations",
    "duplicate_terminal_events",
    "control_command_clamp_count",
    "policy_decision_duplicate_request_count",
    "policy_decision_missing_action_count",
    "native_inference_invalid_output_count",
)
CONTRACT_FIELDS = (
    "environment_id",
    "behavior_name",
    "observation_spec_id",
    "reward_spec_id",
    "action_spec_id",
    "motor_profile_id",
    "motor_contract_id",
    "scenario_suite_id",
    "shot_contract_id",
    "shot_physics_id",
    "glove_handling_id",
    "glove_geometry_id",
    "primary_population",
)


def _episode_key(row: dict[str, str]) -> tuple[int, int]:
    return int(row["arena_id"]), int(row["attempt_id"])


def _count(value: Any) -> int:
    if value in (None, ""):
        return 0
    return int(float(value))


def _rate_value(policy: dict[str, Any], field: str) -> float:
    value = policy[field]
    return float(value["value"] if isinstance(value, dict) else value)


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def validate_source(
    *,
    report_path: Path,
    episodes_path: Path,
    benchmark_path: Path | None = None,
    expected_attempts_per_policy: int = 20_000,
) -> dict[str, Any]:
    report = json.loads(report_path.read_text(encoding="utf-8"))
    benchmark = (
        json.loads(benchmark_path.read_text(encoding="utf-8"))
        if benchmark_path is not None
        else None
    )
    with episodes_path.open(newline="", encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))

    failures: list[str] = []
    if report.get("benchmark_id") != "goalkeeper-control-v2-stage8-heatmap-source-20k":
        failures.append("unexpected benchmark_id")
    if report.get("primary_population") != "expected_on_target":
        failures.append("primary population is not expected_on_target")
    if report.get("glove_handling_id") != "keeper-glove-handling-v1":
        failures.append("report does not select keeper-glove-handling-v1")
    if float(report.get("environment_parameters", {}).get(
        "stage6.glove_handling_version", -1
    )) != 1.0:
        failures.append("glove handling version is not 1")
    if benchmark is not None:
        if int(benchmark.get("master_seed", -1)) != 20260803:
            failures.append("benchmark master seed is not canonical")
        for field in CONTRACT_FIELDS:
            if report.get(field) != benchmark.get(field):
                failures.append(f"report {field} differs from benchmark config")
        for field in ("arena_count", "attempts_per_arena", "total_attempts"):
            if int(report.get(field, -1)) != int(benchmark.get(field, -2)):
                failures.append(f"report {field} differs from benchmark config")
        report_parameters = report.get("environment_parameters", {})
        for key, expected in benchmark.get("environment_parameters", {}).items():
            if float(report_parameters.get(key, float("nan"))) != float(expected):
                failures.append(f"report environment parameter {key} differs")

    source_policy_reports = {
        item["policy"]: item for item in report.get("policies", [])
    }
    missing_policies = set(REQUIRED_POLICIES) - set(source_policy_reports)
    if missing_policies:
        failures.append(
            "required policy set is incomplete: "
            + ", ".join(sorted(missing_policies))
        )

    rows_by_policy: dict[str, list[dict[str, str]]] = {
        policy: [] for policy in REQUIRED_POLICIES
    }
    for row in rows:
        policy = row.get("policy", "")
        if policy in rows_by_policy:
            rows_by_policy[policy].append(row)

    reference_shots: dict[tuple[int, int], tuple[str, ...]] | None = None
    reference_digest: str | None = None
    policy_manifest: list[dict[str, Any]] = []
    for policy in REQUIRED_POLICIES:
        policy_rows = rows_by_policy[policy]
        policy_report = source_policy_reports.get(policy)
        if policy_report is None:
            continue
        if len(policy_rows) != expected_attempts_per_policy:
            failures.append(
                f"{policy} has {len(policy_rows)} rows, expected "
                f"{expected_attempts_per_policy}"
            )
        if not bool(policy_report.get("complete", False)):
            failures.append(f"{policy} is incomplete")
        if int(policy_report.get("attempts", -1)) != expected_attempts_per_policy:
            failures.append(f"{policy} report attempt count is incorrect")

        keyed: dict[tuple[int, int], tuple[str, ...]] = {}
        for row in policy_rows:
            key = _episode_key(row)
            if key in keyed:
                failures.append(f"{policy} contains duplicate episode key {key}")
                continue
            keyed[key] = tuple(row.get(field, "") for field in SHOT_IDENTITY_FIELDS)
            if row.get("glove_handling_enabled", "").lower() != "true":
                failures.append(f"{policy} has glove handling disabled at {key}")
            if row.get("glove_handling_id") != "keeper-glove-handling-v1":
                failures.append(f"{policy} has the wrong glove contract at {key}")

        if reference_shots is None:
            reference_shots = keyed
        elif keyed != reference_shots:
            failures.append(f"{policy} did not receive the identical fixed shots")

        digest = str(policy_report.get("episode_key_digest", ""))
        if reference_digest is None:
            reference_digest = digest
        elif digest != reference_digest:
            failures.append(f"{policy} episode-key digest differs")

        safety_counts = {
            field: sum(_count(row.get(field)) for row in policy_rows)
            for field in SAFETY_COUNT_FIELDS
        }
        nonzero_safety = {
            field: count for field, count in safety_counts.items() if count != 0
        }
        if nonzero_safety:
            failures.append(f"{policy} safety failures: {nonzero_safety}")
        if _count(policy_report.get("policy_decision_request_count")) != (
            _count(policy_report.get("policy_decision_consumed_count"))
            + _count(policy_report.get("policy_decision_discarded_count"))
        ):
            failures.append(f"{policy} decision lifecycle is unbalanced")
        if _count(policy_report.get("outcomes", {}).get("Invalid")):
            failures.append(f"{policy} contains invalid outcomes")
        if _count(policy_report.get("outcomes", {}).get("Timeout")):
            failures.append(f"{policy} contains timeout outcomes")

        policy_manifest.append(
            {
                "policy": policy,
                "attempts": len(policy_rows),
                "expected_on_target_attempts": int(
                    policy_report.get("primary_population_attempts", 0)
                ),
                "episode_key_digest": digest,
                "save_rate": _rate_value(policy_report, "save_rate"),
                "glove_contact_rate": _rate_value(
                    policy_report, "glove_contact_rate"
                ),
                "safety_counts": safety_counts,
            }
        )

    manifest = {
        "schema_id": SCHEMA_ID,
        "status": "passed" if not failures else "failed",
        "benchmark_id": report.get("benchmark_id"),
        "master_seed": int(benchmark.get("master_seed", 20260803))
        if benchmark is not None
        else 20260803,
        "attempts_per_policy": expected_attempts_per_policy,
        "selected_rows": sum(len(items) for items in rows_by_policy.values()),
        "source_rows": len(rows),
        "ignored_source_policies": sorted(
            set(source_policy_reports) - set(REQUIRED_POLICIES)
        ),
        "episode_key_digest": reference_digest,
        "source_hashes": {
            "report_sha256": _sha256(report_path),
            "episodes_sha256": _sha256(episodes_path),
            "benchmark_config_sha256": _sha256(benchmark_path)
            if benchmark_path is not None
            else "",
        },
        "policies": policy_manifest,
        "heatmap_fields": {
            "intended_target": [
                "intended_target_local_x",
                "intended_target_local_y",
            ],
            "unopposed_crossing": [
                "predicted_unopposed_crossing_local_x",
                "predicted_unopposed_crossing_local_y",
            ],
            "filters": [
                "shot_style",
                "launch_speed_mps",
                "launch_angular_velocity_local_x",
                "launch_angular_velocity_local_y",
                "launch_angular_velocity_local_z",
            ],
        },
        "failures": failures,
    }
    if failures:
        raise RuntimeError("Stage 8 source validation failed: " + "; ".join(failures[:10]))
    return manifest


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--episodes", type=Path, required=True)
    parser.add_argument("--benchmark", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--attempts-per-policy", type=int, default=20_000)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    manifest = validate_source(
        report_path=args.report,
        episodes_path=args.episodes,
        benchmark_path=args.benchmark,
        expected_attempts_per_policy=args.attempts_per_policy,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Stage 8 heatmap source validation passed: {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
