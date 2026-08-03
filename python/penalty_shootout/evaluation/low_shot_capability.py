"""Diagnose the frozen goalkeeper's lower-third save-rate deficit."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


DEFAULT_CONTRACT = Path("configs/audits/stage6-low-shot-capability-v1.json")


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def rate_value(policy: dict[str, Any], key: str) -> float:
    value = policy.get(key, {})
    if not isinstance(value, dict):
        return float(value)
    return float(value.get("value", 0.0))


def numeric_mean(policy: dict[str, Any], key: str) -> float:
    value = policy.get(key, {})
    return float(value.get("mean", 0.0)) if isinstance(value, dict) else 0.0


def require_policy(
    policies: list[dict[str, Any]],
    prefix: str,
) -> dict[str, Any]:
    matches = [
        policy
        for policy in policies
        if str(policy.get("policy", "")).startswith(prefix)
    ]
    if len(matches) != 1:
        raise ValueError(f"expected one policy starting with {prefix!r}")
    return matches[0]


def policy_is_safe(policy: dict[str, Any]) -> bool:
    return all(
        (
            int(policy.get("invalid_rate", {}).get("successes", -1)) == 0,
            int(policy.get("timeout_rate", {}).get("successes", -1)) == 0,
            int(policy.get("action_mask_violations", -1)) == 0,
            int(policy.get("control_command_clamp_count", -1)) == 0,
            int(policy.get("policy_action_override_count", -1)) == 0,
            int(policy.get("policy_decision_duplicate_request_count", -1)) == 0,
            int(policy.get("policy_decision_missing_action_count", -1)) == 0,
            int(policy.get("policy_decision_request_count", -1))
            == int(policy.get("policy_decision_consumed_count", -2))
            + int(policy.get("policy_decision_discarded_count", -2)),
            int(policy.get("policy_decision_consumed_count", -1))
            == int(policy.get("accepted_control_decision_count", -2)),
        )
    )


def compact_policy(policy: dict[str, Any]) -> dict[str, Any]:
    return {
        "policy": policy["policy"],
        "attempts": int(policy.get("attempts", 0)),
        "save_rate": rate_value(policy, "save_rate"),
        "glove_contact_rate": rate_value(policy, "glove_contact_rate"),
        "glove_save_rate": rate_value(policy, "glove_save_rate"),
        "arm_save_rate": rate_value(policy, "arm_save_rate"),
        "body_save_rate": rate_value(policy, "body_save_rate"),
        "commit_rate": rate_value(policy, "commit_rate"),
        "immediate_commit_rate": rate_value(policy, "immediate_commit_rate"),
        "premature_commit_rate": rate_value(policy, "premature_commit_rate"),
        "timely_commit_rate": rate_value(policy, "timely_commit_rate"),
        "mean_first_commit_aim_error_m": numeric_mean(
            policy,
            "first_commit_aim_error_m",
        ),
        "mean_goalkeeper_root_distance_m": numeric_mean(
            policy,
            "goalkeeper_root_distance_m",
        ),
        "mean_minimum_glove_ball_distance_m": numeric_mean(
            policy,
            "minimum_glove_ball_distance_m",
        ),
        "mean_committed_glove_forward_m": numeric_mean(
            policy,
            "committed_glove_forward_m",
        ),
        "mean_first_contact_ball_velocity_y_mps": numeric_mean(
            policy,
            "first_contact_ball_velocity_y_mps",
        ),
        "mean_first_contact_ball_velocity_z_mps": numeric_mean(
            policy,
            "first_contact_ball_velocity_z_mps",
        ),
        "mean_first_contact_root_velocity_y_mps": numeric_mean(
            policy,
            "first_contact_root_velocity_y_mps",
        ),
        "mean_first_contact_impulse": numeric_mean(
            policy,
            "first_contact_impulse_magnitude",
        ),
        "safe_lifecycle": policy_is_safe(policy),
    }


def build_audit(
    evaluation: dict[str, Any],
    contract: dict[str, Any],
    reference: dict[str, Any],
    baseline: dict[str, Any] | None = None,
) -> dict[str, Any]:
    policies = evaluation.get("policies", [])
    expected_attempts = int(contract["expected_attempts_per_policy"])
    expected_teacher_names = {
        f"{contract['teacher_policy_prefix']}{float(horizon):.2f}"
        for horizon in contract["teacher_commit_horizons_s"]
    }
    teachers = [
        policy
        for policy in policies
        if policy.get("policy") in expected_teacher_names
    ]
    native = require_policy(policies, contract["native_policy_prefix"])
    digests = {
        str(policy.get("episode_key_digest", ""))
        for policy in policies
    }
    only_low_shots = all(
        set(policy.get("by_height_band", {})) == {"low"}
        for policy in policies
    )
    validity_checks = {
        "benchmark_id": evaluation.get("benchmark_id")
        == contract["benchmark_id"],
        "all_teacher_horizons_present": {
            str(policy.get("policy", "")) for policy in teachers
        }
        == expected_teacher_names,
        "attempts_complete": all(
            bool(policy.get("complete", False))
            and int(policy.get("attempts", 0)) == expected_attempts
            for policy in policies
        ),
        "same_fixed_episode_keys": len(digests) == 1 and "" not in digests,
        "lower_third_only": only_low_shots,
        "safety_and_lifecycle": all(policy_is_safe(policy) for policy in policies),
    }
    expected_forward = contract.get("expected_committed_glove_forward_m")
    if expected_forward is not None:
        validity_checks["committed_glove_forward_applied"] = all(
            abs(
                numeric_mean(policy, "committed_glove_forward_m")
                - float(expected_forward)
            )
            <= 1e-4
            for policy in policies
        )
    valid = all(validity_checks.values())

    teacher_metrics = [compact_policy(policy) for policy in teachers]
    teacher_metrics.sort(key=lambda item: item["policy"])
    best_teacher = max(
        teacher_metrics,
        key=lambda item: item["save_rate"],
    )
    native_metrics = compact_policy(native)
    teacher_rates = [item["save_rate"] for item in teacher_metrics]
    teacher_timing_spread = max(teacher_rates) - min(teacher_rates)

    if reference.get("stage5_freeze", {}).get("status") != "frozen":
        raise ValueError("Stage 5 reference evidence is not frozen")
    high_reference = float(
        reference["official_benchmark"]["by_height_band"]["high"]
        ["save_rate"]["value"]
    )
    native_gap_to_teacher = best_teacher["save_rate"] - native_metrics["save_rate"]
    low_gap_to_high = high_reference - best_teacher["save_rate"]
    maximum_native_gap = float(
        contract["maximum_native_gap_to_best_teacher"]
    )
    maximum_height_gap = float(
        contract["maximum_low_gap_to_frozen_high_reference"]
    )
    minimum_timing_spread = float(
        contract["minimum_material_timing_spread"]
    )

    if not valid:
        diagnosis = "invalid-audit"
        recommendation = "Repair the benchmark data or lifecycle errors and rerun."
    elif native_gap_to_teacher > maximum_native_gap:
        if teacher_timing_spread >= minimum_timing_spread:
            diagnosis = "learned-timing-gap"
            recommendation = (
                "A visible-state timing horizon materially improves low saves; "
                "repair timing before changing the motor."
            )
        else:
            diagnosis = "learned-interception-gap"
            recommendation = (
                "The scripted controller exceeds the native policy without a "
                "material timing effect; inspect learned movement and glove aim."
            )
    elif low_gap_to_high > maximum_height_gap:
        diagnosis = "shared-motor-or-interception-geometry-gap"
        recommendation = (
            "The native policy is near the best visible-state teacher but both "
            "remain materially below high-shot performance. Audit low dive body "
            "drop, glove path, ground clearance, and deflection geometry."
        )
    else:
        diagnosis = "no-material-low-shot-capability-gap"
        recommendation = (
            "The timing sweep closes the apparent height deficit; retain the "
            "motor and carry the measured timing into Stage 6."
        )

    correction_gate = None
    if baseline is not None:
        baseline_native = baseline["native_low_shot"]
        save_improvement = (
            native_metrics["save_rate"] - float(baseline_native["save_rate"])
        )
        glove_contact_regression = (
            float(baseline_native["glove_contact_rate"])
            - native_metrics["glove_contact_rate"]
        )
        minimum_improvement = float(
            contract["minimum_native_save_rate_improvement"]
        )
        maximum_glove_regression = float(
            contract["maximum_glove_contact_rate_regression"]
        )
        correction_gate = {
            "passed": valid
            and save_improvement >= minimum_improvement
            and glove_contact_regression <= maximum_glove_regression,
            "baseline_native_save_rate": float(
                baseline_native["save_rate"]
            ),
            "native_save_rate_improvement": save_improvement,
            "minimum_required_save_rate_improvement": minimum_improvement,
            "baseline_native_glove_contact_rate": float(
                baseline_native["glove_contact_rate"]
            ),
            "native_glove_contact_rate_regression": glove_contact_regression,
            "maximum_allowed_glove_contact_rate_regression":
                maximum_glove_regression,
            "high_shot_regression_check": "pending",
        }

    return {
        "schema_version": 1,
        "audit_id": contract["audit_id"],
        "status": "audit-complete" if valid else "audit-invalid",
        "evaluation_run_id": evaluation.get("run_id"),
        "benchmark_id": evaluation.get("benchmark_id"),
        "validity_checks": validity_checks,
        "diagnosis": diagnosis,
        "recommendation": recommendation,
        "frozen_high_shot_save_rate": high_reference,
        "native_low_shot": native_metrics,
        "best_teacher_low_shot": best_teacher,
        "teacher_timing_sweep": teacher_metrics,
        "teacher_timing_save_rate_spread": teacher_timing_spread,
        "native_gap_to_best_teacher": native_gap_to_teacher,
        "best_low_gap_to_frozen_high": low_gap_to_high,
        "forward_contact_correction_gate": correction_gate,
        "thresholds": {
            "maximum_native_gap_to_best_teacher": maximum_native_gap,
            "maximum_low_gap_to_frozen_high_reference": maximum_height_gap,
            "minimum_material_timing_spread": minimum_timing_spread,
        },
        "notes": [
            "All probe policies use visible ball and goalkeeper state only.",
            "The audit does not modify or retrain the frozen Stage 5 controller.",
            "A shared-gap diagnosis cannot by itself separate motor geometry "
            "from the teacher's interception command geometry.",
        ],
    }


def write_summary(path: Path, audit: dict[str, Any]) -> None:
    lines = [
        "# Stage 6 low-shot capability audit",
        "",
        f"Status: `{audit['status']}`",
        f"Diagnosis: `{audit['diagnosis']}`",
        "",
        "| Policy | Saves | Glove contact | Glove saves | Immediate | Premature | Root distance |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]
    policies = [
        *audit["teacher_timing_sweep"],
        audit["native_low_shot"],
    ]
    for policy in policies:
        lines.append(
            "| {policy} | {save:.2%} | {glove:.2%} | {glove_save:.2%} | "
            "{immediate:.2%} | {premature:.2%} | {root:.3f} m |".format(
                policy=policy["policy"],
                save=policy["save_rate"],
                glove=policy["glove_contact_rate"],
                glove_save=policy["glove_save_rate"],
                immediate=policy["immediate_commit_rate"],
                premature=policy["premature_commit_rate"],
                root=policy["mean_goalkeeper_root_distance_m"],
            )
        )
    lines.extend(
        [
            "",
            f"Frozen high-shot reference: {audit['frozen_high_shot_save_rate']:.2%}",
            f"Best scripted low-shot result: {audit['best_teacher_low_shot']['save_rate']:.2%}",
            f"Frozen native low-shot result: {audit['native_low_shot']['save_rate']:.2%}",
            "",
            audit["recommendation"],
        ]
    )
    correction = audit.get("forward_contact_correction_gate")
    if correction is not None:
        lines.extend(
            [
                "",
                "## Forward-contact correction gate",
                "",
                f"Status: `{'PASS' if correction['passed'] else 'FAIL'}`",
                "Native low-shot improvement: "
                f"{correction['native_save_rate_improvement']:+.2%} "
                f"(required {correction['minimum_required_save_rate_improvement']:.2%})",
                "Native glove-contact regression: "
                f"{correction['native_glove_contact_rate_regression']:+.2%} "
                f"(maximum {correction['maximum_allowed_glove_contact_rate_regression']:.2%})",
                "High-shot regression check: pending a passing low-shot gate.",
            ]
        )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evaluation", type=Path, required=True)
    parser.add_argument("--contract", type=Path, default=DEFAULT_CONTRACT)
    parser.add_argument("--reference", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--summary", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    contract = load_json(args.contract)
    reference_path = args.reference or Path(contract["reference_evidence"])
    audit = build_audit(
        load_json(args.evaluation),
        contract,
        load_json(reference_path),
        load_json(Path(contract["baseline_audit"]))
        if contract.get("baseline_audit")
        else None,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(audit, indent=2) + "\n", encoding="utf-8")
    write_summary(args.summary, audit)
    print(args.summary.read_text(encoding="utf-8"))
    return 0 if audit["status"] == "audit-complete" else 1


if __name__ == "__main__":
    raise SystemExit(main())
