"""Calibration and promotion gates for keeper-glove-handling-v2."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Iterable

from penalty_shootout.evaluation.goalkeeper import rate


SAVE_OUTCOMES = {"Saved", "BlockedThenOut"}
PROFILE_IDS = {"conservative": 0, "balanced": 1, "permissive": 2}
SAFETY_FIELDS = (
    "action_mask_violations",
    "duplicate_terminal_events",
    "control_command_clamp_count",
    "policy_decision_duplicate_request_count",
    "policy_decision_missing_action_count",
    "native_inference_invalid_output_count",
    "native_inference_commit_mismatch_count",
)


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8") as handle:
        return list(csv.DictReader(handle))


def truth(value: Any) -> bool:
    return str(value).strip().lower() in {"1", "true", "yes"}


def number(row: dict[str, Any], key: str, default: float = 0.0) -> float:
    try:
        value = float(row.get(key, default))
    except (TypeError, ValueError):
        return default
    return value if math.isfinite(value) else default


def episode_key(row: dict[str, Any]) -> tuple[int, int, str]:
    return (
        int(row.get("arena_id", -1)),
        int(row.get("attempt_id", -1)),
        str(row.get("seed", "")),
    )


def key_digest(rows: Iterable[dict[str, Any]]) -> str:
    payload = sorted(episode_key(row) for row in rows)
    return hashlib.sha256(
        json.dumps(payload, separators=(",", ":")).encode("utf-8")
    ).hexdigest()


def file_hash(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def primary_rows(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return [row for row in rows if truth(row.get("expected_on_target", False))]


def classified_contacts(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return [
        row
        for row in primary_rows(rows)
        if truth(row.get("glove_contact", False))
    ]


def safety_failures(rows: list[dict[str, Any]]) -> dict[str, int]:
    failures = {
        field: sum(int(number(row, field)) for row in rows)
        for field in SAFETY_FIELDS
    }
    failures["invalids"] = sum(row.get("outcome") == "Invalid" for row in rows)
    failures["timeouts"] = sum(row.get("outcome") == "Timeout" for row in rows)
    failures["controlled_response_violations"] = sum(
        int(number(row, "glove_controlled_response_count")) > 1 for row in rows
    )
    failures["energy_cap_violations"] = sum(
        number(row, "glove_outgoing_energy_ratio") > 0.9501
        for row in rows
        if row.get("glove_handling_outcome")
        in {"Parry", "Punch", "WeakDeflection"}
    )
    return failures


def summarize(rows: list[dict[str, Any]]) -> dict[str, Any]:
    primary = primary_rows(rows)
    contacts = classified_contacts(rows)
    outcomes = Counter(str(row.get("glove_handling_outcome", "None")) for row in contacts)
    contact_goals = sum(
        row.get("outcome") == "Goal" and truth(row.get("goalkeeper_contact"))
        for row in primary
    )
    glove_contacts = sum(truth(row.get("glove_contact")) for row in primary)
    safety = safety_failures(rows)
    return {
        "attempts": len(rows),
        "primary_attempts": len(primary),
        "episode_key_digest": key_digest(rows),
        "save_rate": rate(
            sum(row.get("outcome") in SAVE_OUTCOMES for row in primary),
            len(primary),
        ),
        "contact_then_goal_rate": rate(contact_goals, len(primary)),
        "first_glove_contact_coverage": rate(glove_contacts, len(primary)),
        "classified_contact_attempts": len(contacts),
        "outcomes": dict(sorted(outcomes.items())),
        "catch_share": rate(outcomes["Catch"], len(contacts)),
        "punch_share": rate(outcomes["Punch"], len(contacts)),
        "catch_punch_share": rate(
            outcomes["Catch"] + outcomes["Punch"], len(contacts)
        ),
        "uncontrolled_share": rate(outcomes["Uncontrolled"], len(contacts)),
        "rejection_reasons": dict(
            sorted(
                Counter(
                    str(row.get("glove_handling_rejection_reason", "None"))
                    for row in contacts
                ).items()
            )
        ),
        "maximum_controlled_response_count": max(
            (int(number(row, "glove_controlled_response_count")) for row in rows),
            default=0,
        ),
        "safety_failures": safety,
        "safety_passed": not any(safety.values()),
    }


def profile_eligibility(
    baseline: dict[str, Any], candidate: dict[str, Any]
) -> dict[str, bool]:
    return {
        "catch_share": 0.01 <= candidate["catch_share"]["value"] <= 0.12,
        "punch_share": 0.01 <= candidate["punch_share"]["value"] <= 0.12,
        "combined_share": candidate["catch_punch_share"]["value"] <= 0.20,
        "uncontrolled_share": candidate["uncontrolled_share"]["value"] >= 0.15,
        "contact_coverage": abs(
            candidate["first_glove_contact_coverage"]["value"]
            - baseline["first_glove_contact_coverage"]["value"]
        )
        <= 0.005,
        "contact_then_goal": (
            candidate["contact_then_goal_rate"]["value"]
            <= baseline["contact_then_goal_rate"]["value"]
        ),
        "episode_keys": (
            candidate["episode_key_digest"] == baseline["episode_key_digest"]
        ),
        "safety": candidate["safety_passed"],
    }


def select_profile(
    baseline_rows: list[dict[str, Any]],
    candidates: dict[str, list[dict[str, Any]]],
) -> dict[str, Any]:
    baseline = summarize(baseline_rows)
    results: dict[str, Any] = {}
    eligible: list[tuple[tuple[float, float, float, int], str]] = []
    for name, rows in candidates.items():
        metrics = summarize(rows)
        checks = profile_eligibility(baseline, metrics)
        results[name] = {
            "metrics": metrics,
            "eligibility": checks,
            "eligible": all(checks.values()),
        }
        if all(checks.values()):
            score = (
                metrics["contact_then_goal_rate"]["value"],
                abs(metrics["catch_share"]["value"] - 0.06),
                abs(metrics["punch_share"]["value"] - 0.06),
                PROFILE_IDS[name],
            )
            eligible.append((score, name))
    eligible.sort()
    return {
        "baseline": baseline,
        "profiles": results,
        "selected_profile": eligible[0][1] if eligible else None,
        "passed": bool(eligible),
    }


def _catalog_entry(row: dict[str, Any], master_seed: int) -> dict[str, Any]:
    arena = int(row["arena_id"])
    attempt = int(row["attempt_id"])
    return {
        "arena_id": arena,
        "attempt_id": attempt,
        "shot_style": str(row.get("shot_style", "")),
        "replay_arguments": [
            f"--stage6-replay-master-seed={master_seed}",
            f"--stage6-replay-arena-id={arena}",
            f"--stage6-replay-attempt-id={attempt}",
        ],
    }


def build_review_catalog(
    rows: list[dict[str, Any]], master_seed: int
) -> dict[str, Any]:
    contacts = classified_contacts(rows)

    def best(
        predicate: Callable[[dict[str, Any]], bool],
        score: Callable[[dict[str, Any]], tuple[float, ...]],
        count: int,
    ) -> list[dict[str, Any]]:
        selected = sorted(
            (row for row in contacts if predicate(row)),
            key=score,
            reverse=True,
        )[:count]
        return [_catalog_entry(row, master_seed) for row in selected]

    catches = best(
        lambda row: row.get("glove_handling_outcome") == "Catch",
        lambda row: (
            number(row, "glove_palm_alignment"),
            -number(row, "glove_capture_distance_m"),
            -number(row, "glove_incoming_speed_mps"),
        ),
        4,
    )
    punches = best(
        lambda row: row.get("glove_handling_outcome") == "Punch",
        lambda row: (
            number(row, "glove_forward_speed_mps"),
            number(row, "glove_palm_alignment"),
            number(row, "glove_incoming_speed_mps"),
        ),
        4,
    )
    edges = best(
        lambda row: row.get("glove_contact_region") == "Edge",
        lambda row: (number(row, "glove_normalized_contact_extent"),),
        2,
    )
    backs = best(
        lambda row: row.get("glove_contact_region") == "Back",
        lambda row: (-number(row, "glove_palm_alignment"),),
        2,
    )
    entries = catches + punches + edges + backs
    if len(catches) < 4 or len(punches) < 4 or len(edges) < 2 or len(backs) < 2:
        raise ValueError(
            "Selected profile did not produce the required 4/4/2/2 review catalog"
        )
    return {
        "schema_version": 1,
        "catalog_id": "keeper-glove-handling-v2-review-12",
        "master_seed": master_seed,
        "categories": {
            "catch": len(catches),
            "punch": len(punches),
            "edge": len(edges),
            "back": len(backs),
        },
        "entries": entries,
    }


def _write_frozen_profile(
    selection: dict[str, Any], selection_path: Path, frozen_path: Path
) -> None:
    selected = selection["selected_profile"]
    frozen = {
        "schema_version": 1,
        "contract_id": "keeper-glove-handling-v2",
        "profile_id": selected,
        "profile_index": PROFILE_IDS[selected],
        "selection_report_sha256": file_hash(selection_path),
        "episode_key_digest": selection["profiles"][selected]["metrics"][
            "episode_key_digest"
        ],
    }
    frozen_path.parent.mkdir(parents=True, exist_ok=True)
    frozen_path.write_text(json.dumps(frozen, indent=2) + "\n", encoding="utf-8")


def _validate_catalog_source(
    rows: list[dict[str, Any]],
    selected_profile: str,
    expected_attempts: int,
    expected_benchmark_id: str | None = None,
    expected_arena_count: int | None = None,
    expected_attempts_per_arena: int | None = None,
) -> dict[str, Any]:
    if len(rows) != expected_attempts:
        raise ValueError(
            f"Review catalog source has {len(rows)} attempts; expected {expected_attempts}"
        )
    profile_ids = {
        str(row.get("glove_handling_profile_id", "")) for row in rows
    }
    if profile_ids != {selected_profile}:
        raise ValueError(
            "Review catalog source does not use only the selected fixed profile"
        )
    versions = {int(number(row, "glove_handling_version", -1)) for row in rows}
    if versions != {2}:
        raise ValueError("Review catalog source is not Glove Handling v2")
    benchmark_ids = {str(row.get("benchmark_id", "")) for row in rows}
    if len(benchmark_ids) != 1 or not next(iter(benchmark_ids)):
        raise ValueError("Review catalog source must contain one benchmark ID")
    benchmark_id = next(iter(benchmark_ids))
    if expected_benchmark_id is not None and benchmark_id != expected_benchmark_id:
        raise ValueError("Review catalog source benchmark ID does not match")
    keys = [episode_key(row) for row in rows]
    logical_keys = {(arena_id, attempt_id) for arena_id, attempt_id, _ in keys}
    if len(logical_keys) != len(keys):
        raise ValueError("Review catalog source contains duplicate episode keys")
    if expected_arena_count is not None and expected_attempts_per_arena is not None:
        expected_ids = set(range(1, expected_attempts_per_arena + 1))
        by_arena: dict[int, set[int]] = defaultdict(set)
        for arena_id, attempt_id, _ in keys:
            by_arena[arena_id].add(attempt_id)
        if set(by_arena) != set(range(expected_arena_count)) or any(
            by_arena[arena_id] != expected_ids for arena_id in range(expected_arena_count)
        ):
            raise ValueError("Review catalog source has incomplete per-arena quotas")
    summary = summarize(rows)
    if not summary["safety_passed"]:
        raise ValueError("Review catalog source contains safety failures")
    return {
        "benchmark_id": benchmark_id,
        "attempts": len(rows),
        "episode_key_digest": summary["episode_key_digest"],
        "profile_id": selected_profile,
    }


def render_config(args: argparse.Namespace) -> None:
    raw = json.loads(args.base.read_text(encoding="utf-8"))
    raw["benchmark_id"] = args.benchmark_id
    raw["master_seed"] = args.master_seed
    raw["attempts_per_arena"] = args.attempts_per_arena
    raw["total_attempts"] = raw["arena_count"] * args.attempts_per_arena
    raw["glove_geometry_id"] = (
        "goalkeeper-sphere-gloves-legacy-v1"
        if args.version == 0
        else "goalkeeper-palm-compound-v1"
    )
    raw["glove_handling_id"] = {
        0: "keeper-glove-physx-legacy-v1",
        1: "keeper-glove-handling-v1",
        2: "keeper-glove-handling-v2",
    }[args.version]
    parameters = raw.setdefault("environment_parameters", {})
    parameters["stage6.glove_handling_version"] = args.version
    parameters["stage6.glove_handling_v1"] = 1 if args.version == 1 else 0
    if args.version == 2:
        parameters["stage6.glove_handling_profile"] = PROFILE_IDS[args.profile]
    else:
        parameters.pop("stage6.glove_handling_profile", None)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(raw, indent=2) + "\n", encoding="utf-8")


def select_command(args: argparse.Namespace) -> None:
    baseline_rows = read_csv(args.baseline)
    candidate_paths = {
        name: path for name, path in (item.split("=", 1) for item in args.profile)
    }
    if set(candidate_paths) != set(PROFILE_IDS):
        raise ValueError("All conservative, balanced, and permissive profiles are required")
    candidates = {name: read_csv(Path(path)) for name, path in candidate_paths.items()}
    selection = select_profile(baseline_rows, candidates)
    selected = selection["selected_profile"]
    output = {
        "schema_version": 1,
        "contract_id": "keeper-glove-handling-v2",
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "development_master_seed": args.master_seed,
        **selection,
        "source_files": {
            "baseline": {"path": str(args.baseline), "sha256": file_hash(args.baseline)},
            **{
                name: {"path": str(path), "sha256": file_hash(Path(path))}
                for name, path in candidate_paths.items()
            },
        },
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
    if not selected:
        raise SystemExit("No fixed calibration profile passed. Stopping before visual review.")
    _write_frozen_profile(selection, args.output, args.frozen)
    print(f"Selected fixed profile: {selected}")
    try:
        catalog = build_review_catalog(candidates[selected], args.master_seed)
    except ValueError:
        print("Development batch lacks the complete 4/4/2/2 review catalog.")
        print("A separate deterministic review-only search is required.")
    else:
        args.catalog.write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8")
        print(f"Manual review catalog: {args.catalog}")


def catalog_command(args: argparse.Namespace) -> None:
    selection = json.loads(args.selection.read_text(encoding="utf-8"))
    selected = selection.get("selected_profile")
    if not selection.get("passed") or selected not in PROFILE_IDS:
        raise ValueError("A passing fixed profile selection is required")
    rows = read_csv(args.source)
    source = _validate_catalog_source(
        rows,
        selected,
        args.expected_attempts,
        args.expected_benchmark_id,
        args.expected_arena_count,
        args.expected_attempts_per_arena,
    )
    catalog = build_review_catalog(rows, args.master_seed)
    catalog["source"] = {
        **source,
        "episodes_csv": str(args.source),
        "episodes_csv_sha256": file_hash(args.source),
        "purpose": "visual_review_only",
        "excluded_from_profile_selection": True,
    }
    args.catalog.parent.mkdir(parents=True, exist_ok=True)
    args.catalog.write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8")
    _write_frozen_profile(selection, args.selection, args.frozen)
    print(f"Frozen profile: {selected}")
    print(f"Manual review catalog: {args.catalog}")


def approve_command(args: argparse.Namespace) -> None:
    frozen = json.loads(args.frozen.read_text(encoding="utf-8"))
    approval = {
        "schema_version": 1,
        "approved": True,
        "approved_at": datetime.now(timezone.utc).isoformat(),
        "reviewer": args.reviewer,
        "frozen_profile_sha256": file_hash(args.frozen),
        "profile_id": frozen["profile_id"],
        "criteria": [
            "catches hold without excessive snapping",
            "punches correspond to visible forward glove motion",
            "edge and back contacts remain fallible",
        ],
    }
    args.output.write_text(json.dumps(approval, indent=2) + "\n", encoding="utf-8")
    print(f"Visual approval recorded for {frozen['profile_id']}")


def verify_approval(frozen: Path, approval: Path) -> dict[str, Any]:
    profile = json.loads(frozen.read_text(encoding="utf-8"))
    signed = json.loads(approval.read_text(encoding="utf-8"))
    if not signed.get("approved"):
        raise ValueError("Visual review has not been approved")
    if signed.get("frozen_profile_sha256") != file_hash(frozen):
        raise ValueError("Visual approval does not match the frozen profile")
    return profile


def _height(row: dict[str, Any]) -> str:
    y = number(row, "requested_target_local_y")
    normalized = max(0.0, min(1.0, (y - 0.11) / (2.90 - 0.11)))
    return "low" if normalized < 1 / 3 else "middle" if normalized < 2 / 3 else "high"


def _horizontal(row: dict[str, Any]) -> str:
    x = max(-1.0, min(1.0, number(row, "requested_target_local_x") / 3.49))
    return "left" if x < -0.5 else "left-center" if x < 0 else "right-center" if x < 0.5 else "right"


def _speed(row: dict[str, Any]) -> str:
    speed = number(row, "launch_speed_mps")
    return "slow" if speed < 18 else "medium" if speed < 23 else "fast"


def _band_rates(
    rows: list[dict[str, Any]], classifier: Callable[[dict[str, Any]], str]
) -> dict[str, dict[str, Any]]:
    groups: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in primary_rows(rows):
        groups[classifier(row)].append(row)
    return {
        key: rate(
            sum(row.get("outcome") in SAVE_OUTCOMES for row in values), len(values)
        )
        for key, values in sorted(groups.items())
    }


def promotion_gate(
    baseline_rows: list[dict[str, Any]],
    candidate_rows: list[dict[str, Any]],
    stage: str,
) -> dict[str, Any]:
    baseline = summarize(baseline_rows)
    candidate = summarize(candidate_rows)
    save_tolerance = 0.01 if stage == "holdout" else 0.005
    checks = profile_eligibility(baseline, candidate)
    checks["save_rate"] = (
        candidate["save_rate"]["value"]
        >= baseline["save_rate"]["value"] - save_tolerance
    )
    band_regressions: list[dict[str, Any]] = []
    if stage == "promotion":
        classifiers = {
            "shot_style": lambda row: str(row.get("shot_style", "unknown")),
            "height": _height,
            "speed": _speed,
            "horizontal": _horizontal,
        }
        for family, classifier in classifiers.items():
            left = _band_rates(baseline_rows, classifier)
            right = _band_rates(candidate_rows, classifier)
            for band in sorted(set(left) & set(right)):
                attempts = min(left[band]["total"], right[band]["total"])
                delta = right[band]["value"] - left[band]["value"]
                if attempts >= 100 and delta < -0.02:
                    band_regressions.append(
                        {"family": family, "band": band, "attempts": attempts, "delta": delta}
                    )
        checks["band_regressions"] = not band_regressions
    return {
        "stage": stage,
        "passed": all(checks.values()),
        "checks": checks,
        "failed_checks": [name for name, passed in checks.items() if not passed],
        "baseline": baseline,
        "candidate": candidate,
        "band_regressions": band_regressions,
    }


def promote_command(args: argparse.Namespace) -> None:
    profile = verify_approval(args.frozen, args.approval)
    report = promotion_gate(read_csv(args.baseline), read_csv(args.candidate), args.stage)
    report.update(
        {
            "schema_version": 1,
            "contract_id": "keeper-glove-handling-v2",
            "profile_id": profile["profile_id"],
            "master_seed": args.master_seed,
            "generated_at": datetime.now(timezone.utc).isoformat(),
            "baseline_csv_sha256": file_hash(args.baseline),
            "candidate_csv_sha256": file_hash(args.candidate),
        }
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if not report["passed"]:
        raise SystemExit(
            f"{args.stage} gate failed: {', '.join(report['failed_checks'])}"
        )
    print(f"{args.stage.title()} gate passed for {profile['profile_id']}")


def finalize_command(args: argparse.Namespace) -> None:
    selection = json.loads(args.selection.read_text(encoding="utf-8"))
    holdout = json.loads(args.holdout.read_text(encoding="utf-8"))
    promotion = json.loads(args.promotion.read_text(encoding="utf-8"))
    approval = json.loads(args.approval.read_text(encoding="utf-8"))
    if not selection.get("passed") or not holdout.get("passed") or not promotion.get("passed"):
        raise ValueError("Cannot publish evidence before every fixed gate passes")
    report = {
        "schema_version": 1,
        "stage": "6.5",
        "contract_id": "keeper-glove-handling-v2",
        "geometry_id": "goalkeeper-palm-compound-v1",
        "status": "promotion_gate_passed_default_change_pending",
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "selected_profile": selection["selected_profile"],
        "development": {
            "master_seed": selection["development_master_seed"],
            "baseline": selection["baseline"],
            "selected_candidate": selection["profiles"][selection["selected_profile"]],
        },
        "visual_approval": {
            "approved": approval["approved"],
            "approved_at": approval["approved_at"],
            "reviewer": approval["reviewer"],
        },
        "holdout": holdout,
        "promotion": promotion,
        "training_performed": False,
        "legacy_contracts_retained": [
            "keeper-glove-physx-legacy-v1",
            "keeper-glove-handling-v1",
        ],
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Compact promotion evidence written to {args.output}")


def parser() -> argparse.ArgumentParser:
    root = argparse.ArgumentParser(description=__doc__)
    commands = root.add_subparsers(dest="command", required=True)

    render = commands.add_parser("render-config")
    render.add_argument("--base", type=Path, required=True)
    render.add_argument("--output", type=Path, required=True)
    render.add_argument("--benchmark-id", required=True)
    render.add_argument("--master-seed", type=int, required=True)
    render.add_argument("--attempts-per-arena", type=int, required=True)
    render.add_argument("--version", type=int, choices=(0, 1, 2), required=True)
    render.add_argument("--profile", choices=tuple(PROFILE_IDS), default="balanced")
    render.set_defaults(handler=render_config)

    select = commands.add_parser("select")
    select.add_argument("--baseline", type=Path, required=True)
    select.add_argument("--profile", action="append", required=True)
    select.add_argument("--master-seed", type=int, required=True)
    select.add_argument("--output", type=Path, required=True)
    select.add_argument("--frozen", type=Path, required=True)
    select.add_argument("--catalog", type=Path, required=True)
    select.set_defaults(handler=select_command)

    catalog = commands.add_parser("catalog")
    catalog.add_argument("--selection", type=Path, required=True)
    catalog.add_argument("--source", type=Path, required=True)
    catalog.add_argument("--master-seed", type=int, required=True)
    catalog.add_argument("--expected-attempts", type=int, required=True)
    catalog.add_argument("--expected-benchmark-id", required=True)
    catalog.add_argument("--expected-arena-count", type=int, required=True)
    catalog.add_argument("--expected-attempts-per-arena", type=int, required=True)
    catalog.add_argument("--frozen", type=Path, required=True)
    catalog.add_argument("--catalog", type=Path, required=True)
    catalog.set_defaults(handler=catalog_command)

    approve = commands.add_parser("approve")
    approve.add_argument("--frozen", type=Path, required=True)
    approve.add_argument("--output", type=Path, required=True)
    approve.add_argument("--reviewer", required=True)
    approve.set_defaults(handler=approve_command)

    promote = commands.add_parser("promote")
    promote.add_argument("--frozen", type=Path, required=True)
    promote.add_argument("--approval", type=Path, required=True)
    promote.add_argument("--baseline", type=Path, required=True)
    promote.add_argument("--candidate", type=Path, required=True)
    promote.add_argument("--stage", choices=("holdout", "promotion"), required=True)
    promote.add_argument("--master-seed", type=int, required=True)
    promote.add_argument("--output", type=Path, required=True)
    promote.set_defaults(handler=promote_command)

    finalize = commands.add_parser("finalize")
    finalize.add_argument("--selection", type=Path, required=True)
    finalize.add_argument("--holdout", type=Path, required=True)
    finalize.add_argument("--promotion", type=Path, required=True)
    finalize.add_argument("--approval", type=Path, required=True)
    finalize.add_argument("--output", type=Path, required=True)
    finalize.set_defaults(handler=finalize_command)
    return root


def main() -> None:
    args = parser().parse_args()
    args.handler(args)


if __name__ == "__main__":
    main()
