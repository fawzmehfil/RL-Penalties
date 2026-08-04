"""Evidence-first Stage 6 gameplay-readiness diagnosis."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

from .goalkeeper import (
    TARGET_X_EXTENT,
    TARGET_Y_MAX,
    TARGET_Y_MIN,
    motor_timing_estimate_v1,
)


SAVE_OUTCOMES = {"Saved", "BlockedThenOut"}


def _bool(value: Any) -> bool:
    return str(value).strip().lower() in {"true", "1", "yes"}


def _float(row: dict[str, str], key: str, default: float = 0.0) -> float:
    try:
        value = float(row.get(key, default))
    except (TypeError, ValueError):
        return default
    return value if math.isfinite(value) else default


def read_episodes(path: Path) -> list[dict[str, str]]:
    if not path.exists():
        raise FileNotFoundError(path)
    with path.open(newline="", encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    if not rows:
        raise ValueError(f"No episodes in {path}")
    return rows


def episode_key(row: dict[str, str]) -> tuple[int, int, str]:
    return int(row["arena_id"]), int(row["attempt_id"]), row["seed"]


def key_digest(rows: Iterable[dict[str, str]]) -> str:
    payload = "\n".join(
        f"{arena}:{attempt}:{seed}"
        for arena, attempt, seed in sorted(episode_key(row) for row in rows)
    )
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def wilson(successes: int, total: int) -> dict[str, Any]:
    if total <= 0:
        return {"successes": successes, "total": total, "value": 0.0,
                "lower_95": 0.0, "upper_95": 0.0}
    z = 1.959963984540054
    value = successes / total
    denominator = 1.0 + z * z / total
    center = (value + z * z / (2.0 * total)) / denominator
    margin = z * math.sqrt(
        value * (1.0 - value) / total + z * z / (4.0 * total * total)
    ) / denominator
    return {
        "successes": successes,
        "total": total,
        "value": value,
        "lower_95": max(0.0, center - margin),
        "upper_95": min(1.0, center + margin),
    }


def policy_rows(
    rows: Iterable[dict[str, str]],
    policy_prefix: str,
    *,
    maximum_attempt_id: int | None = None,
) -> list[dict[str, str]]:
    selected = [
        row for row in rows
        if row.get("policy", "").startswith(policy_prefix)
        and (maximum_attempt_id is None or int(row["attempt_id"]) <= maximum_attempt_id)
    ]
    selected.sort(key=episode_key)
    return selected


def expected_on_target(rows: Iterable[dict[str, str]]) -> list[dict[str, str]]:
    return [row for row in rows if _bool(row.get("expected_on_target"))]


def summarize_policy(rows: list[dict[str, str]]) -> dict[str, Any]:
    primary = expected_on_target(rows)
    saves = sum(row["outcome"] in SAVE_OUTCOMES for row in primary)
    gloves = sum(_bool(row.get("glove_contact")) for row in primary)
    contact_goals = sum(
        row["outcome"] == "Goal" and _bool(row.get("goalkeeper_contact"))
        for row in primary
    )
    saturations = sum(_float(row, "root_target_saturation_distance") > 1e-5 for row in primary)
    return {
        "policy": rows[0]["policy"] if rows else "missing",
        "attempts": len(rows),
        "expected_on_target_attempts": len(primary),
        "episode_key_digest": key_digest(rows),
        "save_rate": wilson(saves, len(primary)),
        "glove_contact_rate": wilson(gloves, len(primary)),
        "contact_then_goal_rate": wilson(contact_goals, len(primary)),
        "root_saturation_rate": wilson(saturations, len(primary)),
        "invalids": sum(row["outcome"] == "Invalid" for row in rows),
        "timeouts": sum(row["outcome"] == "Timeout" for row in rows),
        "mask_violations": sum(int(float(row.get("action_mask_violations", 0))) for row in rows),
    }


def paired_outcomes(
    left: list[dict[str, str]],
    right: list[dict[str, str]],
) -> dict[str, Any]:
    left_by_key = {episode_key(row): row for row in expected_on_target(left)}
    right_by_key = {episode_key(row): row for row in expected_on_target(right)}
    if set(left_by_key) != set(right_by_key):
        raise ValueError("Paired policies did not receive the same expected-on-target shots")
    both_save = left_only = right_only = both_fail = 0
    for key in sorted(left_by_key):
        left_save = left_by_key[key]["outcome"] in SAVE_OUTCOMES
        right_save = right_by_key[key]["outcome"] in SAVE_OUTCOMES
        if left_save and right_save:
            both_save += 1
        elif left_save:
            left_only += 1
        elif right_save:
            right_only += 1
        else:
            both_fail += 1
    total = len(left_by_key)
    return {
        "attempts": total,
        "both_save": both_save,
        "left_only_save": left_only,
        "right_only_save": right_only,
        "both_fail": both_fail,
        "left_save_rate": (both_save + left_only) / total if total else 0.0,
        "right_save_rate": (both_save + right_only) / total if total else 0.0,
        "right_minus_left": (right_only - left_only) / total if total else 0.0,
        "union_save_rate": (both_save + left_only + right_only) / total if total else 0.0,
    }


def capability_envelope(rows: list[dict[str, str]], delay_seconds: float) -> dict[str, Any]:
    primary = expected_on_target(rows)
    reachable = saturated = late = 0
    by_band: Counter[str] = Counter()
    for row in primary:
        x = _float(row, "predicted_unopposed_crossing_local_x")
        y = _float(row, "predicted_unopposed_crossing_local_y")
        aim_x = max(-1.0, min(1.0, x / TARGET_X_EXTENT))
        normalized_y = (y - TARGET_Y_MIN) / (TARGET_Y_MAX - TARGET_Y_MIN)
        aim_y = max(-1.0, min(1.0, normalized_y * 2.0 - 1.0))
        estimate = motor_timing_estimate_v1(aim_x, aim_y, 0.0)
        flight_time = _float(row, "sampled_shot_flight_time", _float(row, "ball_flight_time"))
        is_saturated = estimate.root_target_saturation_m > 1e-5
        is_late = flight_time < delay_seconds + estimate.full_reach_time
        saturated += is_saturated
        late += is_late
        if not is_saturated and not is_late:
            reachable += 1
        horizontal = "central" if abs(x) < 2.0 else "inner" if abs(x) < 3.0 else "outer"
        by_band[f"{horizontal}_total"] += 1
        by_band[f"{horizontal}_reachable"] += int(not is_saturated and not is_late)
    return {
        "label": "offline_motor_capability_estimate_not_a_policy_score",
        "attempts": len(primary),
        "reachable_rate": wilson(reachable, len(primary)),
        "root_saturated_rate": wilson(saturated, len(primary)),
        "insufficient_full_reach_time_rate": wilson(late, len(primary)),
        "by_horizontal_band": {
            band: wilson(by_band[f"{band}_reachable"], by_band[f"{band}_total"])
            for band in ("central", "inner", "outer")
        },
    }


def contact_diagnosis(rows: list[dict[str, str]]) -> dict[str, Any]:
    primary = expected_on_target(rows)
    contacts = [row for row in primary if _bool(row.get("goalkeeper_contact"))]
    low = [
        row for row in primary
        if _float(row, "predicted_unopposed_crossing_local_y") < 0.85
    ]
    low_contacts = [row for row in low if _bool(row.get("goalkeeper_contact"))]
    def save_rate(items: list[dict[str, str]]) -> dict[str, Any]:
        return wilson(sum(row["outcome"] in SAVE_OUTCOMES for row in items), len(items))
    contact_z = sorted(_float(row, "first_goalkeeper_contact_point_local_z") for row in contacts)
    median_z = contact_z[len(contact_z) // 2] if contact_z else 0.0
    forward = [
        row for row in contacts
        if _float(row, "first_goalkeeper_contact_point_local_z") >= median_z
    ]
    rear = [
        row for row in contacts
        if _float(row, "first_goalkeeper_contact_point_local_z") < median_z
    ]
    return {
        "contact_attempts": len(contacts),
        "contact_save_rate": save_rate(contacts),
        "contact_then_goal_rate": wilson(
            sum(row["outcome"] == "Goal" for row in contacts), len(contacts)
        ),
        "low_shot_save_rate": save_rate(low),
        "low_contact_save_rate": save_rate(low_contacts),
        "median_contact_z_m": median_z,
        "rear_contact_save_rate": save_rate(rear),
        "forward_contact_save_rate": save_rate(forward),
        "candidate_status": "not_promoted_requires_controlled_runtime_ab_test",
    }


def low_shot_save_rate(rows: list[dict[str, str]]) -> dict[str, Any]:
    low = [
        row for row in expected_on_target(rows)
        if _float(row, "predicted_unopposed_crossing_local_y") < 0.85
    ]
    return wilson(
        sum(row["outcome"] in SAVE_OUTCOMES for row in low),
        len(low),
    )


def replay_manifest(rows: list[dict[str, str]], master_seed: int) -> dict[str, Any]:
    failures = [row for row in expected_on_target(rows) if row["outcome"] == "Goal"]
    failures.sort(
        key=lambda row: (
            -int(_float(row, "root_target_saturation_distance") > 1e-5),
            -_float(row, "launch_speed_mps"),
            _float(row, "predicted_unopposed_crossing_local_y"),
        )
    )
    entries = []
    for row in failures[:20]:
        arena_id, attempt_id, seed = episode_key(row)
        entries.append({
            "arena_id": arena_id,
            "attempt_id": attempt_id,
            "scenario_seed": seed,
            "shot_style": row.get("shot_style"),
            "launch_speed_mps": _float(row, "launch_speed_mps"),
            "predicted_crossing": {
                "x": _float(row, "predicted_unopposed_crossing_local_x"),
                "y": _float(row, "predicted_unopposed_crossing_local_y"),
            },
            "root_saturation_m": _float(row, "root_target_saturation_distance"),
            "replay_arguments": [
                f"--stage6-replay-master-seed={master_seed}",
                f"--stage6-replay-arena-id={arena_id}",
                f"--stage6-replay-attempt-id={attempt_id}",
            ],
        })
    return {"schema_version": 1, "entries": entries}


def build_report(
    audit_config: dict[str, Any],
    delayed_rows: list[dict[str, str]],
    zero_delay_rows: list[dict[str, str]],
    baseline_rows: list[dict[str, str]],
    contact_candidate_rows: list[dict[str, str]] | None = None,
    canonical_baseline_rows: list[dict[str, str]] | None = None,
    canonical_candidate_rows: list[dict[str, str]] | None = None,
    high_baseline_rows: list[dict[str, str]] | None = None,
    high_candidate_rows: list[dict[str, str]] | None = None,
) -> dict[str, Any]:
    maximum_attempt = int(audit_config["attempts_per_arena"])
    native = policy_rows(delayed_rows, "native_split_v1")
    curve = policy_rows(delayed_rows, "reactive_curve_v1")
    motor = policy_rows(delayed_rows, "reactive_motor_v1")
    native_zero = policy_rows(zero_delay_rows, "native_split_v1")
    stand = policy_rows(baseline_rows, "stand_center_v1", maximum_attempt_id=maximum_attempt)
    random = policy_rows(baseline_rows, "random_hybrid_v1", maximum_attempt_id=maximum_attempt)
    required = {"native delayed": native, "curve": curve, "motor": motor,
                "native zero delay": native_zero, "stand": stand, "random": random}
    candidate = []
    if contact_candidate_rows is not None:
        candidate = policy_rows(contact_candidate_rows, "native_split_v1")
        required["contact candidate"] = candidate
    missing = [name for name, rows in required.items() if len(rows) != 400]
    if missing:
        raise ValueError(f"Expected 400 episodes for: {', '.join(missing)}")
    digest = key_digest(native)
    if any(key_digest(rows) != digest for rows in required.values()):
        raise ValueError("Audit policies did not receive identical episode keys")

    summaries = {name: summarize_policy(rows) for name, rows in required.items()}
    pair_native_curve = paired_outcomes(native, curve)
    pair_native_motor = paired_outcomes(native, motor)
    pair_delay = paired_outcomes(native, native_zero)
    pair_contact = paired_outcomes(native, candidate) if candidate else None
    thresholds = audit_config["decision_thresholds"]
    recommendations: list[dict[str, str]] = []
    if pair_native_motor["right_minus_left"] >= thresholds[
        "minimum_teacher_gain_for_interception_training"
    ]:
        recommendations.append({
            "priority": "model_a_interception",
            "verdict": "eligible_for_teacher_guided_fine_tuning",
            "reason": "The visible-state motor-aware teacher beats native by at least five points.",
        })
    if pair_delay["right_minus_left"] >= thresholds[
        "minimum_zero_delay_gain_for_timing_training"
    ]:
        recommendations.append({
            "priority": "model_b_timing",
            "verdict": "latency_decomposition_required_before_retraining",
            "reason": (
                "Removing the 40 ms delay improves native saves by at least "
                "three points, but this ablation also changes Model A inputs."
            ),
        })
    envelope = capability_envelope(native, delay_seconds=0.04)
    if envelope["root_saturated_rate"]["value"] >= 0.10:
        recommendations.append({
            "priority": "motor_capability",
            "verdict": "validate_fair_anticipation_before_training",
            "reason": (
                "At least ten percent of expected-on-target shots saturate "
                "the frozen root target."
            ),
        })
    contacts = contact_diagnosis(native)
    if candidate:
        baseline_low = low_shot_save_rate(native)
        candidate_low = low_shot_save_rate(candidate)
        overall_gain = pair_contact["right_minus_left"]
        low_gain = candidate_low["value"] - baseline_low["value"]
        gameplay_passed = (
            low_gain >= thresholds["minimum_contact_candidate_low_gain"] and
            overall_gain >= thresholds["minimum_contact_candidate_overall_gain"]
        )
        contacts["runtime_candidate"] = {
            "contract_id": "stage6-contact-candidate-v1",
            "paired": pair_contact,
            "baseline_low_save_rate": baseline_low,
            "candidate_low_save_rate": candidate_low,
            "low_save_rate_gain": low_gain,
            "overall_save_rate_gain": overall_gain,
            "passes_gameplay_gain_gate": gameplay_passed,
            "promotion_status": "not_promoted_regression_or_realism_review_pending",
        }
        regression_inputs = (
            canonical_baseline_rows,
            canonical_candidate_rows,
            high_baseline_rows,
            high_candidate_rows,
        )
        if all(rows is not None for rows in regression_inputs):
            canonical_baseline = policy_rows(canonical_baseline_rows or [], "native_split_v1")
            canonical_candidate = policy_rows(canonical_candidate_rows or [], "native_split_v1")
            high_baseline = policy_rows(high_baseline_rows or [], "native_split_v1")
            high_candidate = policy_rows(high_candidate_rows or [], "native_split_v1")
            for label, rows in {
                "canonical baseline": canonical_baseline,
                "canonical candidate": canonical_candidate,
                "high baseline": high_baseline,
                "high candidate": high_candidate,
            }.items():
                if len(rows) != 400:
                    raise ValueError(f"Expected 400 episodes for {label}")
            canonical_pair = paired_outcomes(canonical_baseline, canonical_candidate)
            high_pair = paired_outcomes(high_baseline, high_candidate)
            maximum_regression = thresholds["maximum_canonical_regression"]
            regressions_passed = (
                canonical_pair["right_minus_left"] >= -maximum_regression and
                high_pair["right_minus_left"] >= -maximum_regression
            )
            contacts["runtime_candidate"]["regressions"] = {
                "canonical": canonical_pair,
                "high_shot": high_pair,
                "passed": regressions_passed,
            }
            contacts["runtime_candidate"]["promotion_status"] = (
                "eligible_for_physical_realism_review_not_promoted"
                if gameplay_passed and regressions_passed
                else "rejected_by_gameplay_or_regression_gate"
            )
        recommendations = [
            item for item in recommendations
            if item["priority"] != "contact_physics"
        ]
        status = contacts["runtime_candidate"]["promotion_status"]
        if status == "eligible_for_physical_realism_review_not_promoted":
            recommendations.append({
                "priority": "contact_physics",
                "verdict": status,
                "reason": (
                    "The candidate passed gameplay and regression gates; "
                    "visual deflection realism remains unproven."
                ),
            })
        elif contacts["runtime_candidate"]["passes_gameplay_gain_gate"]:
            recommendations.append({
                "priority": "contact_physics",
                "verdict": "eligible_for_regression_not_promoted",
                "reason": (
                    "The gameplay gain gate passed, but regression evidence "
                    "is absent or incomplete."
                ),
            })
        else:
            recommendations.append({
                "priority": "contact_physics",
                "verdict": "candidate_rejected_no_promotion",
                "reason": (
                    "The audit candidate did not improve both low and "
                    "overall save rates enough."
                ),
            })
    if not candidate and contacts["contact_then_goal_rate"]["value"] >= 0.15:
        recommendations.append({
            "priority": "contact_physics",
            "verdict": "run_controlled_runtime_ab_before_promotion",
            "reason": "At least fifteen percent of keeper contacts still become goals.",
        })
    if not recommendations:
        recommendations.append({
            "priority": "retain_controller",
            "verdict": "no_training_supported_by_this_audit",
            "reason": "No preregistered intervention threshold was crossed.",
        })
    safety_passed = all(
        summary["invalids"] == 0 and summary["timeouts"] == 0 and
        summary["mask_violations"] == 0
        for summary in summaries.values()
    )
    return {
        "schema_version": 1,
        "audit_id": audit_config["audit_id"],
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "status": "passed" if safety_passed else "failed",
        "scope": "diagnostic_only_no_training_or_motor_promotion",
        "episode_key_digest": digest,
        "policies": summaries,
        "paired": {
            "native_vs_reactive_curve": pair_native_curve,
            "native_vs_reactive_motor": pair_native_motor,
            "native_delay2_vs_native_delay0": pair_delay,
            **({"native_vs_contact_candidate": pair_contact} if pair_contact else {}),
        },
        "offline_capability_oracle": envelope,
        "contact_diagnosis": contacts,
        "recommendations": recommendations,
        "safety_invariants_passed": safety_passed,
    }


def write_summary(path: Path, report: dict[str, Any]) -> None:
    lines = [
        "# Stage 6 gameplay-readiness audit",
        "",
        f"Status: **{report['status']}**",
        "",
        "| Policy | Save rate | Glove contact | Contact then goal |",
        "|---|---:|---:|---:|",
    ]
    for name, policy in report["policies"].items():
        lines.append(
            f"| {name} | {policy['save_rate']['value']:.2%} | "
            f"{policy['glove_contact_rate']['value']:.2%} | "
            f"{policy['contact_then_goal_rate']['value']:.2%} |"
        )
    lines.extend(["", "## Paired diagnosis", ""])
    for name, paired in report["paired"].items():
        lines.append(
            f"- `{name}`: right-minus-left "
            f"{paired['right_minus_left']:+.2%}; "
            f"union {paired['union_save_rate']:.2%}"
        )
    lines.extend(["", "## Decision", ""])
    for item in report["recommendations"]:
        lines.append(f"- **{item['priority']}**: {item['verdict']}. {item['reason']}")
    lines.extend(["", "No training or motor/contact change was started by this audit.", ""])
    path.write_text("\n".join(lines), encoding="utf-8")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--delayed-csv", type=Path, required=True)
    parser.add_argument("--zero-delay-csv", type=Path, required=True)
    parser.add_argument("--baseline-csv", type=Path, required=True)
    parser.add_argument("--contact-candidate-csv", type=Path)
    parser.add_argument("--canonical-baseline-csv", type=Path)
    parser.add_argument("--canonical-candidate-csv", type=Path)
    parser.add_argument("--high-baseline-csv", type=Path)
    parser.add_argument("--high-candidate-csv", type=Path)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--canonical-report", type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    config = json.loads(args.config.read_text(encoding="utf-8"))
    report = build_report(
        config,
        read_episodes(args.delayed_csv),
        read_episodes(args.zero_delay_csv),
        read_episodes(args.baseline_csv),
        read_episodes(args.contact_candidate_csv)
        if args.contact_candidate_csv is not None
        else None,
        read_episodes(args.canonical_baseline_csv)
        if args.canonical_baseline_csv is not None else None,
        read_episodes(args.canonical_candidate_csv)
        if args.canonical_candidate_csv is not None else None,
        read_episodes(args.high_baseline_csv)
        if args.high_baseline_csv is not None else None,
        read_episodes(args.high_candidate_csv)
        if args.high_candidate_csv is not None else None,
    )
    args.output_dir.mkdir(parents=True, exist_ok=True)
    (args.output_dir / "report.json").write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8"
    )
    write_summary(args.output_dir / "summary.md", report)
    replay = replay_manifest(
        policy_rows(read_episodes(args.delayed_csv), "native_split_v1"),
        int(config["master_seed"]),
    )
    (args.output_dir / "failure-replays.json").write_text(
        json.dumps(replay, indent=2) + "\n", encoding="utf-8"
    )
    if args.canonical_report is not None:
        args.canonical_report.parent.mkdir(parents=True, exist_ok=True)
        args.canonical_report.write_text(
            json.dumps(report, indent=2) + "\n", encoding="utf-8"
        )
    print((args.output_dir / "summary.md").read_text(encoding="utf-8"))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
