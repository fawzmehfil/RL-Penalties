import json
from pathlib import Path

import pytest
import yaml
from mlagents.plugins.trainer_type import register_trainer_plugins
from mlagents.trainers.settings import RunOptions

from penalty_shootout.evaluation.goalkeeper import (
    BenchmarkConfig,
    load_benchmark_config,
    validate_benchmark_config,
)


ROOT = Path(__file__).resolve().parents[2]


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def test_goalkeeper_robust_manifest_preserves_stage2_vector_contract() -> None:
    state = load_json(ROOT / "configs" / "environment" / "goalkeeper-state-v0.json")
    robust = load_json(ROOT / "configs" / "environment" / "goalkeeper-robust-v0.json")

    assert robust["behavior_name"] == "GoalkeeperRobust-v0"
    assert robust["observation_spec_id"] == "state-po-v0"
    assert robust["reward_spec_id"] == "goalkeeper-sparse-v0"
    assert robust["vector_observation_size"] == 24
    assert robust["discrete_branches"] == [9]
    assert robust["observation_order"] == state["observation_order"]
    assert "requested_target" in robust["excluded_privileged_fields"]
    assert "future_goal_plane_intersection" in robust["excluded_privileged_fields"]
    assert robust["partial_observation"]["noise_application"] == "after_delay"


@pytest.mark.parametrize(
    "name",
    [
        "goalkeeper-robust-v0-id-20k",
        "goalkeeper-robust-v0-delay-noise-20k",
        "goalkeeper-robust-v0-speed-ood-20k",
        "goalkeeper-robust-v0-edge-ood-20k",
    ],
)
def test_stage4_benchmark_configs_are_versioned_and_valid(name: str) -> None:
    config = load_benchmark_config(ROOT / "configs" / "benchmarks" / f"{name}.json")

    assert config.benchmark_id == name
    assert config.behavior_name == "GoalkeeperRobust-v0"
    assert config.observation_spec_id == "state-po-v0"
    assert config.observation_shapes == ((24,),)
    assert config.discrete_branches == (9,)
    assert config.total_attempts == 20_000
    assert config.environment_parameters["stage2.lesson"] == 3.0


def test_stage4_benchmark_config_rejects_mismatched_observation_contract() -> None:
    config = load_benchmark_config(
        ROOT / "configs" / "benchmarks" / "goalkeeper-robust-v0-id-20k.json"
    )
    broken = BenchmarkConfig(
        **{**config.__dict__, "observation_spec_id": "state-v0"}
    )

    with pytest.raises(ValueError, match="state-po-v0"):
        validate_benchmark_config(broken)


@pytest.mark.parametrize(
    "path, recurrent",
    [
        ("configs/training/goalkeeper-robust-v0-ppo.yaml", False),
        ("configs/training/goalkeeper-robust-v0-ppo-recurrent.yaml", True),
    ],
)
def test_stage4_ppo_yaml_matches_pinned_schema(path: str, recurrent: bool) -> None:
    register_trainer_plugins()
    options = RunOptions.from_dict(
        json.loads(json.dumps(yaml.safe_load((ROOT / path).read_text())))
    )

    assert "GoalkeeperRobust-v0" in options.behaviors
    behavior = options.behaviors["GoalkeeperRobust-v0"]
    assert behavior.trainer_type == "ppo"
    assert behavior.max_steps == 5_000_000
    assert behavior.network_settings.memory is not None if recurrent else True
    if recurrent:
        assert behavior.network_settings.memory.sequence_length == 64
        assert behavior.network_settings.memory.memory_size == 128
    assert options.environment_parameters is not None
    assert "stage2.lesson" in options.environment_parameters
    assert "stage4.obs_delay_steps" in options.environment_parameters
