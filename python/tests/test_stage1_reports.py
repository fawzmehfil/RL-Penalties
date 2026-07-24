import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def test_stage1_manifest_matches_acceptance_evidence() -> None:
    manifest_path = ROOT / "configs" / "environment" / "kernel-v1.json"
    report = load_json(ROOT / "docs" / "stage1-acceptance.json")
    digest = hashlib.sha256(manifest_path.read_bytes()).hexdigest()

    assert report["passed"] is True
    assert report["environment_id"] == "penalty-shootout-kernel-v1"
    assert report["scenario_suite_id"] == "on-target-v0"
    assert report["manifest_sha256"] == digest


def test_stage1_acceptance_gate_is_complete() -> None:
    report = load_json(ROOT / "docs" / "stage1-acceptance.json")

    assert report["requested_attempts"] == 10_000
    assert report["terminal_attempts"] == 10_000
    assert report["invalid_outcomes"] == 0
    assert report["timeout_outcomes"] == 0
    assert report["duplicate_terminal_events"] == 0
    assert report["action_mask_violations"] == 0
    assert report["non_finite_states"] == 0
    assert report["maximum_unobstructed_target_error_m"] <= report["tolerance_m"]
    assert len(report["action_attempts"]) == 9
    assert all(count > 0 for count in report["action_attempts"])
    assert len(report["action_contacts"]) == 9
    assert all(count > 0 for count in report["action_contacts"][3:])
    assert report["contact_then_goal"] > 0
    assert report["glove_contacts"] > 0
    assert report["left_glove_contacts"] > 0
    assert report["right_glove_contacts"] > 0
    assert len(report["glove_contacts_by_action"]) == 9
    assert all(count > 0 for count in report["glove_contacts_by_action"][3:])
    assert report["glove_touch_then_goal"] > 0
    assert report["goals_by_action"][0] > 0
    assert report["attempts_per_second"] > 0


def test_stage1_unity_test_summary_passed() -> None:
    report = load_json(ROOT / "docs" / "stage1-test-summary.json")
    assert report["edit_mode"]["failed"] == 0
    assert report["play_mode"]["failed"] == 0
    assert report["edit_mode"]["passed"] >= 28
    assert report["play_mode"]["passed"] >= 2


def test_stage1_macos_connection_repeated_cleanly() -> None:
    report = load_json(ROOT / "docs" / "stage1-macos-connection-report.json")
    assert report["environment_id"] == "penalty-shootout-kernel-v1"
    assert report["all_passed"] is True
    assert report["repeats"] >= 3
    assert all(run["discrete_branches"] == [9] for run in report["runs"])
    assert all(run["observation_shapes"] == [[1]] for run in report["runs"])
    assert all(run["terminal_steps_seen"] == 1 for run in report["runs"])
