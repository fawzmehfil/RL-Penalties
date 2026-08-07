import csv
import hashlib
import json
from pathlib import Path

import pytest

from penalty_shootout.evaluation.stage8_analysis import (
    build_analysis,
    heatmap_cell_id,
    load_analysis_config,
)


ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / "configs/analysis/stage8-goalkeeper-analysis-v1.json"
FINAL = "native_split_v1:seed-001"
TEACHER = "reactive_curve_v1"


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _fixture(tmp_path: Path, *, alter_hash: bool = False) -> tuple[Path, Path, Path]:
    report = tmp_path / "report.json"
    episodes = tmp_path / "episodes.csv"
    manifest = tmp_path / "source-manifest.json"
    contract = {
        "benchmark_id": "goalkeeper-control-v2-stage8-heatmap-source-20k",
        "primary_population": "expected_on_target",
        "environment_id": "penalty-shootout-kernel-v1",
        "behavior_name": "GoalkeeperControl-v2",
        "observation_spec_id": "control-state-v2-gameplay-v1",
        "reward_spec_id": "goalkeeper-sparse-v0",
        "action_spec_id": "goalkeeper-hybrid-v1",
        "scenario_suite_id": "human-shot-v1",
        "motor_profile_id": "keeper-control-v1",
        "motor_contract_id": "keeper-control-forward-v1",
        "shot_contract_id": "player-shot-v1",
        "shot_physics_id": "football-flight-v1",
        "glove_handling_id": "keeper-glove-handling-v1",
        "glove_geometry_id": "goalkeeper-palm-compound-v1",
        "generated_at": "2026-08-07T00:00:00Z",
        "policies": [{"policy": FINAL}, {"policy": TEACHER}],
    }
    report.write_text(json.dumps(contract), encoding="utf-8")

    rows = []
    shots = [
        (1, -3.0, 2.0, "Placed", 17.0, 0.1),
        (2, -1.0, 1.2, "Power", 21.0, 0.3),
        (3, 1.0, 0.4, "Curled", 25.0, 0.8),
        (4, 3.0, 2.0, "Placed", 17.0, 0.1),
    ]
    for policy in (FINAL, TEACHER):
        for attempt, x, y, style, speed, spin in shots:
            saved = policy == TEACHER or attempt in {1, 3}
            row = {
                "policy": policy,
                "arena_id": "0",
                "attempt_id": str(attempt),
                "seed": str(100 + attempt),
                "outcome": "Saved" if saved else "Goal",
                "expected_on_target": "True",
                "expected_target_class": "OnTarget",
                "shot_style": style,
                "mixture_component_id": style,
                "intended_target_local_x": str(x),
                "intended_target_local_y": str(y),
                "intended_target_local_z": "0",
                "predicted_unopposed_crossing_local_x": str(x),
                "predicted_unopposed_crossing_local_y": str(y),
                "predicted_unopposed_crossing_local_z": "0",
                "launch_speed_mps": str(speed),
                "launch_angular_velocity_local_x": "0",
                "launch_angular_velocity_local_y": "0",
                "launch_angular_velocity_local_z": "0",
                "curve_displacement": "0",
                "command_side_spin": str(spin),
                "glove_contact": "True" if saved else "False",
                "goalkeeper_contact": "True" if saved else "False",
                "native_inference_invalid_output_count": "0",
            }
            row.update(
                {
                    field: "0"
                    for field in (
                        "action_mask_violations",
                        "duplicate_terminal_events",
                        "control_command_clamp_count",
                        "policy_decision_duplicate_request_count",
                        "policy_decision_missing_action_count",
                    )
                }
            )
            rows.append(row)
    with episodes.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

    manifest.write_text(
        json.dumps(
            {
                "status": "passed",
                "benchmark_id": contract["benchmark_id"],
                "master_seed": 20260803,
                "attempts_per_policy": 4,
                "episode_key_digest": "fixture-digest",
                "source_hashes": {
                    "report_sha256": _sha256(report),
                    "episodes_sha256": _sha256(episodes),
                },
            }
        ),
        encoding="utf-8",
    )
    if alter_hash:
        report.write_text(report.read_text(encoding="utf-8") + "\n", encoding="utf-8")
    return manifest, report, episodes


def test_config_is_canonical() -> None:
    config = load_analysis_config(CONFIG)
    assert config.final_policy == FINAL
    assert config.teacher_policy == TEACHER
    assert config.master_seed == 20260803
    assert len(config.location_bases) == 2
    assert len(config.style_filters) == 4
    assert len(config.speed_filters) == 4


def test_cell_assignment_uses_four_by_three_grid() -> None:
    config = load_analysis_config(CONFIG)
    assert heatmap_cell_id(
        {"intended_target_local_x": "-3", "intended_target_local_y": "2"},
        "intended_target",
        config,
    ) == "upper-left"
    assert heatmap_cell_id(
        {"intended_target_local_x": "1", "intended_target_local_y": "0.2"},
        "intended_target",
        config,
    ) == "low-centre-right"


def test_analysis_builds_two_views_and_32_filter_slices(tmp_path: Path) -> None:
    manifest, report, episodes = _fixture(tmp_path)
    artifact = build_analysis(
        config_path=CONFIG,
        source_manifest_path=manifest,
        report_path=report,
        episodes_path=episodes,
    )
    assert artifact["schema_id"] == "goalkeeper-analysis-v1"
    assert len(artifact["filter_slices"]) == 32
    assert [item["role"] for item in artifact["policies"]] == ["final", "teacher"]
    overall = artifact["overall_policy_rows"]
    assert overall[0]["save_rate"]["value"] == 0.5
    assert overall[1]["save_rate"]["value"] == 1.0
    assert overall[0]["attempts"] == 4
    assert overall[0]["all_attempts"] == 4
    default_slice = next(
        item
        for item in artifact["filter_slices"]
        if item["location_basis"] == "intended_target"
        and item["style_filter"] == "all"
        and item["speed_filter"] == "all"
    )
    assert sum(cell["sample_count"] for cell in default_slice["cells"]) == 4
    upper_left = next(
        cell for cell in default_slice["cells"] if cell["cell_id"] == "upper-left"
    )
    assert upper_left["teacher_gap_points"] == 0.0


def test_analysis_rejects_changed_source_hash(tmp_path: Path) -> None:
    manifest, report, episodes = _fixture(tmp_path, alter_hash=True)
    with pytest.raises(RuntimeError, match="source report hash changed"):
        build_analysis(
            config_path=CONFIG,
            source_manifest_path=manifest,
            report_path=report,
            episodes_path=episodes,
        )


def test_analysis_rejects_out_of_bounds_primary_location(tmp_path: Path) -> None:
    manifest, report, episodes = _fixture(tmp_path)
    rows = list(csv.DictReader(episodes.open(newline="", encoding="utf-8")))
    rows[0]["intended_target_local_x"] = "4.0"
    rows[4]["intended_target_local_x"] = "4.0"
    with episodes.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    payload = json.loads(manifest.read_text(encoding="utf-8"))
    payload["source_hashes"]["episodes_sha256"] = _sha256(episodes)
    manifest.write_text(json.dumps(payload), encoding="utf-8")
    with pytest.raises(RuntimeError, match="outside assignment bounds"):
        build_analysis(
            config_path=CONFIG,
            source_manifest_path=manifest,
            report_path=report,
            episodes_path=episodes,
        )
