"""Check that the Stage 6 forward-glove correction preserves high saves."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from penalty_shootout.evaluation.low_shot_capability import (
    compact_policy,
    numeric_mean,
    policy_is_safe,
)


DEFAULT_CONTRACT = Path(
    "configs/audits/stage6-high-shot-forward-contact-v1.json"
)


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


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


def build_audit(
    evaluation: dict[str, Any],
    contract: dict[str, Any],
    reference: dict[str, Any],
) -> dict[str, Any]:
    native = require_policy(
        evaluation.get("policies", []),
        contract["native_policy_prefix"],
    )
    expected_attempts = int(contract["expected_attempts"])
    expected_forward = float(contract["expected_committed_glove_forward_m"])
    high_bands = set(native.get("by_height_band", {}))
    validity_checks = {
        "benchmark_id": evaluation.get("benchmark_id")
        == contract["benchmark_id"],
        "attempts_complete": bool(native.get("complete", False))
        and int(native.get("attempts", 0)) == expected_attempts,
        "upper_third_only": high_bands == {"high"},
        "committed_glove_forward_applied": abs(
            numeric_mean(native, "committed_glove_forward_m")
            - expected_forward
        )
        <= 1e-4,
        "safety_and_lifecycle": policy_is_safe(native),
    }
    valid = all(validity_checks.values())

    if reference.get("stage5_freeze", {}).get("status") != "frozen":
        raise ValueError("Stage 5 reference evidence is not frozen")
    reference_high = float(
        reference["official_benchmark"]["by_height_band"]["high"]
        ["save_rate"]["value"]
    )
    native_metrics = compact_policy(native)
    measured_high = native_metrics["save_rate"]
    regression = reference_high - measured_high
    maximum_regression = float(contract["maximum_save_rate_regression"])
    passed = valid and regression <= maximum_regression

    return {
        "schema_version": 1,
        "audit_id": contract["audit_id"],
        "status": "pass" if passed else "fail",
        "evaluation_run_id": evaluation.get("run_id"),
        "benchmark_id": evaluation.get("benchmark_id"),
        "validity_checks": validity_checks,
        "native_high_shot": native_metrics,
        "frozen_high_shot_save_rate": reference_high,
        "measured_high_shot_save_rate": measured_high,
        "save_rate_regression": regression,
        "maximum_allowed_save_rate_regression": maximum_regression,
        "promotion_ready": passed,
        "recommendation": (
            "Promote the forward-glove correction and proceed with Stage 6."
            if passed
            else "Do not promote the correction; inspect the high-shot regression."
        ),
    }


def write_summary(path: Path, audit: dict[str, Any]) -> None:
    native = audit["native_high_shot"]
    lines = [
        "# Stage 6 high-shot forward-contact regression",
        "",
        f"Status: `{'PASS' if audit['promotion_ready'] else 'FAIL'}`",
        "",
        f"Attempts: {native['attempts']}",
        f"Frozen high-shot save rate: {audit['frozen_high_shot_save_rate']:.2%}",
        f"Corrected high-shot save rate: {audit['measured_high_shot_save_rate']:.2%}",
        f"Regression: {audit['save_rate_regression']:+.2%}",
        "Maximum permitted regression: "
        f"{audit['maximum_allowed_save_rate_regression']:.2%}",
        f"Glove contact: {native['glove_contact_rate']:.2%}",
        f"Glove saves: {native['glove_save_rate']:.2%}",
        f"Safety/lifecycle: `{'PASS' if native['safe_lifecycle'] else 'FAIL'}`",
        "",
        audit["recommendation"],
    ]
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
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(audit, indent=2) + "\n", encoding="utf-8")
    write_summary(args.summary, audit)
    print(args.summary.read_text(encoding="utf-8"))
    return 0 if audit["promotion_ready"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
