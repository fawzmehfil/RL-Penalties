import json
from pathlib import Path

import numpy as np
import pytest
import yaml
from mlagents.plugins.trainer_type import register_trainer_plugins
from mlagents.trainers.settings import RunOptions

from penalty_shootout.evaluation.goalkeeper import (
    BenchmarkConfig,
    OnnxPolicy,
    RandomHybridV1Policy,
    ReactiveReachV1Policy,
    StandCenterV1Policy,
    aggregate_policy,
    load_benchmark_config,
    select_stage5_diagnostic_checkpoint,
    stage5_diagnostic_gate,
    validate_benchmark_config,
)


ROOT = Path(__file__).resolve().parents[2]


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def test_stage5_manifest_freezes_hybrid_control_contract() -> None:
    manifest = load_json(
        ROOT / "configs" / "environment" / "goalkeeper-control-v1.json"
    )

    assert manifest["behavior_name"] == "GoalkeeperControl-v1"
    assert manifest["observation_spec_id"] == "control-state-v1"
    assert manifest["reward_spec_id"] == "goalkeeper-sparse-v0"
    assert manifest["action_spec_id"] == "goalkeeper-hybrid-v1"
    assert manifest["motor_profile_id"] == "keeper-control-v1"
    assert manifest["vector_observation_size"] == 32
    assert manifest["continuous_actions"] == 4
    assert manifest["discrete_branches"] == [2]
    assert manifest["continuous_action_order"] == [
        "move_x",
        "aim_x",
        "aim_y",
        "reach",
    ]
    assert len(manifest["observation_order"]) == 32
    assert "requested_target" in manifest["excluded_privileged_fields"]
    assert "future_goal_plane_intersection" in (
        manifest["excluded_privileged_fields"]
    )
    assert manifest["motor"]["arms"]["solver"] == "deterministic-two-bone-ik"


def test_stage5_v2_manifest_adds_visible_ballistics_without_privilege() -> None:
    manifest = load_json(
        ROOT / "configs" / "environment" / "goalkeeper-control-v2.json"
    )

    assert manifest["behavior_name"] == "GoalkeeperControl-v2"
    assert manifest["observation_spec_id"] == "control-state-v2"
    assert manifest["vector_observation_size"] == 35
    assert manifest["observation_order"][-3:] == [
        "visible_time_to_goal_plane",
        "visible_predicted_aim_x",
        "visible_predicted_aim_y",
    ]
    assert "requested_target" in manifest["excluded_privileged_fields"]
    assert "future_goal_plane_intersection" in (
        manifest["excluded_privileged_fields"]
    )


@pytest.mark.parametrize(
    "name",
    [
        "goalkeeper-control-v1-id-20k",
        "goalkeeper-control-v1-speed-ood-20k",
        "goalkeeper-control-v1-edge-ood-20k",
    ],
)
def test_stage5_benchmark_configs_are_versioned_and_valid(name: str) -> None:
    config = load_benchmark_config(
        ROOT / "configs" / "benchmarks" / f"{name}.json"
    )

    assert config.benchmark_id == name
    assert config.behavior_name == "GoalkeeperControl-v1"
    assert config.observation_spec_id == "control-state-v1"
    assert config.action_spec_id == "goalkeeper-hybrid-v1"
    assert config.motor_profile_id == "keeper-control-v1"
    assert config.observation_shapes == ((32,),)
    assert config.continuous_actions == 4
    assert config.discrete_branches == (2,)
    assert config.stage5_lesson == 4
    assert config.total_attempts == 20_000
    assert config.environment_parameters["stage5.metrics_version"] == 4.0


def test_stage5_benchmark_rejects_v0_action_contract() -> None:
    config = load_benchmark_config(
        ROOT
        / "configs"
        / "benchmarks"
        / "goalkeeper-control-v1-id-20k.json"
    )
    broken = BenchmarkConfig(
        **{
            **config.__dict__,
            "continuous_actions": 0,
            "discrete_branches": (9,),
        }
    )

    with pytest.raises(ValueError, match="discrete branches|continuous"):
        validate_benchmark_config(broken)


def test_stage5_v2_benchmark_uses_35_float_contract() -> None:
    config = load_benchmark_config(
        ROOT
        / "configs"
        / "benchmarks"
        / "goalkeeper-control-v2-id-20k.json"
    )

    assert config.behavior_name == "GoalkeeperControl-v2"
    assert config.observation_spec_id == "control-state-v2"
    assert config.observation_shapes == ((35,),)
    assert config.continuous_actions == 4
    assert config.discrete_branches == (2,)
    assert config.environment_parameters[
        "stage5.reach_training_version"
    ] == 5.0


def test_stage5_ppo_yaml_matches_pinned_mlagents_schema() -> None:
    register_trainer_plugins()
    raw = yaml.safe_load(
        (
            ROOT
            / "configs"
            / "training"
            / "goalkeeper-control-v1-ppo.yaml"
        ).read_text()
    )
    options = RunOptions.from_dict(json.loads(json.dumps(raw)))

    behavior = options.behaviors["GoalkeeperControl-v1"]
    assert behavior.trainer_type == "ppo"
    assert behavior.max_steps == 8_000_000
    assert behavior.network_settings.hidden_units == 256
    assert behavior.network_settings.num_layers == 3
    assert len(options.environment_parameters["stage5.lesson"].curriculum) == 5


@pytest.mark.parametrize(
    ("name", "max_steps", "checkpoint_interval"),
    [
        ("goalkeeper-control-v2-ppo-diagnostic.yaml", 250_000, 50_000),
        ("goalkeeper-control-v2-ppo.yaml", 4_000_000, 250_000),
    ],
)
def test_stage5_v2_ppo_yaml_matches_pinned_mlagents_schema(
    name: str,
    max_steps: int,
    checkpoint_interval: int,
) -> None:
    register_trainer_plugins()
    raw = yaml.safe_load(
        (ROOT / "configs" / "training" / name).read_text()
    )
    options = RunOptions.from_dict(json.loads(json.dumps(raw)))

    behavior = options.behaviors["GoalkeeperControl-v2"]
    assert behavior.max_steps == max_steps
    assert behavior.checkpoint_interval == checkpoint_interval
    assert behavior.network_settings.normalize is False
    assert len(options.environment_parameters["stage5.lesson"].curriculum) == 5
    assert options.environment_parameters[
        "stage5.reach_training_version"
    ].curriculum[0].value.value == 5.0


@pytest.mark.parametrize(
    ("name", "max_steps", "checkpoint_interval"),
    [
        ("goalkeeper-control-v1-ppo-reach.yaml", 8_000_000, 500_000),
        (
            "goalkeeper-control-v1-ppo-reach-diagnostic.yaml",
            1_000_000,
            250_000,
        ),
        (
            "goalkeeper-control-v1-ppo-reach-v2.yaml",
            8_000_000,
            500_000,
        ),
        (
            "goalkeeper-control-v1-ppo-reach-v2-diagnostic.yaml",
            1_000_000,
            250_000,
        ),
        (
            "goalkeeper-control-v1-ppo-reach-v3.yaml",
            8_000_000,
            500_000,
        ),
        (
            "goalkeeper-control-v1-ppo-reach-v3-diagnostic.yaml",
            1_000_000,
            250_000,
        ),
        (
            "goalkeeper-control-v1-ppo-reach-v4.yaml",
            8_000_000,
            500_000,
        ),
        (
            "goalkeeper-control-v1-ppo-reach-v4-diagnostic.yaml",
            1_000_000,
            200_000,
        ),
    ],
)
def test_stage5_reach_training_yaml_matches_pinned_mlagents_schema(
    name: str,
    max_steps: int,
    checkpoint_interval: int,
) -> None:
    register_trainer_plugins()
    raw = yaml.safe_load(
        (ROOT / "configs" / "training" / name).read_text()
    )
    options = RunOptions.from_dict(json.loads(json.dumps(raw)))

    behavior = options.behaviors["GoalkeeperControl-v1"]
    assert behavior.max_steps == max_steps
    assert behavior.checkpoint_interval == checkpoint_interval
    assert len(options.environment_parameters["stage5.lesson"].curriculum) == 5
    reach_training = options.environment_parameters[
        "stage5.reach_training_enabled"
    ]
    assert len(reach_training.curriculum) == 1
    assert reach_training.curriculum[0].value.value == 1.0
    if "-v2" in name:
        reach_version = options.environment_parameters[
            "stage5.reach_training_version"
        ]
        assert len(reach_version.curriculum) == 1
        assert reach_version.curriculum[0].value.value == 2.0
    if "-v3" in name:
        reach_version = options.environment_parameters[
            "stage5.reach_training_version"
        ]
        assert len(reach_version.curriculum) == 1
        assert reach_version.curriculum[0].value.value == 3.0
    if "-v4" in name:
        reach_version = options.environment_parameters[
            "stage5.reach_training_version"
        ]
        assert len(reach_version.curriculum) == 1
        assert reach_version.curriculum[0].value.value == 4.0


def test_stage5_scripted_policies_emit_bounded_masked_hybrid_actions() -> None:
    observations = np.zeros((2, 32), dtype=np.float32)
    mask = np.zeros((2, 2), dtype=bool)
    mask[0, 1] = True

    stand_continuous, stand_discrete = StandCenterV1Policy().hybrid_actions(
        observations,
        mask,
    )
    assert stand_continuous.shape == (2, 4)
    assert stand_continuous[:, 3].tolist() == [-1.0, -1.0]
    assert stand_discrete.tolist() == [[0], [0]]

    random_continuous, random_discrete = RandomHybridV1Policy(
        seed=7
    ).hybrid_actions(observations, mask)
    assert random_continuous.shape == (2, 4)
    assert np.all(random_continuous >= -1.0)
    assert np.all(random_continuous <= 1.0)
    assert random_discrete[0, 0] == 0


def test_stage5_reactive_policy_uses_visible_intercept_and_commit_horizon() -> None:
    observations = np.zeros((1, 32), dtype=np.float32)
    observations[0, 0] = 0.2
    observations[0, 1] = 0.3
    observations[0, 2] = 0.4
    observations[0, 3] = 0.0
    observations[0, 4] = 0.2
    observations[0, 5] = -0.4
    continuous, discrete = ReactiveReachV1Policy(
        commit_horizon=0.62
    ).hybrid_actions(observations, None)

    assert continuous.shape == (1, 4)
    assert continuous[0, 0] > 0.0
    assert continuous[0, 1] > 0.0
    assert continuous[0, 3] == 1.0
    assert discrete.tolist() == [[1]]


def test_stage5_onnx_policy_emits_continuous_and_discrete_actions_once() -> None:
    class FakeSession:
        def __init__(self) -> None:
            self.calls = 0
            self.last_feed: dict[str, np.ndarray] = {}

        def run(
            self,
            output_names: list[str],
            feed: dict[str, np.ndarray],
        ) -> list[np.ndarray]:
            self.calls += 1
            self.last_feed = feed
            return [
                np.asarray([[1], [1]], dtype=np.int64),
                np.asarray(
                    [[2.0, -2.0, 0.5, 1.0], [0.1, 0.2, 0.3, 0.4]],
                    dtype=np.float32,
                ),
            ]

    config = load_benchmark_config(
        ROOT
        / "configs"
        / "benchmarks"
        / "goalkeeper-control-v1-id-20k.json"
    )
    session = FakeSession()
    policy = OnnxPolicy.__new__(OnnxPolicy)
    policy.model_path = Path("control.onnx")
    policy.name = "onnx:control"
    policy._session = session
    policy._output_name = "deterministic_discrete_actions"
    policy._continuous_output_name = "deterministic_continuous_actions"
    policy._has_continuous_output = True
    policy._memory_input_name = "recurrent_in"
    policy._memory_output_name = "recurrent_out"
    policy._input_names = {"obs_0", "action_masks"}
    policy._memory_by_agent_id = {}
    policy._memory_shape = None
    mask = np.zeros((2, 2), dtype=bool)
    mask[0, 1] = True

    actions = policy.action_tuple(
        np.zeros((2, 32), dtype=np.float32),
        mask,
        np.asarray([1, 2], dtype=np.int64),
        config,
    )

    assert session.calls == 1
    assert session.last_feed["action_masks"].shape == (2, 2)
    assert actions.continuous.tolist() == [
        [1.0, -1.0, 0.5, 1.0],
        pytest.approx([0.1, 0.2, 0.3, 0.4]),
    ]
    assert actions.discrete.tolist() == [[0], [1]]


def test_stage5_aggregation_reports_commit_and_motor_metrics() -> None:
    config = load_benchmark_config(
        ROOT
        / "configs"
        / "benchmarks"
        / "goalkeeper-control-v1-id-20k.json"
    )
    episodes = [
        {
            "outcome": "Saved",
            "requested_target_local": {"x": 2.0, "y": 1.8, "z": 0.0},
            "ball_flight_time": 0.55,
            "has_first_dive": False,
            "first_accepted_dive_action": "",
            "accepted_action_counts": [0] * 9,
            "goalkeeper_contact": True,
            "glove_contact": True,
            "action_mask_violations": 0,
            "duplicate_terminal_events": 0,
            "has_save_commitment": True,
            "first_commit_ball_flight_time": 0.12,
            "first_commit_visible_time_to_goal_plane": 0.58,
            "first_commit_reach_demand": 1.0,
            "first_commit_reach_extension": 0.72,
            "first_commit_was_immediate": False,
            "first_commit_was_premature": False,
            "first_commit_was_late": False,
            "first_commit_was_timely": True,
            "first_commit_aim": {"x": 0.55, "y": 0.5},
            "first_commit_visible_aim_error": 0.15,
            "first_commit_desired_reach": 0.9,
            "first_commit_reach_shortfall": 0.0,
            "first_eligible_commit_decision_index": 2,
            "first_eligible_commit_ball_flight_time": 0.08,
            "eligible_commit_decisions_before_commit": 1,
            "first_goalkeeper_contact_part": "LeftGlove",
            "goalkeeper_root_distance": 2.2,
            "goalkeeper_peak_root_speed": 5.1,
            "goalkeeper_peak_reach_extension": 1.0,
            "minimum_glove_ball_distance": 0.03,
            "control_command_clamp_count": 0,
            "control_target_clamp_count": 1,
            "root_target_saturation_count": 1,
            "root_target_saturation_distance": 0.25,
            "training_decision_shaping_reward": 0.08,
            "policy_action_override_count": 0,
            "accepted_control_decision_count": 5,
            "control_move_command_count": 4,
            "control_reach_command_count": 3,
            "control_absolute_action_sums": [3.0, 2.0, 1.0, 4.0],
            "control_saturation_counts": [1, 0, 0, 2],
        },
        {
            "outcome": "Goal",
            "requested_target_local": {"x": -2.0, "y": 0.4, "z": 0.0},
            "ball_flight_time": 0.6,
            "has_first_dive": False,
            "first_accepted_dive_action": "",
            "accepted_action_counts": [0] * 9,
            "goalkeeper_contact": False,
            "glove_contact": False,
            "action_mask_violations": 0,
            "duplicate_terminal_events": 0,
            "has_save_commitment": False,
            "first_goalkeeper_contact_part": "None",
            "goalkeeper_root_distance": 0.4,
            "goalkeeper_peak_root_speed": 2.0,
            "goalkeeper_peak_reach_extension": 0.0,
            "minimum_glove_ball_distance": 1.2,
            "control_command_clamp_count": 0,
            "control_target_clamp_count": 0,
            "root_target_saturation_count": 0,
            "root_target_saturation_distance": 0.0,
            "training_decision_shaping_reward": 0.0,
            "policy_action_override_count": 0,
            "accepted_control_decision_count": 5,
            "control_move_command_count": 1,
            "control_reach_command_count": 0,
            "control_absolute_action_sums": [1.0, 2.0, 1.0, 1.0],
            "control_saturation_counts": [0, 0, 0, 0],
        },
    ]

    report = aggregate_policy(
        StandCenterV1Policy(),
        episodes,
        config,
        attempts_per_arena=1,
    )

    assert report["commit_rate"]["value"] == 0.5
    assert report["first_commit_ball_flight_time"]["mean"] == 0.12
    assert (
        report["first_commit_visible_time_to_goal_plane"]["mean"] == 0.58
    )
    assert report["first_commit_reach_demand"]["mean"] == 1.0
    assert report["first_commit_reach_extension"]["mean"] == 0.72
    assert report["immediate_commit_rate"]["value"] == 0.0
    assert report["premature_commit_rate"]["value"] == 0.0
    assert report["late_commit_rate"]["value"] == 0.0
    assert report["timely_commit_rate"]["value"] == 0.5
    assert report["first_commit_visible_aim_error_m"]["mean"] == 0.15
    assert report["first_commit_desired_reach"]["mean"] == 0.9
    assert report["first_commit_reach_shortfall"]["mean"] == 0.0
    assert report["first_eligible_commit_decision_index"]["mean"] == 2.0
    assert (
        report["first_eligible_commit_ball_flight_time"]["mean"] == 0.08
    )
    assert (
        report["eligible_commit_decisions_before_commit"]["mean"] == 0.5
    )
    assert report["goalkeeper_root_distance_m"]["mean"] == 1.3
    assert report["goalkeeper_peak_reach_extension"]["maximum"] == 1.0
    assert report["control_target_clamp_count"] == 1
    assert report["control_target_clamp_attempt_rate"]["value"] == 0.5
    assert report["root_target_saturation_count"] == 1
    assert report["root_target_saturation_attempt_rate"]["value"] == 0.5
    assert report["root_target_saturation_distance_m"]["mean"] == 0.25
    assert report["training_decision_shaping_reward"]["mean"] == 0.04
    assert report["policy_action_override_count"] == 0
    assert report["saturated_shot_save_rate"]["value"] == 1.0
    assert report["glove_save_rate"]["value"] == 0.5
    assert report["glove_first_save_rate"]["value"] == 0.5
    assert report["glove_first_contact_rate"]["value"] == 1.0
    assert report["body_first_contact_rate"]["value"] == 0.0
    assert report["control_usage"]["accepted_decisions"] == 10
    assert report["control_usage"]["move_command_rate"] == 0.5
    assert report["control_usage"]["channels"]["reach"]["saturation_count"] == 2
    assert "high-right" in report["by_first_commit_aim_region"]
    assert "in-window" in report["by_first_commit_timing_band"]
    assert "LeftGlove" in report["by_first_contact_part"]
    assert report["stage5_diagnostic_gate"]["passed"] is False


def test_stage5_diagnostic_selection_uses_behavioral_gate_before_save_rate() -> None:
    passing = {
        "policy": "onnx:passing",
        "policy_type": "onnx",
        "save_rate": {"value": 0.30},
        "glove_save_rate": {"value": 0.15},
        "glove_contact_rate": {"value": 0.30},
        "stage5_diagnostic_gate": {
            "passed": True,
            "checks_passed": 16,
            "checks_total": 16,
            "failed_checks": [],
        },
    }
    higher_save_failing = {
        "policy": "onnx:torso-only",
        "policy_type": "onnx",
        "save_rate": {"value": 0.50},
        "glove_save_rate": {"value": 0.02},
        "glove_contact_rate": {"value": 0.05},
        "stage5_diagnostic_gate": {
            "passed": False,
            "checks_passed": 8,
            "checks_total": 16,
            "failed_checks": ["glove_contact_rate"],
        },
    }

    selected = select_stage5_diagnostic_checkpoint(
        [higher_save_failing, passing]
    )

    assert selected is not None
    assert selected["selected_policy"] == "onnx:passing"
    assert selected["passed"] is True


def test_stage5_v2_gate_checks_one_request_one_command_lifecycle() -> None:
    passing_rate = {"value": 0.30, "successes": 30, "total": 100}
    zero_rate = {"value": 0.0, "successes": 0, "total": 100}
    policy = {
        "attempts": 100,
        "save_rate": passing_rate,
        "glove_contact_rate": passing_rate,
        "glove_save_rate": {"value": 0.15},
        "goalkeeper_peak_reach_extension": {"mean": 0.8},
        "by_height_band": {
            "high": {"save_rate": {"value": 0.20}},
        },
        "first_commit_aim_error_m": {"mean": 0.5},
        "immediate_commit_rate": zero_rate,
        "premature_commit_rate": zero_rate,
        "late_commit_rate": zero_rate,
        "timely_commit_rate": passing_rate,
        "first_commit_reach_shortfall": {"mean": 0.5},
        "policy_action_override_count": 0,
        "control_command_clamp_count": 0,
        "invalid_rate": zero_rate,
        "timeout_rate": zero_rate,
        "action_mask_violations": 0,
        "policy_decision_request_count": 1_100,
        "policy_decision_consumed_count": 1_000,
        "policy_decision_discarded_count": 100,
        "accepted_control_decision_count": 1_000,
        "policy_decision_duplicate_request_count": 0,
        "policy_decision_missing_action_count": 0,
    }

    gate = stage5_diagnostic_gate(policy, control_version=2)
    assert gate["passed"] is True
    assert "timely_commit_rate" not in gate["failed_checks"]

    policy["policy_decision_missing_action_count"] = 1
    failed = stage5_diagnostic_gate(policy, control_version=2)
    assert failed["passed"] is False
    assert "missing_decision_actions" in failed["failed_checks"]
