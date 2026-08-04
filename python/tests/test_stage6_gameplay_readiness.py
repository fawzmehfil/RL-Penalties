from pathlib import Path

import pytest

from penalty_shootout.evaluation.stage6_gameplay_readiness import (
    build_report,
    capability_envelope,
    contact_diagnosis,
    episode_key,
    key_digest,
    paired_outcomes,
)


def episode(
    policy: str,
    attempt: int,
    outcome: str,
    *,
    delay: int = 2,
    x: float = 0.0,
    y: float = 1.0,
    flight: float = 0.6,
    contact: bool = False,
    saturation: float = 0.0,
) -> dict[str, str]:
    return {
        "policy": policy,
        "arena_id": "0",
        "attempt_id": str(attempt),
        "seed": str(100 + attempt),
        "outcome": outcome,
        "expected_on_target": "True",
        "glove_contact": str(contact),
        "goalkeeper_contact": str(contact),
        "root_target_saturation_distance": str(saturation),
        "predicted_unopposed_crossing_local_x": str(x),
        "predicted_unopposed_crossing_local_y": str(y),
        "sampled_shot_flight_time": str(flight),
        "observation_delay_ticks": str(delay),
        "first_goalkeeper_contact_point_local_z": "0.6",
        "action_mask_violations": "0",
    }


def test_episode_keys_and_paired_outcomes_are_order_independent() -> None:
    left = [episode("left", 2, "Goal"), episode("left", 1, "Saved")]
    right = [episode("right", 1, "Saved"), episode("right", 2, "Saved")]

    paired = paired_outcomes(left, right)

    assert episode_key(left[0]) == (0, 2, "102")
    assert key_digest(left) == key_digest(right)
    assert paired["both_save"] == 1
    assert paired["right_only_save"] == 1
    assert paired["right_minus_left"] == pytest.approx(0.5)


def test_capability_oracle_separates_timing_and_root_saturation() -> None:
    rows = [
        episode("native", 1, "Saved", x=0.0, flight=0.7),
        episode("native", 2, "Goal", x=3.5, flight=0.7),
        episode("native", 3, "Goal", x=2.5, flight=0.2),
    ]

    result = capability_envelope(rows, delay_seconds=0.04)

    assert result["label"] == "offline_motor_capability_estimate_not_a_policy_score"
    assert result["root_saturated_rate"]["successes"] == 1
    assert result["insufficient_full_reach_time_rate"]["successes"] == 1
    assert result["reachable_rate"]["successes"] == 1


def test_contact_diagnosis_never_promotes_without_runtime_ab() -> None:
    rows = [
        episode("native", 1, "Goal", y=0.4, contact=True),
        episode("native", 2, "Saved", y=0.5, contact=True),
    ]

    result = contact_diagnosis(rows)

    assert result["contact_then_goal_rate"]["value"] == pytest.approx(0.5)
    assert result["candidate_status"] == "not_promoted_requires_controlled_runtime_ab_test"


def test_low_contact_candidate_still_requires_canonical_regression() -> None:
    config = {
        "audit_id": "stage6-gameplay-readiness-v1",
        "attempts_per_arena": 400,
        "master_seed": 20260803,
        "decision_thresholds": {
            "minimum_teacher_gain_for_interception_training": 0.05,
            "minimum_zero_delay_gain_for_timing_training": 0.03,
            "minimum_contact_candidate_low_gain": 0.05,
            "minimum_contact_candidate_overall_gain": 0.02,
        },
    }
    policies = {
        "native": [episode("native_split_v1:seed-001", index, "Goal", y=0.4)
                   for index in range(1, 401)],
        "curve": [episode("reactive_curve_v1", index, "Goal", y=0.4)
                  for index in range(1, 401)],
        "motor": [episode("reactive_motor_v1", index, "Goal", y=0.4)
                  for index in range(1, 401)],
        "zero": [episode("native_split_v1:seed-001", index, "Goal", delay=0, y=0.4)
                 for index in range(1, 401)],
        "stand": [episode("stand_center_v1", index, "Goal", y=0.4)
                  for index in range(1, 401)],
        "random": [episode("random_hybrid_v1", index, "Goal", y=0.4)
                   for index in range(1, 401)],
        "candidate": [episode("native_split_v1:seed-001", index, "Saved", y=0.4)
                      for index in range(1, 401)],
    }

    report = build_report(
        config,
        policies["curve"] + policies["motor"] + policies["native"],
        policies["zero"],
        policies["stand"] + policies["random"],
        policies["candidate"],
    )

    candidate = report["contact_diagnosis"]["runtime_candidate"]
    assert candidate["passes_gameplay_gain_gate"] is True
    assert candidate["promotion_status"] == "not_promoted_regression_or_realism_review_pending"


def test_build_report_rejects_noncanonical_policy_counts(tmp_path: Path) -> None:
    config = {
        "audit_id": "stage6-gameplay-readiness-v1",
        "attempts_per_arena": 25,
        "master_seed": 20260803,
        "decision_thresholds": {
            "minimum_teacher_gain_for_interception_training": 0.05,
            "minimum_zero_delay_gain_for_timing_training": 0.03,
        },
    }
    with pytest.raises(ValueError, match="Expected 400 episodes"):
        build_report(config, [], [], [])
