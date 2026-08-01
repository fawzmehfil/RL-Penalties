from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


DEFAULT_CONTRACT = Path(
    "configs/supervision/goalkeeper-control-v2-split-supervision-v2.json"
)
DEFAULT_REPORT = Path("docs/stage5-split-supervision-report.json")
DEFAULT_SUMMARY = Path("docs/stage5-training-summary.json")


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def _rate(policy: dict[str, Any], name: str) -> float:
    value = policy.get(name, 0.0)
    if isinstance(value, dict):
        return float(value.get("value", 0.0))
    return float(value)


def _mean(policy: dict[str, Any], name: str) -> float:
    value = policy.get(name, {})
    if isinstance(value, dict):
        return float(value.get("mean", 0.0))
    return float(value)


def _high_save_rate(policy: dict[str, Any]) -> float:
    high = policy.get("by_height_band", {}).get("high", {})
    return _rate(high, "save_rate")


def _policy(report: dict[str, Any], prefix: str) -> dict[str, Any]:
    matches = [
        policy
        for policy in report.get("policies", [])
        if str(policy.get("policy", "")).startswith(prefix)
    ]
    if len(matches) != 1:
        raise ValueError(f"expected one {prefix!r} policy, found {len(matches)}")
    return matches[0]


def _attempt_snapshot(report: dict[str, Any]) -> dict[str, Any]:
    return {
        "attempt_id": report.get("attempt_id"),
        "supervision_contract_id": report.get("supervision_contract_id"),
        "status": report.get("status"),
        "offline": report.get("offline"),
        "smoke_unity_gate": report.get("smoke_unity_gate"),
        "interception_unity_gate": report.get("interception_unity_gate"),
        "combined_unity_gate": report.get("combined_unity_gate"),
        "diagnosis": report.get("diagnosis"),
        "promotion": report.get("promotion"),
    }


def _report_for_attempt(
    report: dict[str, Any],
    contract: dict[str, Any],
    model_manifest: dict[str, Any],
) -> dict[str, Any]:
    attempt_id = (
        f"{contract['supervision_contract_id']}-"
        f"seed-{int(model_manifest['training_seed']):03d}"
    )
    if (
        report.get("supervision_contract_id") == contract["supervision_contract_id"]
        and report.get("attempt_id") == attempt_id
    ):
        return report
    history = list(report.get("attempt_history", []))
    if report.get("status"):
        history.append(_attempt_snapshot(report))
    return {
        "schema_version": 2,
        "stage": 5.6,
        "attempt_id": attempt_id,
        "status": "offline-gate-pending",
        "supervision_contract_id": contract["supervision_contract_id"],
        "source_demonstration_contract_id": contract[
            "source_demonstration_contract_id"
        ],
        "behavior_name": contract["behavior_name"],
        "observation_spec_id": contract["observation_spec_id"],
        "action_spec_id": contract["action_spec_id"],
        "offline": None,
        "smoke_unity_gate": None,
        "interception_unity_gate": None,
        "combined_unity_gate": None,
        "attempt_history": history,
        "implementation_verification": report.get(
            "implementation_verification",
            {},
        ),
        "promotion": {
            "authorized": False,
            "next_step": "evaluate the phase-aware offline gate",
        },
    }


def _lifecycle_and_safety(policy: dict[str, Any]) -> dict[str, bool]:
    requests = int(policy.get("policy_decision_request_count", -1))
    consumed = int(policy.get("policy_decision_consumed_count", -1))
    discarded = int(policy.get("policy_decision_discarded_count", -1))
    accepted = int(policy.get("accepted_control_decision_count", -1))
    return {
        "complete": bool(policy.get("complete", False)),
        "invalids": int(policy.get("invalid_rate", {}).get("successes", -1)) == 0,
        "timeouts": int(policy.get("timeout_rate", {}).get("successes", -1)) == 0,
        "action_masks": int(policy.get("action_mask_violations", -1)) == 0,
        "command_clamps": int(policy.get("control_command_clamp_count", -1)) == 0,
        "duplicates": int(policy.get("policy_decision_duplicate_request_count", -1))
        == 0,
        "missing_actions": int(policy.get("policy_decision_missing_action_count", -1))
        == 0,
        "requests_balanced": requests == consumed + discarded,
        "commands_balanced": consumed == accepted,
    }


def interception_unity_gate(
    evaluation: dict[str, Any],
    contract: dict[str, Any],
) -> dict[str, Any]:
    teacher = _policy(evaluation, "reactive_reach_v1")
    learned = _policy(evaluation, "interception_teacher_timing:")
    fraction = float(contract["unity_gates"]["interception_teacher_fraction"])
    teacher_metrics = {
        "save_rate": _rate(teacher, "save_rate"),
        "glove_contact_rate": _rate(teacher, "glove_contact_rate"),
        "high_shot_save_rate": _high_save_rate(teacher),
    }
    learned_metrics = {
        "save_rate": _rate(learned, "save_rate"),
        "glove_contact_rate": _rate(learned, "glove_contact_rate"),
        "high_shot_save_rate": _high_save_rate(learned),
        "first_commit_aim_error_m": _mean(
            learned,
            "first_commit_aim_error_m",
        ),
        "peak_reach_extension": _mean(
            learned,
            "goalkeeper_peak_reach_extension",
        ),
    }
    checks = {
        "attempts": int(learned.get("attempts", 0)) == 400,
        "teacher_attempts": int(teacher.get("attempts", 0)) == 400,
        "teacher_behavioral_gate": bool(
            teacher.get("stage5_diagnostic_gate", {}).get("passed", False)
        ),
        "save_rate": learned_metrics["save_rate"]
        >= fraction * teacher_metrics["save_rate"],
        "glove_contact_rate": learned_metrics["glove_contact_rate"]
        >= fraction * teacher_metrics["glove_contact_rate"],
        "high_shot_save_rate": learned_metrics["high_shot_save_rate"]
        >= fraction * teacher_metrics["high_shot_save_rate"],
        "aim_error": learned_metrics["first_commit_aim_error_m"]
        <= float(contract["unity_gates"]["interception_maximum_aim_error_m"]),
        "peak_reach": learned_metrics["peak_reach_extension"]
        >= float(contract["unity_gates"]["interception_minimum_peak_reach"]),
        **_lifecycle_and_safety(learned),
    }
    return {
        "passed": all(checks.values()),
        "evaluation_run_id": evaluation.get("run_id"),
        "teacher_fraction_required": fraction,
        "teacher": teacher_metrics,
        "interception_model": learned_metrics,
        "checks": checks,
        "failed_checks": [name for name, passed in checks.items() if not passed],
    }


def smoke_unity_gate(evaluation: dict[str, Any]) -> dict[str, Any]:
    policies = (
        _policy(evaluation, "interception_teacher_timing:"),
        _policy(evaluation, "split_supervised:"),
    )
    checks: dict[str, bool] = {}
    for policy in policies:
        label = str(policy["policy"]).split(":", maxsplit=1)[0]
        checks[f"{label}_attempts"] = int(policy.get("attempts", 0)) == 64
        for name, passed in _lifecycle_and_safety(policy).items():
            checks[f"{label}_{name}"] = passed
    return {
        "passed": all(checks.values()),
        "evaluation_run_id": evaluation.get("run_id"),
        "checks": checks,
        "failed_checks": [name for name, passed in checks.items() if not passed],
    }


def combined_unity_gate(
    evaluation: dict[str, Any],
    contract: dict[str, Any],
) -> dict[str, Any]:
    policy = _policy(evaluation, "split_supervised:")
    settings = contract["unity_gates"]
    metrics = {
        "attempts": int(policy.get("attempts", 0)),
        "commit_rate": _rate(policy, "commit_rate"),
        "save_rate": _rate(policy, "save_rate"),
        "glove_contact_rate": _rate(policy, "glove_contact_rate"),
        "glove_save_rate": _rate(policy, "glove_save_rate"),
        "high_shot_save_rate": _high_save_rate(policy),
        "first_commit_aim_error_m": _mean(
            policy,
            "first_commit_aim_error_m",
        ),
        "peak_reach_extension": _mean(
            policy,
            "goalkeeper_peak_reach_extension",
        ),
    }
    existing_gate = policy.get("stage5_diagnostic_gate", {})
    checks = {
        "attempts": metrics["attempts"] == 400,
        "commit_rate": metrics["commit_rate"]
        >= float(settings["combined_minimum_commit_rate"]),
        "save_rate": metrics["save_rate"]
        >= float(settings["combined_minimum_save_rate"]),
        "glove_contact_rate": metrics["glove_contact_rate"]
        >= float(settings["combined_minimum_glove_contact_rate"]),
        "glove_save_rate": metrics["glove_save_rate"]
        >= float(settings["combined_minimum_glove_save_rate"]),
        "high_shot_save_rate": metrics["high_shot_save_rate"]
        >= float(settings["combined_minimum_high_shot_save_rate"]),
        "aim_error": metrics["first_commit_aim_error_m"]
        <= float(settings["combined_maximum_aim_error_m"]),
        "peak_reach": metrics["peak_reach_extension"]
        >= float(settings["combined_minimum_peak_reach"]),
        "complete_behavioral_gate": bool(existing_gate.get("passed", False)),
        **_lifecycle_and_safety(policy),
    }
    return {
        "passed": all(checks.values()),
        "evaluation_run_id": evaluation.get("run_id"),
        "policy": policy.get("policy"),
        "metrics": metrics,
        "complete_behavioral_gate": existing_gate,
        "checks": checks,
        "failed_checks": [name for name, passed in checks.items() if not passed],
    }


def record_split_evidence(
    model_manifest_path: Path,
    *,
    contract_path: Path = DEFAULT_CONTRACT,
    report_path: Path = DEFAULT_REPORT,
    summary_path: Path = DEFAULT_SUMMARY,
    smoke_report_path: Path | None = None,
    interception_report_path: Path | None = None,
    combined_report_path: Path | None = None,
) -> dict[str, Any]:
    contract = load_json(contract_path)
    model_manifest = load_json(model_manifest_path)
    for key in (
        "supervision_contract_id",
        "behavior_name",
        "observation_spec_id",
        "action_spec_id",
    ):
        if model_manifest.get(key) != contract.get(key):
            raise ValueError(
                f"model manifest {key} does not match the evidence contract"
            )
    report = _report_for_attempt(
        load_json(report_path),
        contract,
        model_manifest,
    )
    offline = {
        "status": model_manifest.get("status"),
        "training_seed": model_manifest.get("training_seed"),
        "dataset_manifest_sha256": model_manifest.get("dataset_manifest_sha256"),
        "split_assignment_sha256": model_manifest.get("split_assignment_sha256"),
        "commit_threshold": model_manifest.get("commit_threshold"),
        "models": model_manifest.get("models"),
        "training": model_manifest.get("training"),
        "evaluation": model_manifest.get("offline_evaluation"),
    }
    report["offline"] = offline
    if smoke_report_path is not None:
        report["smoke_unity_gate"] = smoke_unity_gate(load_json(smoke_report_path))
    if interception_report_path is not None:
        report["interception_unity_gate"] = interception_unity_gate(
            load_json(interception_report_path),
            contract,
        )
    if combined_report_path is not None:
        report["combined_unity_gate"] = combined_unity_gate(
            load_json(combined_report_path),
            contract,
        )

    offline_passed = bool(
        model_manifest.get("offline_evaluation", {})
        .get("gate", {})
        .get("passed", False)
    )
    smoke = report.get("smoke_unity_gate")
    interception = report.get("interception_unity_gate")
    combined = report.get("combined_unity_gate")
    if not offline_passed:
        status = "offline-gate-failed"
        next_step = "inspect the failed offline component; PPO is not authorized"
    elif smoke is None:
        status = "offline-passed-smoke-gate-pending"
        next_step = "run the 16 x 4 split-policy Unity smoke gate"
    elif not smoke.get("passed", False):
        status = "smoke-unity-gate-failed"
        next_step = "inspect evaluator integration; PPO is not authorized"
    elif interception is None:
        status = "offline-passed-interception-gate-pending"
        next_step = "run the interception model with teacher timing on 400 shots"
    elif not interception.get("passed", False):
        status = "interception-unity-gate-failed"
        next_step = (
            "inspect interception or evaluator integration; PPO is not authorized"
        )
    elif combined is None:
        status = "interception-passed-combined-gate-pending"
        next_step = "run the combined split policy on 400 fixed shots"
    elif not combined.get("passed", False):
        status = "combined-unity-gate-failed"
        next_step = "inspect timing integration; PPO is not authorized"
    else:
        status = "supervised-gate-passed"
        next_step = "plan Stage 5.6B native inference and short PPO refinement"
    report["status"] = status
    report["promotion"] = {
        "authorized": status == "supervised-gate-passed",
        "next_step": next_step,
    }
    report_path.write_text(
        json.dumps(report, indent=2) + "\n",
        encoding="utf-8",
    )

    summary = load_json(summary_path)
    current_summary = summary.get("split_supervision")
    if (
        current_summary
        and current_summary.get("contract_id") != contract["supervision_contract_id"]
    ):
        history = list(summary.get("split_supervision_history", []))
        history.append(current_summary)
        summary["split_supervision_history"] = history
    summary["status"] = status
    summary["split_supervision"] = {
        "contract_id": contract["supervision_contract_id"],
        "source_demonstration_contract_id": contract[
            "source_demonstration_contract_id"
        ],
        "interception_model_id": contract["interception_model"]["model_id"],
        "timing_model_id": contract["timing_model"]["model_id"],
        "status": status,
        "training_seed": model_manifest.get("training_seed"),
        "offline_gate_passed": offline_passed,
        "smoke_unity_gate_passed": (
            None if smoke is None else bool(smoke.get("passed"))
        ),
        "interception_unity_gate_passed": (
            None if interception is None else bool(interception.get("passed"))
        ),
        "combined_unity_gate_passed": (
            None if combined is None else bool(combined.get("passed"))
        ),
        "promotion_authorized": status == "supervised-gate-passed",
    }
    summary_path.write_text(
        json.dumps(summary, indent=2) + "\n",
        encoding="utf-8",
    )
    return report


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Record compact Stage 5.6 split-supervision evidence."
    )
    parser.add_argument("--model-manifest", type=Path, required=True)
    parser.add_argument("--contract", type=Path, default=DEFAULT_CONTRACT)
    parser.add_argument("--report", type=Path, default=DEFAULT_REPORT)
    parser.add_argument("--summary", type=Path, default=DEFAULT_SUMMARY)
    parser.add_argument("--smoke-report", type=Path)
    parser.add_argument("--interception-report", type=Path)
    parser.add_argument("--combined-report", type=Path)
    parser.add_argument(
        "--require-stage",
        choices=("offline", "smoke", "interception", "combined"),
        default="offline",
    )
    return parser


def main() -> int:
    args = build_parser().parse_args()
    try:
        report = record_split_evidence(
            args.model_manifest,
            contract_path=args.contract,
            report_path=args.report,
            summary_path=args.summary,
            smoke_report_path=args.smoke_report,
            interception_report_path=args.interception_report,
            combined_report_path=args.combined_report,
        )
    except Exception as exception:
        print(f"FAIL: {exception}")
        return 1
    required = {
        "offline": bool(
            report.get("offline", {})
            .get("evaluation", {})
            .get("gate", {})
            .get("passed", False)
        ),
        "smoke": bool((report.get("smoke_unity_gate") or {}).get("passed", False)),
        "interception": bool(
            (report.get("interception_unity_gate") or {}).get(
                "passed",
                False,
            )
        ),
        "combined": bool(
            (report.get("combined_unity_gate") or {}).get("passed", False)
        ),
    }
    print(json.dumps(report, indent=2))
    return 0 if required[args.require_stage] else 1


if __name__ == "__main__":
    raise SystemExit(main())
