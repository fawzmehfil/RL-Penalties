import json
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import torch

from penalty_shootout.evaluation.goalkeeper import SplitSupervisedPolicy
from penalty_shootout.training.stage5_split_evidence import (
    combined_unity_gate,
    interception_unity_gate,
    smoke_unity_gate,
)
from penalty_shootout.training.stage5_split_supervision import (
    AlignedEpisode,
    CommitTimingModel,
    InterceptionModel,
    balanced_timing_rows,
    _export_onnx,
    offline_gate,
    realign_demo_pairs,
    split_episode_keys,
    timing_sequence_metrics,
)


ROOT = Path(__file__).resolve().parents[2]
CONTRACT_PATH = (
    ROOT
    / "configs"
    / "supervision"
    / "goalkeeper-control-v2-split-supervision-v1.json"
)


def load_contract() -> dict:
    return json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))


def _info(
    marker: float,
    *,
    done: bool = False,
    can_commit: bool = True,
) -> SimpleNamespace:
    observation = [0.0] * 35
    observation[0] = marker
    observation[29] = float(can_commit)
    return SimpleNamespace(
        done=done,
        action_mask=[False, not can_commit],
        observations=[
            SimpleNamespace(
                float_data=SimpleNamespace(data=observation)
            )
        ],
    )


def _pair(
    info: SimpleNamespace,
    *,
    continuous: tuple[float, float, float, float],
    commit: int,
) -> SimpleNamespace:
    return SimpleNamespace(
        agent_info=info,
        action_info=SimpleNamespace(
            continuous_actions=list(continuous),
            discrete_actions=[commit],
        ),
    )


def _episode(arena_id: int, ordinal: int) -> AlignedEpisode:
    return AlignedEpisode(
        arena_id=arena_id,
        episode_ordinal=ordinal,
        observations=np.zeros((1, 35), dtype=np.float32),
        continuous_actions=np.zeros((1, 4), dtype=np.float32),
        commit_actions=np.ones(1, dtype=np.int8),
        commit_allowed=np.ones(1, dtype=bool),
    )


def _rate(value: float) -> dict:
    return {"value": value, "successes": int(value > 0), "total": 1}


def _policy_report(
    name: str,
    *,
    attempts: int,
    save: float,
    glove: float,
    high: float,
    commit: float = 1.0,
    aim_error: float = 0.1,
    reach: float = 0.95,
) -> dict:
    return {
        "policy": name,
        "complete": True,
        "attempts": attempts,
        "save_rate": _rate(save),
        "commit_rate": _rate(commit),
        "glove_contact_rate": _rate(glove),
        "glove_save_rate": _rate(0.25),
        "by_height_band": {"high": {"save_rate": _rate(high)}},
        "first_commit_aim_error_m": {"mean": aim_error},
        "goalkeeper_peak_reach_extension": {"mean": reach},
        "invalid_rate": {"successes": 0},
        "timeout_rate": {"successes": 0},
        "action_mask_violations": 0,
        "control_command_clamp_count": 0,
        "policy_decision_request_count": 100,
        "policy_decision_consumed_count": 90,
        "policy_decision_discarded_count": 10,
        "accepted_control_decision_count": 90,
        "policy_decision_duplicate_request_count": 0,
        "policy_decision_missing_action_count": 0,
        "stage5_diagnostic_gate": {"passed": True},
    }


def test_split_supervision_contract_is_decision_complete() -> None:
    contract = load_contract()

    assert contract["supervision_contract_id"] == (
        "goalkeeper-control-v2-split-supervision-v1"
    )
    assert contract["observation_size"] == 35
    assert contract["continuous_actions"] == 4
    assert contract["discrete_branches"] == [2]
    assert contract["split_seed"] == 20260801
    assert contract["split"] == {
        "train_per_arena": 1000,
        "validation_per_arena": 125,
        "test_per_arena": 125,
    }
    assert contract["timing_model"]["observation_indices"] == [29, 31, 32]


def test_demo_realigns_action_to_preceding_observation_and_terminal_action() -> None:
    pairs = [
        _pair(
            _info(0.1, can_commit=True),
            continuous=(0.0, 0.0, 0.0, 0.0),
            commit=0,
        ),
        _pair(
            _info(0.2, can_commit=False),
            continuous=(-0.8, -0.7, 0.6, 1.0),
            commit=1,
        ),
        _pair(
            _info(0.0, done=True, can_commit=False),
            continuous=(-0.8, -0.7, 0.6, 1.0),
            commit=0,
        ),
    ]

    episodes = realign_demo_pairs(pairs, arena_id=3)

    assert len(episodes) == 1
    episode = episodes[0]
    assert episode.key == (3, 1)
    np.testing.assert_allclose(episode.observations[:, 0], [0.1, 0.2])
    np.testing.assert_array_equal(episode.commit_actions, [1, 0])
    assert episode.commit_index == 0
    assert bool(episode.commit_allowed[0]) is True


def test_episode_split_is_exact_reproducible_and_has_no_leakage() -> None:
    contract = load_contract()
    episodes = [
        _episode(arena_id, ordinal)
        for arena_id in range(16)
        for ordinal in range(1, 1251)
    ]

    first = split_episode_keys(episodes, contract)
    second = split_episode_keys(episodes, contract)

    assert first == second
    assert len(first) == 20_000
    for arena_id in range(16):
        counts = {
            split: sum(
                key[0] == arena_id and value == split
                for key, value in first.items()
            )
            for split in ("train", "validation", "test")
        }
        assert counts == {"train": 1000, "validation": 125, "test": 125}


def test_balanced_timing_rows_use_same_arena_fallback_for_first_commit() -> None:
    values = {
        "episode_offsets": np.asarray([0, 1, 3], dtype=np.int64),
        "episode_arena_ids": np.asarray([0, 0], dtype=np.int16),
        "episode_ordinals": np.asarray([1, 2], dtype=np.int16),
        "teacher_commit_indices": np.asarray([0, 1], dtype=np.int16),
        "commit_allowed": np.asarray([True, True, True]),
        "commit_actions": np.asarray([1, 0, 1], dtype=np.int8),
    }

    indexes, stats = balanced_timing_rows(values, seed=1)

    np.testing.assert_array_equal(values["commit_actions"][indexes], [1, 1, 0, 0])
    assert stats == {
        "commit_rows": 2,
        "wait_rows": 2,
        "same_episode_wait_rows": 1,
        "same_arena_fallback_wait_rows": 1,
    }


def test_timing_sequence_metrics_use_full_unbalanced_episodes() -> None:
    values = {
        "episode_offsets": np.asarray([0, 3, 6], dtype=np.int64),
        "teacher_commit_indices": np.asarray([1, 1], dtype=np.int16),
        "commit_allowed": np.asarray([True, True, False, True, True, False]),
    }
    probabilities = np.asarray([0.1, 0.9, 0.9, 0.8, 0.2, 0.9])

    metrics = timing_sequence_metrics(values, probabilities, threshold=0.5)

    assert metrics["commit_coverage"] == 1.0
    assert metrics["within_one_decision_rate"] == 1.0
    assert metrics["premature_rate"] == 0.0
    assert metrics["late_rate"] == 0.0
    assert metrics["raw_masked_positive_rows"] == 2
    assert metrics["masked_predictions"] == 0
    assert metrics["repeated_commits"] == 0


def test_models_have_fixed_shapes_ranges_and_timing_features() -> None:
    observations = torch.zeros((5, 35), dtype=torch.float32)
    interception = InterceptionModel()
    timing = CommitTimingModel()

    continuous = interception(observations)
    logits = timing(observations)

    assert continuous.shape == (5, 4)
    assert bool(torch.all(torch.abs(continuous) <= 1.0))
    assert logits.shape == (5, 1)
    assert timing.feature_indices.tolist() == [29, 31, 32]


def test_supervised_models_export_with_onnx_runtime_parity(tmp_path: Path) -> None:
    interception_error = _export_onnx(
        InterceptionModel(),
        tmp_path / "interception.onnx",
        "continuous_actions",
    )
    timing_error = _export_onnx(
        CommitTimingModel(),
        tmp_path / "timing.onnx",
        "commit_logit",
    )

    assert interception_error < 1e-5
    assert timing_error < 1e-5


def test_offline_gate_reports_the_exact_failed_component() -> None:
    thresholds = load_contract()["offline_gates"]
    interception = {
        "move_mae": 0.05,
        "aim_x_mae": 0.05,
        "aim_y_mae": 0.05,
        "physical_aim_error_m": 0.1,
        "reach_mae": 0.02,
        "finite": True,
        "bounded": True,
    }
    timing = {
        "commit_coverage": 0.0,
        "within_one_decision_rate": 0.0,
        "premature_rate": 0.0,
        "late_rate": 0.0,
        "masked_predictions": 0,
        "repeated_commits": 0,
    }

    gate = offline_gate(interception, timing, thresholds)

    assert gate["passed"] is False
    assert gate["failed_checks"] == ["commit_coverage", "commit_timing"]


class _Session:
    def __init__(self, output: np.ndarray) -> None:
        self.output = output

    def run(self, names: list[str], feed: dict[str, np.ndarray]) -> list[np.ndarray]:
        return [np.repeat(self.output, len(feed["obs_0"]), axis=0)]


def test_split_policy_masks_and_latches_one_commit_per_agent() -> None:
    policy = object.__new__(SplitSupervisedPolicy)
    policy._interception_session = _Session(
        np.asarray([[0.1, -0.2, 0.3, 1.0]], dtype=np.float32)
    )
    policy._teacher_timing = False
    policy._timing_session = _Session(np.asarray([[10.0]], dtype=np.float32))
    policy._commit_threshold = 0.5
    policy._committed_agent_ids = set()
    observations = np.zeros((2, 35), dtype=np.float32)
    agent_ids = np.asarray([10, 11], dtype=np.int64)
    masks = np.asarray([[False, False], [False, True]])

    continuous, first = policy.hybrid_actions(observations, masks, agent_ids)
    _, second = policy.hybrid_actions(observations, masks, agent_ids)
    policy.reset_agents(np.asarray([10]))
    _, third = policy.hybrid_actions(observations, masks, agent_ids)

    assert continuous.shape == (2, 4)
    np.testing.assert_array_equal(first, [[1], [0]])
    np.testing.assert_array_equal(second, [[0], [0]])
    np.testing.assert_array_equal(third, [[1], [0]])


def test_stage5_split_evidence_gates_are_staged() -> None:
    contract = load_contract()
    teacher = _policy_report(
        "reactive_reach_v1",
        attempts=400,
        save=0.55,
        glove=0.7,
        high=0.65,
    )
    interception = _policy_report(
        "interception_teacher_timing:seed-001",
        attempts=400,
        save=0.5,
        glove=0.65,
        high=0.6,
    )
    combined = _policy_report(
        "split_supervised:seed-001",
        attempts=400,
        save=0.4,
        glove=0.45,
        high=0.35,
        commit=0.9,
    )

    smoke = smoke_unity_gate(
        {
            "run_id": "smoke",
            "policies": [
                {**interception, "attempts": 64},
                {**combined, "attempts": 64},
            ],
        }
    )
    interception_gate = interception_unity_gate(
        {"run_id": "interception", "policies": [teacher, interception]},
        contract,
    )
    combined_gate = combined_unity_gate(
        {"run_id": "combined", "policies": [combined]},
        contract,
    )

    assert smoke["passed"] is True
    assert interception_gate["passed"] is True
    assert combined_gate["passed"] is True


def test_handoff_has_hard_stops_and_never_launches_ppo() -> None:
    script = (
        ROOT / "scripts" / "run_stage5_split_supervision_handoff.sh"
    ).read_text(encoding="utf-8")

    assert "--require-stage offline" in script
    assert "--require-stage smoke" in script
    assert "--require-stage interception" in script
    assert "--require-stage combined" in script
    assert ".venv/bin/mlagents-learn" not in script
    assert "configs/training" not in script
    assert "PPO was not started" in script
