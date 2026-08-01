import hashlib
import json
from pathlib import Path

from penalty_shootout.training.stage5_native_inference import build_evidence


def _rate(value: float) -> dict:
    return {"value": value, "successes": int(value > 0), "total": 400}


def _policy(name: str, *, native: bool) -> dict:
    policy = {
        "policy": name,
        "complete": True,
        "attempts": 400,
        "episode_key_digest": "fixed-episode-keys",
        "save_rate": _rate(0.5725),
        "commit_rate": _rate(1.0),
        "glove_contact_rate": _rate(0.725),
        "by_height_band": {"high": {"save_rate": _rate(0.6739)}},
        "invalid_rate": {"successes": 0},
        "timeout_rate": {"successes": 0},
        "action_mask_violations": 0,
        "control_command_clamp_count": 0,
        "policy_action_override_count": 0,
        "policy_decision_request_count": 1000,
        "policy_decision_consumed_count": 900,
        "policy_decision_discarded_count": 100,
        "accepted_control_decision_count": 900,
        "policy_decision_duplicate_request_count": 0,
        "policy_decision_missing_action_count": 0,
        "native_inference_evaluation_count": 900 if native else 0,
        "native_inference_maximum_action_error": 0.00001 if native else 0.0,
        "native_inference_commit_mismatch_count": 0,
        "native_inference_invalid_output_count": 0,
    }
    return policy


def test_native_evidence_requires_exact_parity_and_safety(tmp_path: Path) -> None:
    project = tmp_path / "project"
    model_dir = project / "unity" / "Assets" / "Models"
    model_dir.mkdir(parents=True)
    interception = model_dir / "interception.onnx"
    timing = model_dir / "timing.onnx"
    interception.write_bytes(b"interception")
    timing.write_bytes(b"timing")
    digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
    contract = {
        "inference_contract_id": "goalkeeper-control-v2-split-native-v1",
        "source_supervision_contract_id": (
            "goalkeeper-control-v2-split-supervision-v2"
        ),
        "models": {
            "interception": {
                "asset": "Assets/Models/interception.onnx",
                "sha256": digest(interception),
            },
            "timing": {
                "asset": "Assets/Models/timing.onnx",
                "sha256": digest(timing),
            },
        },
        "promotion_gates": {
            "maximum_native_python_action_error": 0.0001,
            "maximum_save_rate_delta": 0.02,
            "maximum_glove_contact_rate_delta": 0.02,
            "maximum_high_shot_save_rate_delta": 0.03,
            "minimum_commit_rate": 0.85,
            "minimum_save_rate": 0.35,
            "minimum_glove_contact_rate": 0.4,
            "minimum_high_shot_save_rate": 0.3,
        },
        "ppo_refinement": {"maximum_initial_budget_steps": 250000},
    }
    source = {
        "status": "supervised-gate-passed",
        "supervision_contract_id": (
            "goalkeeper-control-v2-split-supervision-v2"
        ),
    }
    evaluation = {
        "run_id": "native-400",
        "policies": [
            _policy("split_supervised:seed-001", native=False),
            _policy("native_split_v1:seed-001", native=True),
        ],
    }

    evidence = build_evidence(evaluation, contract, source, project)

    assert evidence["status"] == "native-gate-passed"
    assert all(evidence["checks"].values())
    assert evidence["ppo_refinement"]["authorized"] is True


def test_native_evidence_rejects_commit_mismatch(tmp_path: Path) -> None:
    project = tmp_path / "project"
    model_dir = project / "unity" / "Assets" / "Models"
    model_dir.mkdir(parents=True)
    model = model_dir / "model.onnx"
    model.write_bytes(b"model")
    model_hash = hashlib.sha256(model.read_bytes()).hexdigest()
    contract = {
        "inference_contract_id": "native",
        "source_supervision_contract_id": "supervision",
        "models": {
            "interception": {
                "asset": "Assets/Models/model.onnx",
                "sha256": model_hash,
            }
        },
        "promotion_gates": {
            "maximum_native_python_action_error": 0.0001,
            "maximum_save_rate_delta": 0.02,
            "maximum_glove_contact_rate_delta": 0.02,
            "maximum_high_shot_save_rate_delta": 0.03,
            "minimum_commit_rate": 0.85,
            "minimum_save_rate": 0.35,
            "minimum_glove_contact_rate": 0.4,
            "minimum_high_shot_save_rate": 0.3,
        },
        "ppo_refinement": {"maximum_initial_budget_steps": 250000},
    }
    native = _policy("native_split_v1:seed-001", native=True)
    native["native_inference_commit_mismatch_count"] = 1

    evidence = build_evidence(
        {
            "run_id": "native-400",
            "policies": [
                _policy("split_supervised:seed-001", native=False),
                native,
            ],
        },
        contract,
        {
            "status": "supervised-gate-passed",
            "supervision_contract_id": "supervision",
        },
        project,
    )

    assert evidence["status"] == "native-gate-failed"
    assert evidence["failed_checks"] == ["commit_action_parity"]
