from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def rate_value(policy: dict[str, Any], key: str) -> float:
    value = policy.get(key, 0.0)
    if isinstance(value, dict):
        value = value.get("value", value.get("mean", 0.0))
    return float(value or 0.0)


def compact_policy(policy: dict[str, Any]) -> dict[str, Any]:
    high = (
        policy.get("by_height_band", {})
        .get("high", {})
        .get("save_rate", {})
        .get("value", 0.0)
    )
    requests = int(policy.get("policy_decision_request_count", 0))
    consumed = int(policy.get("policy_decision_consumed_count", 0))
    discarded = int(policy.get("policy_decision_discarded_count", 0))
    accepted = int(policy.get("accepted_control_decision_count", 0))
    return {
        "policy": policy["policy"],
        "policy_type": policy["policy_type"],
        "attempts": int(policy["attempts"]),
        "save_rate": rate_value(policy, "save_rate"),
        "commit_rate": rate_value(policy, "commit_rate"),
        "glove_contact_rate": rate_value(
            policy,
            "glove_contact_rate",
        ),
        "glove_save_rate": rate_value(policy, "glove_save_rate"),
        "high_shot_save_rate": float(high),
        "first_commit_aim_error_m": float(
            policy.get("first_commit_aim_error_m", {}).get(
                "mean",
                0.0,
            )
        ),
        "peak_reach_extension_mean": float(
            policy.get("goalkeeper_peak_reach_extension", {}).get(
                "mean",
                0.0,
            )
        ),
        "invalids": int(
            policy.get("invalid_rate", {}).get("successes", 0)
        ),
        "timeouts": int(
            policy.get("timeout_rate", {}).get("successes", 0)
        ),
        "action_mask_violations": int(
            policy.get("action_mask_violations", 0)
        ),
        "control_command_clamps": int(
            policy.get("control_command_clamp_count", 0)
        ),
        "decision_lifecycle": {
            "requests": requests,
            "consumed": consumed,
            "discarded": discarded,
            "accepted": accepted,
            "duplicate_requests": int(
                policy.get(
                    "policy_decision_duplicate_request_count",
                    0,
                )
            ),
            "missing_actions": int(
                policy.get(
                    "policy_decision_missing_action_count",
                    0,
                )
            ),
            "balanced": (
                requests == consumed + discarded and
                consumed == accepted
            ),
        },
        "gate": policy.get("stage5_diagnostic_gate"),
    }


def record_stage5_imitation_evidence(
    *,
    manifest_path: Path,
    evaluation_report_path: Path,
    implementation_report_path: Path,
    training_summary_path: Path,
    training_run_id: str,
    seed: int,
) -> dict[str, Any]:
    manifest = load_json(manifest_path)
    evaluation = load_json(evaluation_report_path)
    implementation = load_json(implementation_report_path)
    summary = load_json(training_summary_path)
    if manifest.get("status") != "passed":
        raise ValueError("Demonstration manifest did not pass validation.")

    selection = evaluation.get("stage5_diagnostic_selection")
    if not selection:
        raise ValueError(
            "Evaluation report has no Stage 5 diagnostic selection."
        )
    selected_policy = next(
        (
            policy
            for policy in evaluation["policies"]
            if policy["policy"] == selection["selected_policy"]
        ),
        None,
    )
    if selected_policy is None:
        raise ValueError(
            "Selected checkpoint is missing from policy reports."
        )

    compact_policies = [
        compact_policy(policy)
        for policy in evaluation["policies"]
    ]
    selected_compact = next(
        policy
        for policy in compact_policies
        if policy["policy"] == selection["selected_policy"]
    )
    passed = bool(selection["passed"])
    manifest_hash = hashlib.sha256(
        manifest_path.read_bytes()
    ).hexdigest()
    implementation["status"] = (
        "diagnostic-completed-passed"
        if passed
        else "diagnostic-completed-failed"
    )
    implementation["demonstration"].update(
        {
            "status": "passed",
            "manifest_sha256": manifest_hash,
            "terminal_episodes": int(
                manifest["terminal_episodes"]
            ),
            "decision_steps": int(manifest["decision_steps"]),
            "commit_actions": int(manifest["commit_actions"]),
            "continuous_action_coverage":
                manifest["continuous_action_coverage"],
            "teacher_quality": manifest["teacher_quality"],
            "demonstration_files":
                manifest["demonstration_files"],
        }
    )
    implementation["diagnostic"].update(
        {
            "status": (
                "passed" if passed else "failed"
            ),
            "training_run_id": training_run_id,
            "seed": seed,
            "evaluation_run_id": evaluation["run_id"],
            "evaluation_attempts": int(
                evaluation["total_attempts"]
            ),
            "selected_checkpoint":
                selection["selected_policy"],
            "selection": selection,
            "policies": compact_policies,
        }
    )

    run_entry = {
        "run_id": training_run_id,
        "seed": seed,
        "steps": 500000,
        "training_reward_spec_id":
            "goalkeeper-control-result-v2",
        "demonstration_contract_id":
            "goalkeeper-control-v2-reactive-demo-v1",
        "evaluation_run_id": evaluation["run_id"],
        "evaluation_attempts": int(evaluation["total_attempts"]),
        "best_checkpoint": selection["selected_policy"],
        "save_rate": selected_compact["save_rate"],
        "commit_rate": selected_compact["commit_rate"],
        "glove_contact_rate":
            selected_compact["glove_contact_rate"],
        "glove_save_rate": selected_compact["glove_save_rate"],
        "high_shot_save_rate":
            selected_compact["high_shot_save_rate"],
        "peak_reach_extension_mean":
            selected_compact["peak_reach_extension_mean"],
        "invalids": selected_compact["invalids"],
        "timeouts": selected_compact["timeouts"],
        "action_mask_violations":
            selected_compact["action_mask_violations"],
        "passed": passed,
    }
    summary["training_runs"] = [
        run
        for run in summary["training_runs"]
        if run.get("run_id") != training_run_id
    ]
    summary["training_runs"].append(run_entry)
    summary["status"] = (
        "imitation-diagnostic-passed"
        if passed
        else "imitation-diagnostic-failed"
    )
    summary["imitation_bootstrap"]["status"] = (
        "diagnostic-completed-passed"
        if passed
        else "diagnostic-completed-failed"
    )
    summary["selected_checkpoint"] = (
        selection["selected_policy"] if passed else None
    )

    write_json(implementation_report_path, implementation)
    write_json(training_summary_path, summary)
    return {
        "passed": passed,
        "selected_policy": selection["selected_policy"],
        "implementation_report": str(implementation_report_path),
        "training_summary": str(training_summary_path),
    }


def write_json(path: Path, value: dict[str, Any]) -> None:
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(
        json.dumps(value, indent=2) + "\n",
        encoding="utf-8",
    )
    temporary.replace(path)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Record compact Stage 5.5 diagnostic evidence."
    )
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument(
        "--evaluation-report",
        type=Path,
        required=True,
    )
    parser.add_argument(
        "--implementation-report",
        type=Path,
        default=Path(
            "docs/stage5-imitation-bootstrap-report.json"
        ),
    )
    parser.add_argument(
        "--training-summary",
        type=Path,
        default=Path("docs/stage5-training-summary.json"),
    )
    parser.add_argument("--training-run-id", required=True)
    parser.add_argument("--seed", type=int, required=True)
    return parser


def main() -> int:
    args = build_parser().parse_args()
    outcome = record_stage5_imitation_evidence(
        manifest_path=args.manifest.expanduser().resolve(),
        evaluation_report_path=(
            args.evaluation_report.expanduser().resolve()
        ),
        implementation_report_path=(
            args.implementation_report.expanduser().resolve()
        ),
        training_summary_path=(
            args.training_summary.expanduser().resolve()
        ),
        training_run_id=args.training_run_id,
        seed=args.seed,
    )
    print(json.dumps(outcome, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
