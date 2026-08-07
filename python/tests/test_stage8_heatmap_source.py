import csv
import json
from pathlib import Path

import pytest

from penalty_shootout.evaluation.goalkeeper import load_benchmark_config
from penalty_shootout.evaluation.stage8_heatmap_source import (
    REQUIRED_POLICIES,
    SAFETY_COUNT_FIELDS,
    SHOT_IDENTITY_FIELDS,
    validate_source,
)


ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / "configs/benchmarks/goalkeeper-control-v2-stage8-heatmap-source-20k.json"


def test_stage8_source_config_is_fixed() -> None:
    config = load_benchmark_config(CONFIG)
    assert config.arena_count == 16
    assert config.attempts_per_arena == 1250
    assert config.total_attempts == 20_000
    assert config.master_seed == 20260803
    assert config.primary_population == "expected_on_target"
    assert config.glove_handling_id == "keeper-glove-handling-v1"
    assert config.environment_parameters["stage6.glove_handling_version"] == 1


def _write_fixture(tmp_path: Path, *, mismatch: bool = False) -> tuple[Path, Path]:
    report_path = tmp_path / "report.json"
    episodes_path = tmp_path / "episodes.csv"
    policy_reports = []
    rows = []
    for policy_index, policy in enumerate(REQUIRED_POLICIES):
        policy_reports.append(
            {
                "policy": policy,
                "complete": True,
                "attempts": 1,
                "primary_population_attempts": 1,
                "episode_key_digest": "same-digest",
                "save_rate": {"value": 0.5},
                "glove_contact_rate": {"value": 0.6},
                "policy_decision_request_count": 2,
                "policy_decision_consumed_count": 1,
                "policy_decision_discarded_count": 1,
                "outcomes": {"Saved": 1},
            }
        )
        row = {field: "0" for field in SHOT_IDENTITY_FIELDS}
        row.update(
            {
                "arena_id": "0",
                "attempt_id": "1",
                "policy": policy,
                "seed": "7",
                "shot_style": "Placed",
                "expected_on_target": "True",
                "expected_target_class": "OnTarget",
                "glove_handling_enabled": "True",
                "glove_handling_id": "keeper-glove-handling-v1",
            }
        )
        row.update({field: "0" for field in SAFETY_COUNT_FIELDS})
        if mismatch and policy_index == 1:
            row["intended_target_local_x"] = "1"
        rows.append(row)

    report_path.write_text(
        json.dumps(
            {
                "benchmark_id": "goalkeeper-control-v2-stage8-heatmap-source-20k",
                "environment_id": "penalty-shootout-kernel-v1",
                "behavior_name": "GoalkeeperControl-v2",
                "observation_spec_id": "control-state-v2-gameplay-v1",
                "reward_spec_id": "goalkeeper-sparse-v0",
                "action_spec_id": "goalkeeper-hybrid-v1",
                "motor_profile_id": "keeper-control-v1",
                "motor_contract_id": "keeper-control-forward-v1",
                "scenario_suite_id": "human-shot-v1",
                "shot_contract_id": "player-shot-v1",
                "shot_physics_id": "football-flight-v1",
                "primary_population": "expected_on_target",
                "glove_handling_id": "keeper-glove-handling-v1",
                "glove_geometry_id": "goalkeeper-palm-compound-v1",
                "arena_count": 1,
                "attempts_per_arena": 1,
                "total_attempts": 1,
                "environment_parameters": {"stage6.glove_handling_version": 1},
                "policies": policy_reports,
            }
        ),
        encoding="utf-8",
    )
    with episodes_path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    return report_path, episodes_path


def test_stage8_source_validator_accepts_paired_rows(tmp_path: Path) -> None:
    report, episodes = _write_fixture(tmp_path)
    manifest = validate_source(
        report_path=report,
        episodes_path=episodes,
        expected_attempts_per_policy=1,
    )
    assert manifest["status"] == "passed"
    assert manifest["selected_rows"] == 2
    assert manifest["source_rows"] == 2


def test_stage8_source_validator_rejects_shot_mismatch(tmp_path: Path) -> None:
    report, episodes = _write_fixture(tmp_path, mismatch=True)
    with pytest.raises(RuntimeError, match="identical fixed shots"):
        validate_source(
            report_path=report,
            episodes_path=episodes,
            expected_attempts_per_policy=1,
        )


def test_stage8_source_validator_checks_benchmark_contract(tmp_path: Path) -> None:
    report, episodes = _write_fixture(tmp_path)
    benchmark = tmp_path / "benchmark.json"
    payload = json.loads(report.read_text(encoding="utf-8"))
    payload.pop("policies")
    payload["master_seed"] = 20260803
    benchmark.write_text(json.dumps(payload), encoding="utf-8")
    manifest = validate_source(
        report_path=report,
        episodes_path=episodes,
        benchmark_path=benchmark,
        expected_attempts_per_policy=1,
    )
    assert manifest["master_seed"] == 20260803
    assert manifest["source_hashes"]["benchmark_config_sha256"]

    payload["observation_spec_id"] = "wrong-observation"
    benchmark.write_text(json.dumps(payload), encoding="utf-8")
    with pytest.raises(RuntimeError, match="observation_spec_id"):
        validate_source(
            report_path=report,
            episodes_path=episodes,
            benchmark_path=benchmark,
            expected_attempts_per_policy=1,
        )
