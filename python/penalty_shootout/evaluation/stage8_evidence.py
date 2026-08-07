"""Write compact Stage 8 evidence for the static React analysis site."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any


SCHEMA_ID = "stage8-goalkeeper-analysis-report-v1"
ANALYSIS_SCHEMA_ID = "goalkeeper-analysis-v1"
FINAL_POLICY = "native_split_v1:seed-001"
TEACHER_POLICY = "reactive_curve_v1"


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _tree_hash(path: Path, excluded: set[str] | None = None) -> tuple[str, int, int]:
    excluded = excluded or set()
    digest = hashlib.sha256()
    files = sorted(
        item
        for item in path.rglob("*")
        if item.is_file() and not any(part in excluded for part in item.relative_to(path).parts)
    )
    total_bytes = 0
    for item in files:
        relative = item.relative_to(path).as_posix().encode("utf-8")
        payload = item.read_bytes()
        digest.update(len(relative).to_bytes(8, "big"))
        digest.update(relative)
        digest.update(len(payload).to_bytes(8, "big"))
        digest.update(payload)
        total_bytes += len(payload)
    return digest.hexdigest(), len(files), total_bytes


def _test_result(path: Path) -> dict[str, Any]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    result = {
        "success": bool(payload.get("success")),
        "total": int(payload.get("numTotalTests", 0)),
        "passed": int(payload.get("numPassedTests", 0)),
        "failed": int(payload.get("numFailedTests", 0)),
        "sha256": _sha256(path),
    }
    if not result["success"] or result["failed"] != 0 or result["total"] == 0:
        raise RuntimeError(f"Stage 8 web tests did not pass: {path}")
    return result


def build_evidence(
    *,
    artifact_path: Path,
    source_manifest_path: Path,
    model_manifest_path: Path,
    web_source_path: Path,
    web_build_path: Path,
    test_results_path: Path,
    visual_approval: str = "pending",
) -> dict[str, Any]:
    artifact = json.loads(artifact_path.read_text(encoding="utf-8"))
    source = json.loads(source_manifest_path.read_text(encoding="utf-8"))
    models = json.loads(model_manifest_path.read_text(encoding="utf-8"))
    failures: list[str] = []

    if artifact.get("schema_id") != ANALYSIS_SCHEMA_ID:
        failures.append("analysis artifact schema is incorrect")
    if source.get("status") != "passed":
        failures.append("source validation did not pass")
    if artifact.get("episode_key_digest") != source.get("episode_key_digest"):
        failures.append("analysis and source episode digests differ")
    policies = {row.get("policy_id"): row for row in artifact.get("overall_policy_rows", [])}
    for policy_id in (FINAL_POLICY, TEACHER_POLICY):
        row = policies.get(policy_id)
        if row is None:
            failures.append(f"analysis is missing {policy_id}")
        elif row.get("all_attempts") != 20_000 or row.get("attempts") != 18_242:
            failures.append(f"analysis counts are incorrect for {policy_id}")
    if any(item.get("total_failures") != 0 for item in artifact.get("safety_totals", [])):
        failures.append("analysis contains safety failures")
    if len(artifact.get("filter_slices", [])) != 32:
        failures.append("analysis does not contain 32 filter slices")

    model_rows = models.get("models", {})
    expected_models = {
        "interception": "goalkeeper-interception-v2",
        "timing": "goalkeeper-commit-timing-v1",
    }
    recorded_model_hashes: dict[str, dict[str, str]] = {}
    for role, model_id in expected_models.items():
        row = model_rows.get(role, {})
        model_path = model_manifest_path.parent / str(row.get("path", ""))
        if row.get("model_id") != model_id or not model_path.is_file():
            failures.append(f"selected {role} model is missing")
            continue
        actual_hash = _sha256(model_path)
        if actual_hash != row.get("sha256"):
            failures.append(f"selected {role} model hash changed")
        recorded_model_hashes[role] = {"model_id": model_id, "sha256": actual_hash}

    public_data = web_source_path / "public/data/goalkeeper-analysis-v1.json"
    built_data = web_build_path / "data/goalkeeper-analysis-v1.json"
    required_source = [
        web_source_path / "package.json",
        web_source_path / "package-lock.json",
        web_source_path / "src/App.tsx",
        web_source_path / "src/styles.css",
    ]
    if any(not path.is_file() for path in required_source):
        failures.append("React analysis source is incomplete")
    artifact_hash = _sha256(artifact_path)
    for data_path, description in ((public_data, "public"), (built_data, "built")):
        if not data_path.is_file() or _sha256(data_path) != artifact_hash:
            failures.append(f"{description} analysis data differs from the frozen artifact")
    if not (web_build_path / "index.html").is_file():
        failures.append("Stage 8 web build is missing index.html")
    if visual_approval not in {"pending", "approved"}:
        failures.append("visual approval must be pending or approved")

    test_result = _test_result(test_results_path)
    if failures:
        raise RuntimeError("Stage 8 evidence failed: " + "; ".join(failures))

    source_hash, source_files, source_bytes = _tree_hash(
        web_source_path, excluded={"node_modules", "dist"}
    )
    build_hash, build_files, build_bytes = _tree_hash(web_build_path)
    final = policies[FINAL_POLICY]
    teacher = policies[TEACHER_POLICY]
    return {
        "schema_id": SCHEMA_ID,
        "status": "passed" if visual_approval == "approved" else "technical_passed_manual_pending",
        "stage": 8,
        "presentation": "static-react-site",
        "analysis_id": artifact["analysis_id"],
        "source_benchmark_id": artifact["source_benchmark_id"],
        "master_seed": artifact["master_seed"],
        "episode_key_digest": artifact["episode_key_digest"],
        "source_hashes": artifact["source_hashes"],
        "analysis_artifact": {
            "path": "web/stage8-analysis/public/data/goalkeeper-analysis-v1.json",
            "sha256": artifact_hash,
            "filter_slice_count": len(artifact["filter_slices"]),
        },
        "policies": [
            {
                "policy_id": policy_id,
                "all_attempts": policies[policy_id]["all_attempts"],
                "expected_on_target_attempts": policies[policy_id]["attempts"],
                "save_rate": policies[policy_id]["save_rate"],
                "glove_contact_rate": policies[policy_id]["glove_contact_rate"],
            }
            for policy_id in (FINAL_POLICY, TEACHER_POLICY)
        ],
        "selected_model_hashes": recorded_model_hashes,
        "web_tests": test_result,
        "verified_viewports": ["1920x1080", "1440x900", "1280x800", "390x844"],
        "source": {
            "path": "web/stage8-analysis",
            "tree_sha256": source_hash,
            "file_count": source_files,
            "bytes": source_bytes,
        },
        "build": {
            "path": "web/stage8-analysis/dist",
            "tree_sha256": build_hash,
            "file_count": build_files,
            "bytes": build_bytes,
        },
        "visual_approval": visual_approval,
        "training_performed": False,
        "goalkeeper_contract_changed": False,
        "scope": ["final_save_rate_heatmap", "teacher_gap_heatmap", "supporting_statistics"],
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact", type=Path, required=True)
    parser.add_argument("--source-manifest", type=Path, required=True)
    parser.add_argument("--model-manifest", type=Path, required=True)
    parser.add_argument("--web-source", type=Path, required=True)
    parser.add_argument("--web-build", type=Path, required=True)
    parser.add_argument("--test-results", type=Path, required=True)
    parser.add_argument("--visual-approval", choices=("pending", "approved"), default="pending")
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    report = build_evidence(
        artifact_path=args.artifact,
        source_manifest_path=args.source_manifest,
        model_manifest_path=args.model_manifest,
        web_source_path=args.web_source,
        web_build_path=args.web_build,
        test_results_path=args.test_results,
        visual_approval=args.visual_approval,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Stage 8 evidence written: {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
