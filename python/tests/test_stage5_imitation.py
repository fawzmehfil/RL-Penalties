import json
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import yaml
from mlagents.plugins.trainer_type import register_trainer_plugins
from mlagents.trainers.settings import RunOptions

from penalty_shootout.evaluation.goalkeeper import (
    load_benchmark_config,
    reactive_reach_command_from_visible_state_v1,
    stage5_diagnostic_gate,
)
from penalty_shootout.training.goalkeeper_demo import (
    _inspect_pairs,
    _validate_teacher_report,
    validate_demonstrations,
)
from penalty_shootout.training.stage5_imitation_evidence import (
    record_stage5_imitation_evidence,
)


ROOT = Path(__file__).resolve().parents[2]
CONTRACT_PATH = (
    ROOT
    / "configs"
    / "demonstrations"
    / "goalkeeper-control-v2-reactive-demo-v1.json"
)


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def test_stage5_reactive_demonstration_contract_is_canonical() -> None:
    contract = load_json(CONTRACT_PATH)

    assert contract["demonstration_contract_id"] == (
        "goalkeeper-control-v2-reactive-demo-v1"
    )
    assert contract["behavior_name"] == "GoalkeeperControl-v2"
    assert contract["observation_spec_id"] == "control-state-v2"
    assert contract["observation_shapes"] == [[35]]
    assert contract["continuous_actions"] == 4
    assert contract["discrete_branches"] == [2]
    assert contract["arena_count"] == 16
    assert contract["attempts_per_arena"] == 1250
    assert contract["total_attempts"] == 20_000
    assert contract["master_seed"] == 20260723
    assert contract["dataset_requirements"]["commits_per_episode"] == 1


def test_stage5_reactive_teacher_matches_shared_parity_fixtures() -> None:
    fixtures = load_json(
        ROOT
        / "configs"
        / "demonstrations"
        / "reactive-teacher-v1-parity.json"
    )["fixtures"]

    for fixture in fixtures:
        actions, commit = reactive_reach_command_from_visible_state_v1(
            ball_position=tuple(fixture["ball_position"]),
            ball_velocity=tuple(fixture["ball_velocity"]),
            gravity=tuple(fixture["gravity"]),
            keeper_x=fixture["keeper_x"],
            commit_horizon=fixture["commit_horizon"],
        )
        np.testing.assert_allclose(
            actions,
            fixture["expected_actions"],
            rtol=0.0,
            atol=1e-5,
            err_msg=fixture["name"],
        )
        assert bool(commit and fixture["can_commit"]) is (
            fixture["expected_commit"]
        )


def test_stage5_bc_training_configs_parse_with_pinned_mlagents() -> None:
    register_trainer_plugins()
    expectations = (
        (
            "goalkeeper-control-v2-bc-diagnostic.yaml",
            500_000,
            300_000,
            50_000,
        ),
        (
            "goalkeeper-control-v2-bc-ppo.yaml",
            2_000_000,
            500_000,
            100_000,
        ),
    )
    for name, max_steps, bc_steps, checkpoint_interval in expectations:
        raw = yaml.safe_load(
            (
                ROOT
                / "configs"
                / "training"
                / name
            ).read_text(encoding="utf-8")
        )
        options = RunOptions.from_dict(
            json.loads(json.dumps(raw))
        )
        behavior = options.behaviors["GoalkeeperControl-v2"]
        bc = behavior.behavioral_cloning

        assert behavior.max_steps == max_steps
        assert behavior.checkpoint_interval == checkpoint_interval
        assert behavior.network_settings.normalize is False
        assert behavior.hyperparameters.learning_rate == 3e-4
        assert behavior.hyperparameters.beta == 1e-3
        assert bc is not None
        assert bc.strength == 0.5
        assert bc.steps == bc_steps
        assert bc.batch_size == 1024
        assert bc.num_epoch == 3
        assert bc.samples_per_update == 32768
        lesson = options.environment_parameters[
            "stage5.lesson"
        ].curriculum
        assert len(lesson) == 1
        assert lesson[0].value.value == 4.0
        assert lesson[0].completion_criteria is None


def test_stage5_imitation_benchmark_uses_strict_gate_profile() -> None:
    config = load_benchmark_config(
        ROOT
        / "configs"
        / "benchmarks"
        / "goalkeeper-control-v2-bc-id-20k.json"
    )

    assert config.stage5_gate_profile == "imitation-v1"
    assert config.behavior_name == "GoalkeeperControl-v2"
    assert config.observation_shapes == ((35,),)
    assert config.continuous_actions == 4
    assert config.discrete_branches == (2,)


def test_stage5_imitation_gate_rejects_deterministic_no_commit_policy() -> None:
    policy = {
        "attempts": 100,
        "save_rate": {"value": 0.40},
        "commit_rate": {"value": 0.90},
        "glove_contact_rate": {"value": 0.45},
        "glove_save_rate": {"value": 0.25},
        "goalkeeper_peak_reach_extension": {"mean": 0.80},
        "by_height_band": {
            "high": {"save_rate": {"value": 0.35}},
        },
        "first_commit_aim_error_m": {"mean": 0.70},
        "immediate_commit_rate": {"value": 0.0},
        "premature_commit_rate": {"value": 0.0},
        "late_commit_rate": {"value": 0.0},
        "timely_commit_rate": {"value": 0.90},
        "first_commit_reach_shortfall": {"mean": 0.0},
        "policy_action_override_count": 0,
        "control_command_clamp_count": 0,
        "invalid_rate": {"successes": 0},
        "timeout_rate": {"successes": 0},
        "action_mask_violations": 0,
        "policy_decision_request_count": 1000,
        "policy_decision_consumed_count": 950,
        "policy_decision_discarded_count": 50,
        "accepted_control_decision_count": 950,
        "policy_decision_duplicate_request_count": 0,
        "policy_decision_missing_action_count": 0,
    }

    passing = stage5_diagnostic_gate(
        policy,
        control_version=2,
        profile="imitation-v1",
    )
    assert passing["passed"] is True

    policy["commit_rate"] = {"value": 0.0}
    failed = stage5_diagnostic_gate(
        policy,
        control_version=2,
        profile="imitation-v1",
    )
    assert failed["passed"] is False
    assert "commit_rate" in failed["failed_checks"]


def test_stage5_demo_pair_inspection_enforces_one_legal_commit() -> None:
    def pair(
        *,
        done: bool,
        commit: int = 0,
        can_commit: float = 1.0,
    ) -> SimpleNamespace:
        observation = [0.0] * 35
        observation[29] = can_commit
        info = SimpleNamespace(
            done=done,
            action_mask=[False, not bool(can_commit)],
            observations=[
                SimpleNamespace(
                    float_data=SimpleNamespace(data=observation)
                )
            ],
        )
        action = SimpleNamespace(
            continuous_actions=[0.1, -0.2, 0.3, 1.0],
            discrete_actions=[commit],
        )
        return SimpleNamespace(agent_info=info, action_info=action)

    valid_pairs = [
        pair(done=False, can_commit=1.0),
        pair(done=False, commit=1, can_commit=0.0),
        pair(done=True),
        pair(done=False, can_commit=1.0),
        pair(done=False, commit=1, can_commit=0.0),
        pair(done=True),
    ]
    valid = _inspect_pairs(valid_pairs, 4, 1)
    assert valid["terminal_episodes"] == 2
    assert valid["commit_actions"] == 2
    assert valid["episodes_with_wrong_commit_count"] == 0
    assert valid["illegal_commit_count"] == 0

    invalid_pairs = [
        pair(done=False, can_commit=0.0),
        pair(done=False, commit=1, can_commit=1.0),
        pair(done=False, commit=1, can_commit=0.0),
        pair(done=True),
    ]
    invalid = _inspect_pairs(invalid_pairs, 4, 1)
    assert invalid["episodes_with_wrong_commit_count"] == 1
    assert invalid["illegal_commit_count"] == 1


def test_stage5_teacher_report_requires_quality_identity_and_quotas() -> None:
    contract = load_json(CONTRACT_PATH)
    report = {
        "demonstration_contract_id":
            contract["demonstration_contract_id"],
        "behavior_name": contract["behavior_name"],
        "observation_spec_id": contract["observation_spec_id"],
        "action_spec_id": contract["action_spec_id"],
        "scenario_suite_id": contract["scenario_suite_id"],
        "master_seed": str(contract["master_seed"]),
        "arena_count": 16,
        "attempts_per_arena": 1250,
        "total_attempts": 20_000,
        "save_rate": 0.51,
        "glove_contact_rate": 0.66,
        "high_shot_save_rate": 0.56,
        "invalids": 0,
        "timeouts": 0,
        "off_target": 0,
        "action_mask_violations": 0,
        "control_command_clamps": 0,
        "policy_decision_duplicate_requests": 0,
        "policy_decision_missing_actions": 0,
        "arenas": [
            {"arena_id": arena_id, "attempts": 1250}
            for arena_id in range(16)
        ],
    }
    errors: list[str] = []
    _validate_teacher_report(report, contract, errors)
    assert errors == []

    report["save_rate"] = 0.49
    report["arenas"][3]["attempts"] = 1249
    _validate_teacher_report(report, contract, errors := [])
    assert any("save_rate" in error for error in errors)
    assert any("arena 3 attempts" in error for error in errors)


def test_stage5_demo_validator_fails_without_replacing_missing_data(
    tmp_path: Path,
) -> None:
    demo_dir = tmp_path / "missing"
    validation = validate_demonstrations(
        demo_dir,
        CONTRACT_PATH,
    )

    assert validation.passed is False
    assert "no .demo files" in validation.errors[0]
    assert not demo_dir.exists()


def test_stage5_evidence_postprocessor_records_selected_checkpoint(
    tmp_path: Path,
) -> None:
    manifest_path = tmp_path / "manifest.json"
    evaluation_path = tmp_path / "evaluation.json"
    implementation_path = tmp_path / "implementation.json"
    summary_path = tmp_path / "summary.json"
    manifest_path.write_text(
        json.dumps(
            {
                "status": "passed",
                "terminal_episodes": 20_000,
                "decision_steps": 180_000,
                "commit_actions": 20_000,
                "continuous_action_coverage": {},
                "teacher_quality": {
                    "save_rate": 0.57,
                },
                "demonstration_files": [
                    {
                        "path": "GKCtrlV2A000.demo",
                        "sha256": "abc",
                    }
                ],
            }
        ),
        encoding="utf-8",
    )
    policy = {
        "policy": "onnx:GoalkeeperControl-v2-500000",
        "policy_type": "onnx",
        "attempts": 400,
        "save_rate": {"value": 0.40},
        "commit_rate": {"value": 0.90},
        "glove_contact_rate": {"value": 0.45},
        "glove_save_rate": {"value": 0.25},
        "by_height_band": {
            "high": {"save_rate": {"value": 0.35}},
        },
        "first_commit_aim_error_m": {"mean": 0.70},
        "goalkeeper_peak_reach_extension": {"mean": 0.80},
        "invalid_rate": {"successes": 0},
        "timeout_rate": {"successes": 0},
        "action_mask_violations": 0,
        "control_command_clamp_count": 0,
        "policy_decision_request_count": 4000,
        "policy_decision_consumed_count": 3600,
        "policy_decision_discarded_count": 400,
        "accepted_control_decision_count": 3600,
        "policy_decision_duplicate_request_count": 0,
        "policy_decision_missing_action_count": 0,
        "stage5_diagnostic_gate": {
            "passed": True,
            "failed_checks": [],
        },
    }
    evaluation_path.write_text(
        json.dumps(
            {
                "run_id": "stage5-bc-screen",
                "total_attempts": 400,
                "policies": [policy],
                "stage5_diagnostic_selection": {
                    "selected_policy": policy["policy"],
                    "passed": True,
                    "reason": "passed every check",
                    "checks_passed": 17,
                    "checks_total": 17,
                    "failed_checks": [],
                },
            }
        ),
        encoding="utf-8",
    )
    implementation = load_json(
        ROOT / "docs" / "stage5-imitation-bootstrap-report.json"
    )
    summary = load_json(
        ROOT / "docs" / "stage5-training-summary.json"
    )
    implementation_path.write_text(
        json.dumps(implementation),
        encoding="utf-8",
    )
    summary_path.write_text(
        json.dumps(summary),
        encoding="utf-8",
    )

    outcome = record_stage5_imitation_evidence(
        manifest_path=manifest_path,
        evaluation_report_path=evaluation_path,
        implementation_report_path=implementation_path,
        training_summary_path=summary_path,
        training_run_id="gk-control-v2-bc-bootstrap_seed-001",
        seed=1,
    )

    assert outcome["passed"] is True
    updated_implementation = load_json(implementation_path)
    assert updated_implementation["status"] == (
        "diagnostic-completed-passed"
    )
    assert updated_implementation["demonstration"][
        "terminal_episodes"
    ] == 20_000
    assert updated_implementation["diagnostic"][
        "selected_checkpoint"
    ] == policy["policy"]
    updated_summary = load_json(summary_path)
    assert updated_summary["status"] == (
        "imitation-diagnostic-passed"
    )
    assert updated_summary["selected_checkpoint"] == policy["policy"]
    assert updated_summary["training_runs"][-1]["save_rate"] == 0.40
