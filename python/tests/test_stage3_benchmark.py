import json
from pathlib import Path

import numpy as np
import pytest

from penalty_shootout.evaluation.goalkeeper import (
    ACTION_NAMES,
    BenchmarkConfig,
    GoalkeeperPolicy,
    StandCenterPolicy,
    aggregate_policy,
    compact_report,
    height_band,
    horizontal_band,
    load_benchmark_config,
    quadrant,
    rate,
    validate_benchmark_config,
    wilson_interval,
)


ROOT = Path(__file__).resolve().parents[2]


def episode(
    *,
    outcome: str,
    x: float,
    y: float,
    flight_time: float = 0.64,
    first_dive: str = "",
    counts: list[int] | None = None,
    goalkeeper_contact: bool = False,
    glove_contact: bool = False,
) -> dict:
    action_id = ACTION_NAMES.index(first_dive) if first_dive else -1
    return {
        "outcome": outcome,
        "requested_target_local": {"x": x, "y": y, "z": 0.0},
        "ball_flight_time": flight_time,
        "has_first_dive": bool(first_dive),
        "first_accepted_dive_action": first_dive,
        "first_accepted_dive_action_id": action_id,
        "accepted_action_counts": counts or [1, 0, 0, 0, 0, 0, 0, 0, 0],
        "goalkeeper_contact": goalkeeper_contact,
        "glove_contact": glove_contact,
        "action_mask_violations": 0,
        "duplicate_terminal_events": 0,
    }


def test_stage3_benchmark_config_is_canonical() -> None:
    config = load_benchmark_config(
        ROOT / "configs" / "benchmarks" / "goalkeeper-state-v0-id-20k.json"
    )

    assert config.benchmark_id == "goalkeeper-state-v0-id-20k"
    assert config.behavior_name == "GoalkeeperState-v0"
    assert config.arena_count == 16
    assert config.attempts_per_arena == 1250
    assert config.total_attempts == 20_000
    assert config.stage2_lesson == 3
    assert config.master_seed == 20260723


def test_stage3_benchmark_config_rejects_mismatched_total() -> None:
    config = load_benchmark_config(
        ROOT / "configs" / "benchmarks" / "goalkeeper-state-v0-id-20k.json"
    )
    broken = BenchmarkConfig(
        **{**config.__dict__, "total_attempts": config.total_attempts + 1}
    )

    with pytest.raises(ValueError, match="total_attempts"):
        validate_benchmark_config(broken)


def test_wilson_interval_and_rate_are_bounded() -> None:
    low, high = wilson_interval(50, 100)

    assert 0.40 < low < 0.50
    assert 0.50 < high < 0.60
    assert rate(0, 0)["value"] == 0.0


def test_stage3_binning_uses_terminal_target_location() -> None:
    low_left = episode(outcome="Saved", x=-2.5, y=0.35)
    high_right = episode(outcome="Goal", x=2.5, y=2.1)

    assert quadrant(low_left) == "low-left"
    assert horizontal_band(low_left) == "left"
    assert height_band(low_left) == "low"
    assert quadrant(high_right) == "high-right"
    assert horizontal_band(high_right) == "right"
    assert height_band(high_right) == "high"


def test_aggregate_policy_reports_required_stage3_metrics() -> None:
    config = load_benchmark_config(
        ROOT / "configs" / "benchmarks" / "goalkeeper-state-v0-id-20k.json"
    )
    episodes = [
        episode(
            outcome="Saved",
            x=-2.0,
            y=0.4,
            first_dive="DiveLeftLow",
            counts=[2, 0, 0, 1, 0, 0, 0, 0, 0],
            goalkeeper_contact=True,
            glove_contact=True,
        ),
        episode(outcome="BlockedThenOut", x=2.0, y=1.2, first_dive="DiveRightMiddle"),
        episode(outcome="Goal", x=2.2, y=2.0, first_dive="DiveLeftHigh", goalkeeper_contact=True),
        episode(outcome="Invalid", x=-0.2, y=1.4),
    ]

    report = aggregate_policy(StandCenterPolicy(), episodes, config, attempts_per_arena=1)

    assert report["save_rate"]["successes"] == 2
    assert report["goal_rate"]["successes"] == 1
    assert report["invalid_rate"]["successes"] == 1
    assert report["glove_contact_rate"]["successes"] == 1
    assert report["contact_then_goal_rate"]["successes"] == 1
    assert report["wrong_side_rate"]["successes"] == 1
    assert "low-left" in report["by_quadrant"]
    assert "DiveLeftLow" in report["by_first_dive_action"]
    assert report["action_usage"]["Hold"]["count"] >= 2


def test_policy_sanitize_enforces_onnx_style_action_masks() -> None:
    mask = np.zeros((2, len(ACTION_NAMES)), dtype=bool)
    mask[0, 8] = True
    mask[1, 0] = True

    actions = GoalkeeperPolicy._sanitize([8, 1], mask)

    assert actions.tolist() == [[0], [1]]


def test_compact_report_keeps_benchmark_evidence_small() -> None:
    report = {
        "schema_version": 1,
        "benchmark_id": "goalkeeper-state-v0-id-20k",
        "environment_id": "penalty-shootout-kernel-v1",
        "behavior_name": "GoalkeeperState-v0",
        "observation_spec_id": "state-v0",
        "reward_spec_id": "goalkeeper-sparse-v0",
        "run_id": "smoke",
        "generated_at": "2026-07-25T00:00:00+00:00",
        "full_benchmark": False,
        "arena_count": 16,
        "attempts_per_arena": 4,
        "total_attempts": 64,
        "primary_metric": "save_rate",
        "comparisons": [],
        "passed": False,
        "status": "smoke run",
        "policies": [
            {
                "policy": "stand_center",
                "policy_type": "scripted",
                "attempts": 64,
                "expected_attempts": 64,
                "complete": True,
                "outcomes": {"Goal": 64},
                "save_rate": {"value": 0.0},
                "goal_rate": {"value": 1.0},
                "invalid_rate": {"value": 0.0},
                "timeout_rate": {"value": 0.0},
                "glove_contact_rate": {"value": 0.0},
                "goalkeeper_contact_rate": {"value": 0.0},
                "contact_then_goal_rate": {"value": 0.0},
                "wrong_side_rate": {"value": 0.0},
                "wrong_height_rate": {"value": 0.0},
                "action_mask_violations": 0,
                "duplicate_terminal_events": 0,
                "action_usage": {},
                "by_quadrant": {},
                "by_height_band": {},
                "by_horizontal_band": {},
                "by_flight_time_band": {},
                "by_first_dive_action": {},
                "raw": json.dumps({"not": "kept"}),
            }
        ],
    }

    compact = compact_report(report)

    assert compact["policies"][0]["policy"] == "stand_center"
    assert "raw" not in compact["policies"][0]
