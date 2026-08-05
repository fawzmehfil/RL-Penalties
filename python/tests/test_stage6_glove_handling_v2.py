import json
from pathlib import Path

import pytest

from penalty_shootout.evaluation.goalkeeper import load_benchmark_config
from penalty_shootout.evaluation.stage6_glove_handling_v2 import (
    _validate_catalog_source,
    build_review_catalog,
    key_digest,
    promotion_gate,
    select_profile,
    summarize,
)


ROOT = Path(__file__).resolve().parents[2]
BASE = (
    ROOT
    / "configs"
    / "benchmarks"
    / "goalkeeper-control-v2-human-shot-v1-glove-handling-2k.json"
)


def episode(attempt: int, outcome: str = "Parry") -> dict[str, str]:
    region = "Palm"
    if outcome == "Uncontrolled":
        region = "Back" if attempt % 2 else "Edge"
    return {
        "arena_id": str((attempt - 1) // 25),
        "attempt_id": str((attempt - 1) % 25 + 1),
        "seed": str(1000 + attempt),
        "outcome": "Saved" if outcome in {"Catch", "Punch"} else "Goal",
        "expected_on_target": "True",
        "goalkeeper_contact": "True",
        "glove_contact": "True",
        "glove_handling_enabled": "True",
        "glove_handling_version": "2",
        "glove_handling_profile_id": "balanced",
        "glove_handling_outcome": outcome,
        "glove_contact_region": region,
        "glove_handling_rejection_reason": (
            "BackFace" if region == "Back" else "EdgeContact" if region == "Edge" else "None"
        ),
        "glove_candidate_contact_count": "2",
        "glove_forward_speed_mps": "1.1",
        "glove_capture_distance_m": "0.12",
        "glove_palm_alignment": "0.8",
        "glove_normalized_contact_extent": "0.9" if region == "Edge" else "0.2",
        "glove_incoming_speed_mps": "14",
        "glove_outgoing_energy_ratio": "0.5",
        "glove_controlled_response_count": "1" if outcome != "Uncontrolled" else "0",
        "shot_style": ("Placed", "Power", "Curled")[attempt % 3],
        "launch_speed_mps": "20",
        "requested_target_local_x": "0.5",
        "requested_target_local_y": "1.0",
        "action_mask_violations": "0",
        "duplicate_terminal_events": "0",
        "control_command_clamp_count": "0",
        "policy_decision_duplicate_request_count": "0",
        "policy_decision_missing_action_count": "0",
        "native_inference_invalid_output_count": "0",
        "native_inference_commit_mismatch_count": "0",
    }


def candidate_rows() -> list[dict[str, str]]:
    rows = []
    for attempt in range(1, 101):
        outcome = (
            "Catch"
            if attempt <= 6
            else "Punch"
            if attempt <= 12
            else "Uncontrolled"
            if attempt <= 32
            else "Parry"
        )
        rows.append(episode(attempt, outcome))
    return rows


def baseline_rows() -> list[dict[str, str]]:
    rows = candidate_rows()
    for row in rows:
        row["glove_handling_version"] = "1"
        row["glove_handling_profile_id"] = ""
        row["glove_handling_outcome"] = "Parry"
        row["outcome"] = "Goal"
    return rows


def test_existing_v1_config_is_explicit_and_valid() -> None:
    config = load_benchmark_config(BASE)
    assert config.glove_handling_id == "keeper-glove-handling-v1"
    assert config.glove_geometry_id == "goalkeeper-palm-compound-v1"
    assert config.environment_parameters["stage6.glove_handling_version"] == 1


def test_v2_summary_uses_glove_contact_denominator() -> None:
    summary = summarize(candidate_rows())
    assert summary["catch_share"]["value"] == pytest.approx(0.06)
    assert summary["punch_share"]["value"] == pytest.approx(0.06)
    assert summary["uncontrolled_share"]["value"] == pytest.approx(0.20)
    assert summary["maximum_controlled_response_count"] == 1
    assert summary["safety_passed"] is True


def test_profile_selection_is_deterministic_and_conservative_on_tie() -> None:
    rows = candidate_rows()
    selection = select_profile(
        baseline_rows(),
        {
            "permissive": rows,
            "balanced": rows,
            "conservative": rows,
        },
    )
    assert selection["passed"] is True
    assert selection["selected_profile"] == "conservative"


def test_profile_selection_rejects_missing_punches() -> None:
    rows = candidate_rows()
    for row in rows:
        if row["glove_handling_outcome"] == "Punch":
            row["glove_handling_outcome"] = "Parry"
    selection = select_profile(
        baseline_rows(),
        {name: rows for name in ("conservative", "balanced", "permissive")},
    )
    assert selection["passed"] is False


def test_review_catalog_contains_fixed_4422_categories() -> None:
    catalog = build_review_catalog(candidate_rows(), 20260821)
    assert catalog["categories"] == {
        "catch": 4,
        "punch": 4,
        "edge": 2,
        "back": 2,
    }
    assert len(catalog["entries"]) == 12


def test_review_catalog_rejects_an_incomplete_development_sample() -> None:
    rows = candidate_rows()
    catches = [row for row in rows if row["glove_handling_outcome"] == "Catch"]
    for row in catches[3:]:
        row["glove_handling_outcome"] = "Parry"
    with pytest.raises(ValueError, match="required 4/4/2/2"):
        build_review_catalog(rows, 20260821)


def test_review_catalog_source_is_fixed_profile_v2_and_safe() -> None:
    rows = candidate_rows()
    for row in rows:
        row["benchmark_id"] = "stage6-glove-v2-review-catalog-100"
    source = _validate_catalog_source(rows, "balanced", 100)
    assert source["attempts"] == 100
    assert source["profile_id"] == "balanced"


def test_review_catalog_source_cannot_change_selected_profile() -> None:
    rows = candidate_rows()
    for row in rows:
        row["benchmark_id"] = "stage6-glove-v2-review-catalog-100"
        row["glove_handling_profile_id"] = "permissive"
    with pytest.raises(ValueError, match="selected fixed profile"):
        _validate_catalog_source(rows, "balanced", 100)


def test_review_catalog_source_requires_complete_fixed_quota() -> None:
    rows = candidate_rows()
    for row in rows:
        row["benchmark_id"] = "stage6-glove-v2-review-catalog-100"
    source = _validate_catalog_source(
        rows,
        "balanced",
        100,
        "stage6-glove-v2-review-catalog-100",
        4,
        25,
    )
    assert source["attempts"] == 100
    rows[-1]["attempt_id"] = rows[-2]["attempt_id"]
    with pytest.raises(ValueError, match="duplicate episode keys"):
        _validate_catalog_source(
            rows,
            "balanced",
            100,
            "stage6-glove-v2-review-catalog-100",
            4,
            25,
        )


def test_holdout_gate_requires_identical_episode_digest() -> None:
    baseline = baseline_rows()
    candidate = candidate_rows()
    assert key_digest(baseline) == key_digest(candidate)
    candidate[0]["seed"] = "different"
    report = promotion_gate(baseline, candidate, "holdout")
    assert report["passed"] is False
    assert "episode_keys" in report["failed_checks"]


def test_v2_benchmark_config_requires_fixed_profile(tmp_path: Path) -> None:
    raw = json.loads(BASE.read_text(encoding="utf-8"))
    raw["glove_handling_id"] = "keeper-glove-handling-v2"
    raw["environment_parameters"]["stage6.glove_handling_version"] = 2
    raw["environment_parameters"].pop("stage6.glove_handling_profile", None)
    path = tmp_path / "invalid.json"
    path.write_text(json.dumps(raw), encoding="utf-8")
    with pytest.raises(ValueError, match="fixed.*profile"):
        load_benchmark_config(path)
