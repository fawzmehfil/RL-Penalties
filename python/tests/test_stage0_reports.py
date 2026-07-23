import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def load_report(name: str) -> dict:
    return json.loads((ROOT / "docs" / name).read_text(encoding="utf-8"))


def test_acceptance_report_passed() -> None:
    report = load_report("stage0-acceptance.json")
    assert report["passed"] is True
    assert report["terminal_attempts"] == 1000
    assert report["invalid_outcomes"] == 0


def test_unity_test_summary_passed() -> None:
    report = load_report("stage0-test-summary.json")
    assert report["edit_mode"] == {"passed": 9, "failed": 0}
    assert report["play_mode"] == {"passed": 1, "failed": 0}


def test_macos_connection_repeated_cleanly() -> None:
    report = load_report("stage0-macos-connection-report.json")
    assert report["all_passed"] is True
    assert report["repeats"] >= 3
    assert all(run["terminal_steps_seen"] == 1 for run in report["runs"])
