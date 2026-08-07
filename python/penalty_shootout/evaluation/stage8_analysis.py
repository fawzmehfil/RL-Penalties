"""Build the deterministic Stage 8 analysis artifact from paired benchmark rows."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Iterable

from penalty_shootout.evaluation.goalkeeper import rate
from penalty_shootout.evaluation.stage8_heatmap_source import (
    SAFETY_COUNT_FIELDS,
    SHOT_IDENTITY_FIELDS,
)


SCHEMA_ID = "goalkeeper-analysis-v1"
SAVE_OUTCOMES = frozenset({"Saved", "BlockedThenOut"})
LOCATION_FIELDS = {
    "intended_target": (
        "intended_target_local_x",
        "intended_target_local_y",
    ),
    "unopposed_crossing": (
        "predicted_unopposed_crossing_local_x",
        "predicted_unopposed_crossing_local_y",
    ),
}
HEIGHT_BANDS = ("low", "middle", "high")
HORIZONTAL_BANDS = ("left", "right")
STYLE_BANDS = ("placed", "power", "curled")
SPEED_BANDS = ("slow", "medium", "fast")
SPIN_BANDS = ("low", "moderate", "high")


@dataclass(frozen=True)
class AnalysisConfig:
    analysis_id: str
    source_benchmark_id: str
    master_seed: int
    primary_population: str
    final_policy: str
    teacher_policy: str
    minimum_x: float
    maximum_x: float
    minimum_y: float
    maximum_y: float
    assignment_tolerance_m: float
    location_bases: tuple[str, ...]
    style_filters: tuple[str, ...]
    speed_filters: tuple[str, ...]
    slow_max_exclusive: float
    medium_max_exclusive: float


def load_analysis_config(path: Path) -> AnalysisConfig:
    payload = json.loads(path.read_text(encoding="utf-8"))
    bounds = payload["goal_bounds"]
    speeds = payload["speed_bands_mps"]
    config = AnalysisConfig(
        analysis_id=str(payload["analysis_id"]),
        source_benchmark_id=str(payload["source_benchmark_id"]),
        master_seed=int(payload["master_seed"]),
        primary_population=str(payload["primary_population"]),
        final_policy=str(payload["final_policy"]),
        teacher_policy=str(payload["teacher_policy"]),
        minimum_x=float(bounds["minimum_x"]),
        maximum_x=float(bounds["maximum_x"]),
        minimum_y=float(bounds["minimum_y"]),
        maximum_y=float(bounds["maximum_y"]),
        assignment_tolerance_m=float(bounds["assignment_tolerance_m"]),
        location_bases=tuple(str(item) for item in payload["location_bases"]),
        style_filters=tuple(str(item) for item in payload["style_filters"]),
        speed_filters=tuple(str(item) for item in payload["speed_filters"]),
        slow_max_exclusive=float(speeds["slow_max_exclusive"]),
        medium_max_exclusive=float(speeds["medium_max_exclusive"]),
    )
    failures: list[str] = []
    if config.analysis_id != SCHEMA_ID:
        failures.append("unexpected analysis_id")
    if config.primary_population != "expected_on_target":
        failures.append("primary_population must be expected_on_target")
    if config.master_seed != 20260803:
        failures.append("master_seed is not canonical")
    if set(config.location_bases) != set(LOCATION_FIELDS):
        failures.append("location bases are incomplete")
    if config.style_filters != ("all",) + STYLE_BANDS:
        failures.append("style filters are not canonical")
    if config.speed_filters != ("all",) + SPEED_BANDS:
        failures.append("speed filters are not canonical")
    if not (
        config.minimum_x < config.maximum_x
        and config.minimum_y < config.maximum_y
        and config.assignment_tolerance_m >= 0.0
    ):
        failures.append("goal bounds are invalid")
    if not (0.0 < config.slow_max_exclusive < config.medium_max_exclusive):
        failures.append("speed thresholds are invalid")
    if config.final_policy == config.teacher_policy:
        failures.append("final and teacher policies must differ")
    if failures:
        raise ValueError("Invalid Stage 8 analysis config: " + "; ".join(failures))
    return config


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _bool(value: Any) -> bool:
    if isinstance(value, bool):
        return value
    normalized = str(value or "").strip().lower()
    if normalized in {"true", "1"}:
        return True
    if normalized in {"false", "0", ""}:
        return False
    raise ValueError(f"invalid boolean value: {value!r}")


def _integer(value: Any) -> int:
    if value in (None, ""):
        return 0
    number = float(value)
    if not math.isfinite(number) or not number.is_integer():
        raise ValueError(f"invalid integer value: {value!r}")
    return int(number)


def _number(row: dict[str, str], field: str) -> float:
    try:
        value = float(row[field])
    except (KeyError, TypeError, ValueError) as error:
        raise ValueError(f"missing or invalid {field}") from error
    if not math.isfinite(value):
        raise ValueError(f"non-finite {field}")
    return value


def _episode_key(row: dict[str, str]) -> tuple[int, int]:
    return _integer(row.get("arena_id")), _integer(row.get("attempt_id"))


def _shot_style(row: dict[str, str]) -> str:
    return str(row.get("shot_style", "")).strip().lower()


def _speed_band(row: dict[str, str], config: AnalysisConfig) -> str:
    speed = _number(row, "launch_speed_mps")
    if speed < config.slow_max_exclusive:
        return "slow"
    if speed < config.medium_max_exclusive:
        return "medium"
    return "fast"


def _spin_band(row: dict[str, str]) -> str:
    side_spin = abs(_number(row, "command_side_spin"))
    if side_spin < 0.20:
        return "low"
    if side_spin < 0.55:
        return "moderate"
    return "high"


def _height_band(row: dict[str, str]) -> str:
    y = _number(row, "predicted_unopposed_crossing_local_y")
    if y < 0.85:
        return "low"
    if y < 1.55:
        return "middle"
    return "high"


def _horizontal_band(row: dict[str, str]) -> str:
    return "left" if _number(
        row, "predicted_unopposed_crossing_local_x"
    ) < 0.0 else "right"


def _metric(rows: Iterable[dict[str, str]], predicate: Callable[[dict[str, str]], bool]) -> dict[str, float | int]:
    materialized = list(rows)
    return rate(sum(1 for row in materialized if predicate(row)), len(materialized))


def _save_metric(rows: Iterable[dict[str, str]]) -> dict[str, float | int]:
    return _metric(rows, lambda row: row.get("outcome") in SAVE_OUTCOMES)


def _glove_metric(rows: Iterable[dict[str, str]]) -> dict[str, float | int]:
    return _metric(rows, lambda row: _bool(row.get("glove_contact")))


def _goal_metric(rows: Iterable[dict[str, str]]) -> dict[str, float | int]:
    return _metric(rows, lambda row: row.get("outcome") == "Goal")


def _glove_save_metric(rows: Iterable[dict[str, str]]) -> dict[str, float | int]:
    return _metric(
        rows,
        lambda row: row.get("outcome") in SAVE_OUTCOMES
        and _bool(row.get("glove_contact")),
    )


def _contact_then_goal_metric(rows: Iterable[dict[str, str]]) -> dict[str, float | int]:
    return _metric(
        rows,
        lambda row: row.get("outcome") == "Goal"
        and _bool(row.get("goalkeeper_contact")),
    )


def _clamped_location(
    row: dict[str, str], basis: str, config: AnalysisConfig
) -> tuple[float, float]:
    x_field, y_field = LOCATION_FIELDS[basis]
    x = _number(row, x_field)
    y = _number(row, y_field)
    if not (
        config.minimum_x - config.assignment_tolerance_m
        <= x
        <= config.maximum_x + config.assignment_tolerance_m
        and config.minimum_y - config.assignment_tolerance_m
        <= y
        <= config.maximum_y + config.assignment_tolerance_m
    ):
        raise ValueError(
            f"{basis} location ({x:.4f}, {y:.4f}) is outside assignment bounds"
        )
    return (
        min(config.maximum_x, max(config.minimum_x, x)),
        min(config.maximum_y, max(config.minimum_y, y)),
    )


def heatmap_cell_id(
    row: dict[str, str], basis: str, config: AnalysisConfig
) -> str:
    x, y = _clamped_location(row, basis, config)
    x_fraction = (x - config.minimum_x) / (config.maximum_x - config.minimum_x)
    y_fraction = (y - config.minimum_y) / (config.maximum_y - config.minimum_y)
    column = min(3, int(x_fraction * 4.0))
    vertical_index = min(2, int(y_fraction * 3.0))
    vertical = ("low", "middle", "upper")[vertical_index]
    horizontal = ("left", "centre-left", "centre-right", "right")[column]
    return f"{vertical}-{horizontal}"


def _filtered(
    rows: Iterable[dict[str, str]],
    *,
    style_filter: str,
    speed_filter: str,
    config: AnalysisConfig,
) -> list[dict[str, str]]:
    return [
        row
        for row in rows
        if (style_filter == "all" or _shot_style(row) == style_filter)
        and (speed_filter == "all" or _speed_band(row, config) == speed_filter)
    ]


def _policy_rate_row(
    policy_id: str,
    primary_rows: list[dict[str, str]],
    all_rows: list[dict[str, str]],
) -> dict[str, Any]:
    primary_outcomes = Counter(row.get("outcome", "") for row in primary_rows)
    all_outcomes = Counter(row.get("outcome", "") for row in all_rows)
    return {
        "policy_id": policy_id,
        "attempts": len(primary_rows),
        "all_attempts": len(all_rows),
        "saves": sum(
            row.get("outcome") in SAVE_OUTCOMES for row in primary_rows
        ),
        "goals": primary_outcomes["Goal"],
        "save_rate": _save_metric(primary_rows),
        "goal_rate": _goal_metric(primary_rows),
        "glove_contact_rate": _glove_metric(primary_rows),
        "glove_save_rate": _glove_save_metric(primary_rows),
        "contact_then_goal_rate": _contact_then_goal_metric(primary_rows),
        "invalid_count": all_outcomes["Invalid"],
        "timeout_count": all_outcomes["Timeout"],
        "inference_error_count": sum(
            _integer(row.get("native_inference_invalid_output_count"))
            for row in all_rows
        ),
    }


def _breakdown_table(
    dimension: str,
    bands: tuple[str, ...],
    classifier: Callable[[dict[str, str]], str],
    rows_by_policy: dict[str, list[dict[str, str]]],
) -> dict[str, Any]:
    rows: list[dict[str, Any]] = []
    for band in bands:
        policy_rows = []
        for policy_id, policy_source in rows_by_policy.items():
            selected = [row for row in policy_source if classifier(row) == band]
            policy_rows.append(
                {
                    "policy_id": policy_id,
                    "attempts": len(selected),
                    "save_rate": _save_metric(selected),
                    "glove_contact_rate": _glove_metric(selected),
                }
            )
        rows.append({"band_id": band, "policies": policy_rows})
    return {"dimension": dimension, "rows": rows}


def build_analysis(
    *,
    config_path: Path,
    source_manifest_path: Path,
    report_path: Path,
    episodes_path: Path,
) -> dict[str, Any]:
    config = load_analysis_config(config_path)
    source_manifest = json.loads(source_manifest_path.read_text(encoding="utf-8"))
    report = json.loads(report_path.read_text(encoding="utf-8"))
    failures: list[str] = []

    if source_manifest.get("status") != "passed":
        failures.append("source manifest did not pass")
    if source_manifest.get("benchmark_id") != config.source_benchmark_id:
        failures.append("source manifest benchmark does not match config")
    if int(source_manifest.get("master_seed", -1)) != config.master_seed:
        failures.append("source manifest master seed does not match config")
    if report.get("benchmark_id") != config.source_benchmark_id:
        failures.append("source report benchmark does not match config")
    if report.get("primary_population") != config.primary_population:
        failures.append("source primary population does not match config")
    expected_hashes = source_manifest.get("source_hashes", {})
    if expected_hashes.get("report_sha256") != _sha256(report_path):
        failures.append("source report hash changed")
    if expected_hashes.get("episodes_sha256") != _sha256(episodes_path):
        failures.append("source episodes hash changed")

    required_policies = (config.final_policy, config.teacher_policy)
    report_policies = {item.get("policy"): item for item in report.get("policies", [])}
    for policy_id in required_policies:
        if policy_id not in report_policies:
            failures.append(f"source report is missing {policy_id}")

    with episodes_path.open(newline="", encoding="utf-8") as handle:
        source_rows = list(csv.DictReader(handle))
    rows_by_policy: dict[str, list[dict[str, str]]] = {
        policy_id: [] for policy_id in required_policies
    }
    for row in source_rows:
        policy_id = row.get("policy", "")
        if policy_id in rows_by_policy:
            rows_by_policy[policy_id].append(row)

    paired: dict[str, dict[tuple[int, int], dict[str, str]]] = {}
    for policy_id, rows in rows_by_policy.items():
        keyed: dict[tuple[int, int], dict[str, str]] = {}
        for row in rows:
            key = _episode_key(row)
            if key in keyed:
                failures.append(f"{policy_id} has duplicate episode key {key}")
            keyed[key] = row
            try:
                if _bool(row.get("expected_on_target")):
                    for basis in config.location_bases:
                        _clamped_location(row, basis, config)
                    _speed_band(row, config)
                    _spin_band(row)
            except ValueError as error:
                failures.append(f"{policy_id} {key}: {error}")
        paired[policy_id] = keyed

    final_keys = set(paired.get(config.final_policy, {}))
    teacher_keys = set(paired.get(config.teacher_policy, {}))
    if final_keys != teacher_keys:
        failures.append("required policies do not have identical episode keys")
    for key in sorted(final_keys & teacher_keys):
        final_row = paired[config.final_policy][key]
        teacher_row = paired[config.teacher_policy][key]
        if any(
            final_row.get(field, "") != teacher_row.get(field, "")
            for field in SHOT_IDENTITY_FIELDS
        ):
            failures.append(f"paired shot identity differs at {key}")
            break

    expected_attempts = int(source_manifest.get("attempts_per_policy", 0))
    for policy_id, rows in rows_by_policy.items():
        if len(rows) != expected_attempts:
            failures.append(
                f"{policy_id} has {len(rows)} rows, expected {expected_attempts}"
            )
    if failures:
        raise RuntimeError("Stage 8 analysis input failed: " + "; ".join(failures[:20]))

    primary_by_policy = {
        policy_id: [row for row in rows if _bool(row.get("expected_on_target"))]
        for policy_id, rows in rows_by_policy.items()
    }
    final_primary = primary_by_policy[config.final_policy]
    teacher_primary = primary_by_policy[config.teacher_policy]

    cell_order = [
        f"{vertical}-{horizontal}"
        for vertical in ("upper", "middle", "low")
        for horizontal in ("left", "centre-left", "centre-right", "right")
    ]
    filter_slices: list[dict[str, Any]] = []
    for basis in config.location_bases:
        for style_filter in config.style_filters:
            for speed_filter in config.speed_filters:
                final_filtered = _filtered(
                    final_primary,
                    style_filter=style_filter,
                    speed_filter=speed_filter,
                    config=config,
                )
                teacher_filtered = _filtered(
                    teacher_primary,
                    style_filter=style_filter,
                    speed_filter=speed_filter,
                    config=config,
                )
                final_cells: dict[str, list[dict[str, str]]] = defaultdict(list)
                teacher_cells: dict[str, list[dict[str, str]]] = defaultdict(list)
                for row in final_filtered:
                    final_cells[heatmap_cell_id(row, basis, config)].append(row)
                for row in teacher_filtered:
                    teacher_cells[heatmap_cell_id(row, basis, config)].append(row)

                cells: list[dict[str, Any]] = []
                for cell_id in cell_order:
                    final_rows = final_cells[cell_id]
                    teacher_rows = teacher_cells[cell_id]
                    final_keys_for_cell = {_episode_key(row) for row in final_rows}
                    teacher_keys_for_cell = {_episode_key(row) for row in teacher_rows}
                    if final_keys_for_cell != teacher_keys_for_cell:
                        raise RuntimeError(
                            f"paired cell population differs for {basis}/"
                            f"{style_filter}/{speed_filter}/{cell_id}"
                        )
                    final_save = _save_metric(final_rows)
                    teacher_save = _save_metric(teacher_rows)
                    cells.append(
                        {
                            "cell_id": cell_id,
                            "sample_count": len(final_rows),
                            "final_save_rate": final_save,
                            "teacher_save_rate": teacher_save,
                            "final_glove_contact_rate": _glove_metric(final_rows),
                            "teacher_gap_points": 100.0
                            * (
                                float(teacher_save["value"])
                                - float(final_save["value"])
                            ),
                        }
                    )
                filter_slices.append(
                    {
                        "location_basis": basis,
                        "style_filter": style_filter,
                        "speed_filter": speed_filter,
                        "sample_count": len(final_filtered),
                        "cells": cells,
                    }
                )

    breakdown_tables = [
        _breakdown_table(
            "height",
            HEIGHT_BANDS,
            _height_band,
            primary_by_policy,
        ),
        _breakdown_table(
            "shot_style",
            STYLE_BANDS,
            _shot_style,
            primary_by_policy,
        ),
        _breakdown_table(
            "speed",
            SPEED_BANDS,
            lambda row: _speed_band(row, config),
            primary_by_policy,
        ),
        _breakdown_table(
            "spin",
            SPIN_BANDS,
            _spin_band,
            primary_by_policy,
        ),
    ]
    left_right_rows: list[dict[str, Any]] = []
    for policy_id, rows in primary_by_policy.items():
        left = [row for row in rows if _horizontal_band(row) == "left"]
        right = [row for row in rows if _horizontal_band(row) == "right"]
        left_rate = _save_metric(left)
        right_rate = _save_metric(right)
        left_right_rows.append(
            {
                "policy_id": policy_id,
                "left": {"attempts": len(left), "save_rate": left_rate},
                "right": {"attempts": len(right), "save_rate": right_rate},
                "left_minus_right_points": 100.0
                * (float(left_rate["value"]) - float(right_rate["value"])),
            }
        )

    safety_totals = []
    for policy_id, rows in rows_by_policy.items():
        counts = {
            field: sum(_integer(row.get(field)) for row in rows)
            for field in SAFETY_COUNT_FIELDS
        }
        counts["invalid_outcomes"] = sum(
            row.get("outcome") == "Invalid" for row in rows
        )
        counts["timeout_outcomes"] = sum(
            row.get("outcome") == "Timeout" for row in rows
        )
        safety_totals.append(
            {
                "policy_id": policy_id,
                "total_failures": sum(counts.values()),
                "counts": [
                    {"metric_id": metric_id, "count": count}
                    for metric_id, count in counts.items()
                ],
            }
        )

    contract_fields = (
        "environment_id",
        "behavior_name",
        "observation_spec_id",
        "reward_spec_id",
        "action_spec_id",
        "scenario_suite_id",
        "motor_profile_id",
        "motor_contract_id",
        "shot_contract_id",
        "shot_physics_id",
        "glove_handling_id",
        "glove_geometry_id",
        "primary_population",
    )
    return {
        "schema_id": SCHEMA_ID,
        "analysis_id": config.analysis_id,
        "source_benchmark_id": config.source_benchmark_id,
        "generated_at": report.get("generated_at", ""),
        "master_seed": source_manifest.get("master_seed"),
        "episode_key_digest": source_manifest.get("episode_key_digest"),
        "source_hashes": [
            {"artifact_id": "report.json", "sha256": _sha256(report_path)},
            {"artifact_id": "episodes.csv", "sha256": _sha256(episodes_path)},
            {
                "artifact_id": "source-manifest.json",
                "sha256": _sha256(source_manifest_path),
            },
        ],
        "contracts": [
            {"contract_id": field, "value": str(report.get(field, ""))}
            for field in contract_fields
        ],
        "policies": [
            {
                "policy_id": config.final_policy,
                "display_name": "Final goalkeeper",
                "role": "final",
            },
            {
                "policy_id": config.teacher_policy,
                "display_name": "Reactive teacher",
                "role": "teacher",
            },
        ],
        "goal_grid": {
            "columns": 4,
            "rows": 3,
            "minimum_x": config.minimum_x,
            "maximum_x": config.maximum_x,
            "minimum_y": config.minimum_y,
            "maximum_y": config.maximum_y,
            "cell_order": cell_order,
        },
        "filter_slices": filter_slices,
        "overall_policy_rows": [
            _policy_rate_row(
                config.final_policy,
                final_primary,
                rows_by_policy[config.final_policy],
            ),
            _policy_rate_row(
                config.teacher_policy,
                teacher_primary,
                rows_by_policy[config.teacher_policy],
            ),
        ],
        "breakdown_tables": breakdown_tables,
        "left_right_rows": left_right_rows,
        "safety_totals": safety_totals,
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--source-manifest", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--episodes", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    artifact = build_analysis(
        config_path=args.config,
        source_manifest_path=args.source_manifest,
        report_path=args.report,
        episodes_path=args.episodes,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(artifact, indent=2, allow_nan=False) + "\n",
        encoding="utf-8",
    )
    print(f"Stage 8 analysis artifact written: {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
