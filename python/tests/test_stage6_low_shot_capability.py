import json
from pathlib import Path

import pytest

from penalty_shootout.evaluation.goalkeeper import (
    ReactiveReachV1Policy,
    discard_warmup_attempts,
    load_benchmark_config,
    make_policy,
)
from penalty_shootout.evaluation.low_shot_capability import build_audit


ROOT = Path(__file__).resolve().parents[2]


def _rate(value: float, total: int = 2000) -> dict:
    return {"successes": round(value * total), "total": total, "value": value}


def _policy(name: str, save_rate: float, forward: float = 0.0) -> dict:
    return {
        "policy": name,
        "attempts": 2000,
        "complete": True,
        "episode_key_digest": "fixed-low-shots",
        "save_rate": _rate(save_rate),
        "glove_contact_rate": _rate(0.72),
        "glove_save_rate": _rate(0.50),
        "arm_save_rate": _rate(0.25),
        "body_save_rate": _rate(0.10),
        "commit_rate": _rate(1.0),
        "immediate_commit_rate": _rate(0.2),
        "premature_commit_rate": _rate(0.3),
        "timely_commit_rate": _rate(0.7),
        "first_commit_aim_error_m": {"mean": 0.02},
        "goalkeeper_root_distance_m": {"mean": 1.5},
        "minimum_glove_ball_distance_m": {"mean": 0.3},
        "committed_glove_forward_m": {"mean": forward},
        "first_contact_ball_velocity_y_mps": {"mean": -1.0},
        "first_contact_ball_velocity_z_mps": {"mean": -5.0},
        "first_contact_root_velocity_y_mps": {"mean": -0.3},
        "first_contact_impulse_magnitude": {"mean": 2.0},
        "by_height_band": {"low": {"attempts": 2000}},
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
    return json.loads(
        (
            ROOT
            / "configs"
            / "audits"
            / "stage6-low-shot-capability-v1.json"
        ).read_text(encoding="utf-8")
    )


def _reference() -> dict:
    return {
        "stage5_freeze": {"status": "frozen"},
        "official_benchmark": {
            "by_height_band": {
                "high": {"save_rate": {"value": 0.6882}}
            }
        },
    }


def _evaluation(teacher_rates: list[float], native_rate: float) -> dict:
    horizons = _contract()["teacher_commit_horizons_s"]
    policies = [
        _policy(f"reactive_reach_v1_h{horizon:.2f}", value)
        for horizon, value in zip(horizons, teacher_rates, strict=True)
    ]
    policies.extend(
        [
            _policy("stand_center_v1", 0.05),
            _policy("native_split_v1:seed-001", native_rate),
        ]
    )
    return {
        "benchmark_id": "goalkeeper-control-v2-low-capability-2k",
        "run_id": "low-audit",
        "policies": policies,
    }


def test_low_shot_benchmark_is_fixed_lower_third() -> None:
    config = load_benchmark_config(
        ROOT
        / "configs"
        / "benchmarks"
        / "goalkeeper-control-v2-low-capability-2k.json"
    )

    assert config.total_attempts == 2000
    assert config.warmup_attempts_per_arena == 1
    assert config.observation_shapes == ((35,),)
    assert config.environment_parameters["stage5.target_y_min"] == 0.0
    assert config.environment_parameters["stage5.target_y_max"] == 0.32
    assert config.environment_parameters["stage5.reach_training_enabled"] == 0.0


def test_forward_contact_benchmark_changes_only_committed_glove_depth() -> None:
    baseline = load_benchmark_config(
        ROOT
        / "configs"
        / "benchmarks"
        / "goalkeeper-control-v2-low-capability-2k.json"
    )
    corrected = load_benchmark_config(
        ROOT
        / "configs"
        / "benchmarks"
        / "goalkeeper-control-v2-low-forward-contact-2k.json"
    )

    changed = {
        key
        for key in set(baseline.environment_parameters)
        | set(corrected.environment_parameters)
        if baseline.environment_parameters.get(key)
        != corrected.environment_parameters.get(key)
    }
    assert changed == {"stage6.committed_glove_forward_m"}
    assert corrected.motor_contract_id == "keeper-control-forward-v1"
    assert corrected.environment_parameters[
        "stage6.committed_glove_forward_m"
    ] == pytest.approx(0.28)


def test_low_shot_benchmark_discards_one_warmup_per_arena() -> None:
    episodes = [
        {"arena_id": arena, "attempt_id": attempt}
        for arena in range(2)
        for attempt in range(1, 4)
    ]

    retained = discard_warmup_attempts(episodes, 1)

    assert [(item["arena_id"], item["attempt_id"]) for item in retained] == [
        (0, 2),
        (0, 3),
        (1, 2),
        (1, 3),
    ]


def test_parameterized_reactive_policy_parses_horizon() -> None:
    config = load_benchmark_config(
        ROOT
        / "configs"
        / "benchmarks"
        / "goalkeeper-control-v2-low-capability-2k.json"
    )

    policy = make_policy("reactive_reach_v1:0.35", 1, config)

    assert isinstance(policy, ReactiveReachV1Policy)
    assert policy.commit_horizon == pytest.approx(0.35)
    assert policy.name == "reactive_reach_v1_h0.35"
    with pytest.raises(ValueError, match="between 0.1 and 1.5"):
        make_policy("reactive_reach_v1:2.0", 1, config)


def test_audit_identifies_shared_geometry_gap() -> None:
    audit = build_audit(
        _evaluation([0.48, 0.50, 0.51, 0.50, 0.49], 0.49),
        _contract(),
        _reference(),
    )

    assert audit["status"] == "audit-complete"
    assert audit["diagnosis"] == "shared-motor-or-interception-geometry-gap"
    assert all(audit["validity_checks"].values())


def test_audit_identifies_timing_gap() -> None:
    audit = build_audit(
        _evaluation([0.42, 0.49, 0.58, 0.51, 0.44], 0.48),
        _contract(),
        _reference(),
    )

    assert audit["diagnosis"] == "learned-timing-gap"
    assert audit["teacher_timing_save_rate_spread"] == pytest.approx(0.16)


def test_forward_contact_gate_compares_against_frozen_low_baseline() -> None:
    contract = json.loads(
        (
            ROOT
            / "configs"
            / "audits"
            / "stage6-low-shot-forward-contact-v1.json"
        ).read_text(encoding="utf-8")
    )
    horizons = contract["teacher_commit_horizons_s"]
    policies = [
        _policy(f"reactive_reach_v1_h{horizon:.2f}", 0.52, 0.28)
        for horizon in horizons
    ]
    policies.extend(
        [
            _policy("stand_center_v1", 0.05, 0.28),
            _policy("native_split_v1:seed-001", 0.54, 0.28),
        ]
    )
    baseline = {
        "native_low_shot": {
            "save_rate": 0.4715,
            "glove_contact_rate": 0.7035,
        }
    }
    audit = build_audit(
        {
            "benchmark_id": contract["benchmark_id"],
            "run_id": "forward-contact",
            "policies": policies,
        },
        contract,
        _reference(),
        baseline,
    )

    assert audit["validity_checks"]["committed_glove_forward_applied"]
    assert audit["forward_contact_correction_gate"]["passed"]
    assert audit["forward_contact_correction_gate"][
        "native_save_rate_improvement"
    ] == pytest.approx(0.0685)
