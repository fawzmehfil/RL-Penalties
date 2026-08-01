from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any


DEFAULT_CONTRACT = Path(
    "configs/inference/goalkeeper-control-v2-split-native-v1.json"
)
DEFAULT_SOURCE_REPORT = Path("docs/stage5-split-supervision-report.json")
DEFAULT_OUTPUT = Path("docs/stage5-native-inference-report.json")
DEFAULT_SUMMARY = Path("docs/stage5-training-summary.json")


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def rate(policy: dict[str, Any], name: str) -> float:
    value = policy.get(name, {})
    return float(value.get("value", 0.0)) if isinstance(value, dict) else float(value)


def high_save_rate(policy: dict[str, Any]) -> float:
    return rate(policy.get("by_height_band", {}).get("high", {}), "save_rate")


def policy_with_prefix(
    report: dict[str, Any],
    prefix: str,
) -> dict[str, Any]:
    matches = [
        policy
        for policy in report.get("policies", [])
        if str(policy.get("policy", "")).startswith(prefix)
    ]
    if len(matches) != 1:
        raise ValueError(f"expected one {prefix!r} policy, found {len(matches)}")
    return matches[0]


def validate_packaged_models(
    contract: dict[str, Any],
    project_root: Path,
) -> dict[str, str]:
    hashes: dict[str, str] = {}
    for name, model in contract["models"].items():
        asset = project_root / "unity" / str(model["asset"])
        if not asset.is_file():
            raise FileNotFoundError(asset)
        digest = sha256(asset)
        if digest != model["sha256"]:
            raise ValueError(f"packaged {name} model hash changed: {asset}")
        hashes[name] = digest
    return hashes


def build_evidence(
    evaluation: dict[str, Any],
    contract: dict[str, Any],
    source_report: dict[str, Any],
    project_root: Path,
) -> dict[str, Any]:
    if source_report.get("status") != "supervised-gate-passed":
        raise ValueError("Stage 5.6A source report has not passed")
    if source_report.get("supervision_contract_id") != contract.get(
        "source_supervision_contract_id"
    ):
        raise ValueError("native contract does not match Stage 5.6A evidence")

    packaged_hashes = validate_packaged_models(contract, project_root)
    python_policy = policy_with_prefix(evaluation, "split_supervised:")
    native_policy = policy_with_prefix(evaluation, "native_split_v1:")
    gates = contract["promotion_gates"]
    python_metrics = {
        "save_rate": rate(python_policy, "save_rate"),
        "glove_contact_rate": rate(python_policy, "glove_contact_rate"),
        "high_shot_save_rate": high_save_rate(python_policy),
        "commit_rate": rate(python_policy, "commit_rate"),
    }
    native_metrics = {
        "save_rate": rate(native_policy, "save_rate"),
        "glove_contact_rate": rate(native_policy, "glove_contact_rate"),
        "high_shot_save_rate": high_save_rate(native_policy),
        "commit_rate": rate(native_policy, "commit_rate"),
        "maximum_action_error": float(
            native_policy.get("native_inference_maximum_action_error", 1.0)
        ),
        "commit_mismatches": int(
            native_policy.get("native_inference_commit_mismatch_count", -1)
        ),
        "invalid_outputs": int(
            native_policy.get("native_inference_invalid_output_count", -1)
        ),
        "native_evaluations": int(
            native_policy.get("native_inference_evaluation_count", 0)
        ),
    }
    lifecycle_safe = all(
        (
            int(native_policy.get("action_mask_violations", -1)) == 0,
            int(native_policy.get("control_command_clamp_count", -1)) == 0,
            int(native_policy.get("policy_decision_duplicate_request_count", -1))
            == 0,
            int(native_policy.get("policy_decision_missing_action_count", -1)) == 0,
            int(native_policy.get("policy_action_override_count", -1)) == 0,
            int(native_policy.get("invalid_rate", {}).get("successes", -1)) == 0,
            int(native_policy.get("timeout_rate", {}).get("successes", -1)) == 0,
            int(native_policy.get("policy_decision_request_count", -1))
            == int(native_policy.get("policy_decision_consumed_count", -2))
            + int(native_policy.get("policy_decision_discarded_count", -2)),
            int(native_policy.get("policy_decision_consumed_count", -1))
            == int(native_policy.get("accepted_control_decision_count", -2)),
        )
    )
    checks = {
        "attempts_complete": bool(native_policy.get("complete", False)),
        "same_fixed_episode_keys": native_policy.get("episode_key_digest")
        == python_policy.get("episode_key_digest"),
        "native_inference_executed": native_metrics["native_evaluations"] > 0,
        "continuous_action_parity": native_metrics["maximum_action_error"]
        <= float(gates["maximum_native_python_action_error"]),
        "commit_action_parity": native_metrics["commit_mismatches"] == 0,
        "valid_native_outputs": native_metrics["invalid_outputs"] == 0,
        "save_rate_parity": abs(
            native_metrics["save_rate"] - python_metrics["save_rate"]
        )
        <= float(gates["maximum_save_rate_delta"]),
        "glove_contact_parity": abs(
            native_metrics["glove_contact_rate"]
            - python_metrics["glove_contact_rate"]
        )
        <= float(gates["maximum_glove_contact_rate_delta"]),
        "high_shot_parity": abs(
            native_metrics["high_shot_save_rate"]
            - python_metrics["high_shot_save_rate"]
        )
        <= float(gates["maximum_high_shot_save_rate_delta"]),
        "minimum_commit_rate": native_metrics["commit_rate"]
        >= float(gates["minimum_commit_rate"]),
        "minimum_save_rate": native_metrics["save_rate"]
        >= float(gates["minimum_save_rate"]),
        "minimum_glove_contact_rate": native_metrics["glove_contact_rate"]
        >= float(gates["minimum_glove_contact_rate"]),
        "minimum_high_shot_save_rate": native_metrics["high_shot_save_rate"]
        >= float(gates["minimum_high_shot_save_rate"]),
        "lifecycle_and_safety": lifecycle_safe,
    }
    passed = all(checks.values())
    return {
        "schema_version": 1,
        "stage": "5.6B",
        "status": "native-gate-passed" if passed else "native-gate-failed",
        "inference_contract_id": contract["inference_contract_id"],
        "source_supervision_contract_id": contract[
            "source_supervision_contract_id"
        ],
        "evaluation_run_id": evaluation.get("run_id"),
        "attempts_per_policy": int(native_policy.get("attempts", 0)),
        "packaged_model_hashes": packaged_hashes,
        "python_reference": python_metrics,
        "native_unity": native_metrics,
        "checks": checks,
        "failed_checks": [name for name, passed in checks.items() if not passed],
        "ppo_refinement": {
            "authorized": passed,
            "automatic_launch": False,
            "maximum_initial_budget_steps": int(
                contract["ppo_refinement"]["maximum_initial_budget_steps"]
            ),
            "decision": (
                "A separate bounded refinement design may proceed."
                if passed
                else "Do not start PPO; repair native parity first."
            ),
        },
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Record Stage 5.6B native Unity inference evidence."
    )
    parser.add_argument("--evaluation", type=Path, required=True)
    parser.add_argument("--contract", type=Path, default=DEFAULT_CONTRACT)
    parser.add_argument("--source-report", type=Path, default=DEFAULT_SOURCE_REPORT)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--summary", type=Path, default=DEFAULT_SUMMARY)
    parser.add_argument("--project-root", type=Path, default=Path.cwd())
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    evidence = build_evidence(
        load_json(args.evaluation),
        load_json(args.contract),
        load_json(args.source_report),
        args.project_root.resolve(),
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    if args.summary.is_file():
        summary = load_json(args.summary)
        summary["status"] = evidence["status"]
        summary["native_inference"] = {
            "contract_id": evidence["inference_contract_id"],
            "status": evidence["status"],
            "evaluation_run_id": evidence["evaluation_run_id"],
            "attempts_per_policy": evidence["attempts_per_policy"],
            "native_metrics": evidence["native_unity"],
            "checks": evidence["checks"],
            "ppo_refinement": evidence["ppo_refinement"],
        }
        if evidence["status"] == "native-gate-passed":
            summary["selected_controller"] = {
                "policy_id": "goalkeeper-control-v2-split-native-v1",
                "interception_model_id": "goalkeeper-interception-v2",
                "timing_model_id": "goalkeeper-commit-timing-v1",
                "deployment": "native-unity-inference",
                "save_rate_400": evidence["native_unity"]["save_rate"],
                "glove_contact_rate_400": evidence["native_unity"][
                    "glove_contact_rate"
                ],
                "high_shot_save_rate_400": evidence["native_unity"][
                    "high_shot_save_rate"
                ],
            }
        args.summary.write_text(
            json.dumps(summary, indent=2) + "\n",
            encoding="utf-8",
        )
    print(json.dumps(evidence, indent=2))
    return 0 if evidence["status"] == "native-gate-passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
