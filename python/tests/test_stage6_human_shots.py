from pathlib import Path

import numpy as np
import pytest

from penalty_shootout.evaluation.goalkeeper import (
    BenchmarkConfig,
    ReactiveCurveV1Policy,
    ReactiveMotorV1Policy,
    aggregate_policy,
    load_benchmark_config,
    make_policy,
    motor_timing_estimate_v1,
    stage6_curve_magnitude,
    stage6_pretraining_diagnosis,
    stage6_speed_band,
    stage6_spin_band,
    validate_benchmark_config,
    write_summary,
)


ROOT = Path(__file__).resolve().parents[2]
CONFIG = (
    ROOT
    / "configs"
    / "benchmarks"
    / "goalkeeper-control-v2-human-shot-v1-pretraining-2k.json"
)


def test_stage6_benchmark_contract_is_fixed() -> None:
    config = load_benchmark_config(CONFIG)

    assert config.observation_spec_id == "control-state-v2-gameplay-v1"
    assert config.scenario_suite_id == "human-shot-v1"
    assert config.shot_contract_id == "player-shot-v1"
    assert config.shot_physics_id == "football-flight-v1"
    assert config.primary_population == "expected_on_target"
    assert config.total_attempts == 2_000
    assert config.master_seed == 20260803
    assert config.environment_parameters["stage6.observation_delay_ticks"] == 2


def test_stage6_config_rejects_wrong_primary_population() -> None:
    config = load_benchmark_config(CONFIG)
    broken = BenchmarkConfig(
        **{**config.__dict__, "primary_population": "all_attempts"}
    )

    with pytest.raises(ValueError, match="primary_population"):
        validate_benchmark_config(broken)


def test_reactive_curve_uses_visible_prediction_and_mask() -> None:
    config = load_benchmark_config(CONFIG)
    policy = make_policy("reactive_curve_v1", 1, config)
    assert isinstance(policy, ReactiveCurveV1Policy)
    observations = np.zeros((2, 35), dtype=np.float32)
    observations[:, 32] = np.asarray([0.2, 0.8], dtype=np.float32)
    observations[:, 33] = np.asarray([0.3, -0.5], dtype=np.float32)
    observations[:, 34] = np.asarray([0.6, -0.4], dtype=np.float32)
    observations[0, 9] = 0.2 / 3.1
    masks = np.zeros((2, 2), dtype=bool)
    masks[0, 1] = True

    continuous, discrete = policy.hybrid_actions(observations, masks)

    assert continuous.shape == (2, 4)
    assert np.all(np.isfinite(continuous))
    assert np.all(np.abs(continuous) <= 1.0)
    assert discrete.tolist() == [[0], [0]]
    assert continuous[0].tolist() == pytest.approx([0.692, 0.3, 0.6, 1.0])


def test_reactive_motor_matches_frozen_motor_timing_parity_fixture() -> None:
    estimate = motor_timing_estimate_v1(0.3, 0.6, 0.2)
    assert estimate.root_target_x == pytest.approx(0.5006)
    assert estimate.root_target_y == pytest.approx(0.0714)
    assert estimate.dive_duration == pytest.approx(0.5153647)
    assert estimate.full_reach_time == pytest.approx(0.3364532)
    assert estimate.root_target_saturation_m == pytest.approx(0.0)

    config = load_benchmark_config(CONFIG)
    policy = make_policy("reactive_motor_v1", 1, config)
    assert isinstance(policy, ReactiveMotorV1Policy)
    observations = np.zeros((1, 35), dtype=np.float32)
    observations[0, 9] = 0.2 / 3.1
    observations[0, 32] = 0.3 / 1.5
    observations[0, 33] = 0.3
    observations[0, 34] = 0.6

    continuous, discrete = policy.hybrid_actions(
        observations,
        np.zeros((1, 2), dtype=bool),
    )

    assert continuous[0].tolist() == pytest.approx([0.24048, 0.3, 0.6, 1.0])
    assert discrete.tolist() == [[1]]


def test_stage6_bins_are_stable() -> None:
    episode = {
        "launch_speed_mps": 24.0,
        "command_side_spin": -0.7,
        "curve_displacement": {"x": 0.3, "y": 0.4},
    }

    assert stage6_speed_band(episode) == "fast"
    assert stage6_spin_band(episode) == "high"
    assert stage6_curve_magnitude(episode) == pytest.approx(0.5)


def test_stage6_diagnosis_recommends_training_for_native_gap() -> None:
    def policy(name: str, save: float, glove: float) -> dict:
        return {
            "policy": name,
            "save_rate": {"value": save},
            "glove_contact_rate": {"value": glove},
            "invalid_rate": {"successes": 0},
            "timeout_rate": {"successes": 0},
            "by_shot_style": {
                "Placed": {
                    "attempts": 700,
                    "save_rate": {"value": save},
                }
            },
            "by_flight_time_band": {},
            "by_spin_band": {},
            "by_launch_speed_band": {},
            "by_height_band": {},
            "by_horizontal_band": {},
            "action_mask_violations": 0,
            "duplicate_terminal_events": 0,
            "control_command_clamp_count": 0,
            # Anatomical root saturation is reported but is not an invalid
            # command or lifecycle failure.
            "control_target_clamp_count": 3,
            "policy_decision_duplicate_request_count": 0,
            "policy_decision_missing_action_count": 0,
            "policy_decision_request_count": 10,
            "policy_decision_consumed_count": 9,
            "policy_decision_discarded_count": 1,
            "native_inference_invalid_output_count": 0,
            "native_inference_commit_mismatch_count": 0,
            "runtime_crossing_error_m": {
                "count": 100,
                "median": 0.02,
                "p95": 0.04,
                "maximum": 0.06,
            },
        }

    diagnosis = stage6_pretraining_diagnosis(
        [
            policy("stand_center_v1", 0.05, 0.0),
            policy("reactive_curve_v1", 0.60, 0.75),
            policy("native_split_v1:seed-001", 0.40, 0.50),
        ]
    )

    assert diagnosis["training_recommended"] is True
    assert diagnosis["safety_invariants_passed"] is True
    assert diagnosis["overall_save_rate_gap"] == pytest.approx(0.20)


def test_stage6_summary_handles_incomplete_policy_set(tmp_path: Path) -> None:
    report = {
        "benchmark_id": "goalkeeper-control-v2-human-shot-v1-pretraining-2k",
        "run_id": "stage6-summary-test",
        "generated_at": "2026-08-03T00:00:00+00:00",
        "behavior_name": "GoalkeeperControl-v2",
        "policies": [],
        "stage6_pretraining_diagnosis": {
            "training_recommended": True,
            "reasons": ["native_split_v1 and reactive_curve_v1 are both required"],
        },
    }

    output = tmp_path / "summary.md"
    write_summary(output, report)

    summary = output.read_text(encoding="utf-8")
    assert "Training recommended: **true**" in summary
    assert "Native expected-on-target save rate: unavailable" in summary


def test_stage6_uses_expected_on_target_primary_denominator() -> None:
    config = load_benchmark_config(CONFIG)
    policy = make_policy("stand_center_v1", 1, config)
    outcomes = [
        ("Saved", True),
        ("Goal", True),
        ("Saved", False),
        ("PostOrCrossbarOut", False),
        ("MissWide", False),
        ("MissHigh", False),
    ]
    episodes = []
    for attempt_id, (outcome, expected_on_target) in enumerate(outcomes, 1):
        episodes.append(
            {
                "arena_id": 0,
                "attempt_id": attempt_id,
                "seed": str(attempt_id),
                "outcome": outcome,
                "expected_on_target": expected_on_target,
                "goalkeeper_contact": False,
                "goal_frame_contact": False,
                "glove_contact": False,
                "has_centre_plane_intersection": True,
                "target_error": 0.02,
                "action_mask_violations": 0,
                "duplicate_terminal_events": 0,
                "accepted_action_counts": [0] * 9,
                "sampled_shot_flight_time": 0.6,
                "ball_flight_time": 0.6,
                "has_first_dive": False,
                "first_accepted_dive_action": "",
                "has_save_commitment": False,
                "requested_target_local": {"x": 0.0, "y": 1.0, "z": 0.0},
                "glove_handling_enabled": True,
                "glove_handling_id": "keeper-glove-handling-v1",
                "glove_geometry_id": "goalkeeper-palm-compound-v1",
                "glove_handling_outcome": (
                    "Catch" if attempt_id == 1 else "Parry"
                ),
                "glove_outgoing_energy_ratio": 0.4,
            }
        )

    report = aggregate_policy(policy, episodes, config, attempts_per_arena=125)

    assert report["primary_population_attempts"] == 2
    assert report["save_rate"]["value"] == pytest.approx(0.5)
    assert report["all_attempt_save_rate"]["value"] == pytest.approx(2 / 6)
    assert report["frame_rate"]["value"] == pytest.approx(1 / 6)
    assert report["miss_wide_rate"]["value"] == pytest.approx(1 / 6)
    assert report["miss_high_rate"]["value"] == pytest.approx(1 / 6)
    assert report["runtime_crossing_error_m"]["p95"] == pytest.approx(0.02)
    assert report["glove_handling"]["enabled_attempts"] == 2
    assert report["glove_handling"]["catch_rate"]["value"] == pytest.approx(0.5)
    assert report["glove_handling"]["parry_rate"]["value"] == pytest.approx(0.5)
    assert report["glove_handling"]["energy_cap_violations"] == 0
