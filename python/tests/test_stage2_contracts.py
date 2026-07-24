import json
from pathlib import Path

from mlagents.trainers.settings import RunOptions
from mlagents.plugins.trainer_type import register_trainer_plugins

from penalty_shootout.baselines import RandomLegal, StandCenter


ROOT = Path(__file__).resolve().parents[2]


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def test_goalkeeper_state_manifest_is_complete() -> None:
    manifest = load_json(ROOT / "configs" / "environment" / "goalkeeper-state-v0.json")

    assert manifest["environment_id"] == "penalty-shootout-kernel-v1"
    assert manifest["behavior_name"] == "GoalkeeperState-v0"
    assert manifest["observation_spec_id"] == "state-v0"
    assert manifest["reward_spec_id"] == "goalkeeper-sparse-v0"
    assert manifest["vector_observation_size"] == 24
    assert manifest["discrete_branches"] == [9]
    assert len(manifest["observation_order"]) == 24
    assert "requested_target" in manifest["excluded_privileged_fields"]
    assert "future_goal_plane_intersection" in manifest["excluded_privileged_fields"]


def test_stage2_ppo_yaml_matches_pinned_schema() -> None:
    config_path = ROOT / "configs" / "training" / "goalkeeper-state-v0-ppo.yaml"
    register_trainer_plugins()
    options = RunOptions.from_dict(json.loads(json.dumps(__import__("yaml").safe_load(config_path.read_text()))))

    assert "GoalkeeperState-v0" in options.behaviors
    behavior = options.behaviors["GoalkeeperState-v0"]
    assert behavior.trainer_type == "ppo"
    assert behavior.max_steps == 5_000_000
    assert options.environment_parameters is not None
    assert "stage2.lesson" in options.environment_parameters
    assert len(options.environment_parameters["stage2.lesson"].curriculum) == 4


def test_stage2_baselines_emit_legal_actions() -> None:
    assert StandCenter().act() == 0

    mask = [False] + [True] * 8
    assert RandomLegal(seed=1).act(mask) == 0

    mask = [True, False, False, True, True, True, True, True, True]
    sampled = {RandomLegal(seed=seed).act(mask) for seed in range(20)}
    assert sampled <= {1, 2}
    assert sampled


def test_stage2_training_summary_is_explicitly_pending_until_models_exist() -> None:
    report = load_json(ROOT / "docs" / "stage2-training-summary.json")
    assert report["required_training_seeds"] == 3
    assert report["passed"] is False
    assert "not run" in report["status"]
