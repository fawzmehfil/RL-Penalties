import json
from pathlib import Path

import pytest

from penalty_shootout.evaluation.goalkeeper import load_benchmark_config
from penalty_shootout.evaluation.high_shot_regression import build_audit


ROOT = Path(__file__).resolve().parents[2]


def _rate(value: float, total: int = 2000) -> dict:
    return {"successes": round(value * total), "total": total, "value": value}


def _policy(save_rate: float, forward: float = 0.28) -> dict:
    return {
        "policy": "native_split_v1:seed-001",
        "attempts": 2000,
        "complete": True,
        "save_rate": _rate(save_rate),
        "glove_contact_rate": _rate(0.75),
        "glove_save_rate": _rate(0.60),
        "arm_save_rate": _rate(0.05),
        "body_save_rate": _rate(0.03),
        "commit_rate": _rate(1.0),
        "immediate_commit_rate": _rate(0.2),
        "premature_commit_rate": _rate(0.3),
        "timely_commit_rate": _rate(0.7),
        "first_commit_aim_error_m": {"mean": 0.02},
        "goalkeeper_root_distance_m": {"mean": 1.5},
        "minimum_glove_ball_distance_m": {"mean": 0.2},
        "committed_glove_forward_m": {"mean": forward},
        "first_contact_ball_velocity_y_mps": {"mean": -1.0},
        "first_contact_ball_velocity_z_mps": {"mean": -5.0},
        "first_contact_root_velocity_y_mps": {"mean": -0.3},
        "first_contact_impulse_magnitude": {"mean": 2.0},
        "by_height_band": {"high": {"attempts": 2000}},
        "invalid_rate": {"successes": 0},
        "timeout_rate": {"successes": 0},
        "action_mask_violations": 0,
        "control_command_clamp_count": 0,
        "policy_action_override_count": 0,
        "policy_decision_duplicate_request_count": 0,
        "policy_decision_missing_action_count": 0,
        "policy_decision_request_count": 100,
        "policy_decision_consumed_count": 90,
        "policy_decision_discarded_count": 10,
        "accepted_control_decision_count": 90,
    }


def _contract() -> dict:
    path = ROOT / "configs/audits/stage6-high-shot-forward-contact-v1.json"
    return json.loads(path.read_text(encoding="utf-8"))


def _reference() -> dict:
    return {
        "stage5_freeze": {"status": "frozen"},
        "official_benchmark": {
            "by_height_band": {
                "high": {"save_rate": {"value": 0.6882}}
            }
        },
    }


def _evaluation(save_rate: float) -> dict:
    return {
        "benchmark_id": "goalkeeper-control-v2-high-forward-contact-2k",
        "run_id": "high-regression",
        "policies": [_policy(save_rate)],
    }


def test_high_shot_benchmark_is_fixed_upper_third() -> None:
    config = load_benchmark_config(
        ROOT
        / "configs/benchmarks/goalkeeper-control-v2-high-forward-contact-2k.json"
    )

    assert config.total_attempts == 2000
    assert config.warmup_attempts_per_arena == 1
    assert config.motor_contract_id == "keeper-control-forward-v1"
    assert config.environment_parameters["stage5.target_y_min"] == pytest.approx(0.68)
    assert config.environment_parameters["stage5.target_y_max"] == pytest.approx(1.0)
    assert config.environment_parameters[
        "stage6.committed_glove_forward_m"
    ] == pytest.approx(0.28)


def test_stage6_motor_contract_promotes_offset_without_changing_stage5() -> None:
    stage5 = json.loads(
        (ROOT / "configs/environment/goalkeeper-control-v2.json").read_text(
            encoding="utf-8"
        )
    )
    stage6 = json.loads(
        (
            ROOT
            / "configs/environment/goalkeeper-control-v2-stage6.json"
        ).read_text(encoding="utf-8")
    )

    assert stage5["motor_profile_id"] == "keeper-control-v1"
    assert "environment_parameters" not in stage5
    assert stage6["base_motor_profile_id"] == "keeper-control-v1"
    assert stage6["motor_contract_id"] == "keeper-control-forward-v1"
    assert stage6["environment_parameters"][
        "stage6.committed_glove_forward_m"
    ] == pytest.approx(0.28)
    assert stage6["compatibility"][
        "stage5_default_committed_glove_forward_m"
    ] == pytest.approx(0.0)


def test_high_shot_audit_passes_within_non_regression_margin() -> None:
    audit = build_audit(_evaluation(0.67), _contract(), _reference())

    assert audit["promotion_ready"]
    assert audit["save_rate_regression"] == pytest.approx(0.0182)
    assert all(audit["validity_checks"].values())


def test_high_shot_audit_fails_outside_non_regression_margin() -> None:
    audit = build_audit(_evaluation(0.64), _contract(), _reference())

    assert not audit["promotion_ready"]
    assert audit["status"] == "fail"
