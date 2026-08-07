import hashlib
import json
from pathlib import Path

import pytest

from penalty_shootout.evaluation.stage8_evidence import build_evidence


def _fixture(tmp_path: Path) -> dict[str, Path]:
    artifact = tmp_path / "analysis.json"
    source = tmp_path / "source.json"
    models_dir = tmp_path / "models"
    models_dir.mkdir()
    (models_dir / "interception.onnx").write_bytes(b"interception")
    (models_dir / "timing.onnx").write_bytes(b"timing")
    model_manifest = tmp_path / "model-manifest.json"
    model_manifest.write_text(json.dumps({"models": {
        "interception": {"model_id": "goalkeeper-interception-v2", "path": "models/interception.onnx", "sha256": hashlib.sha256(b"interception").hexdigest()},
        "timing": {"model_id": "goalkeeper-commit-timing-v1", "path": "models/timing.onnx", "sha256": hashlib.sha256(b"timing").hexdigest()},
    }}), encoding="utf-8")
    rows = [{"policy_id": policy, "all_attempts": 20_000, "attempts": 18_242, "save_rate": {"value": value}, "glove_contact_rate": {"value": value}}
            for policy, value in (("native_split_v1:seed-001", 0.54), ("reactive_curve_v1", 0.55))]
    payload = {"schema_id": "goalkeeper-analysis-v1", "analysis_id": "goalkeeper-analysis-v1", "source_benchmark_id": "benchmark", "master_seed": 20260803, "episode_key_digest": "digest", "source_hashes": [], "overall_policy_rows": rows, "safety_totals": [{"policy_id": row["policy_id"], "total_failures": 0} for row in rows], "filter_slices": [{} for _ in range(32)]}
    artifact.write_text(json.dumps(payload), encoding="utf-8")
    source.write_text(json.dumps({"status": "passed", "episode_key_digest": "digest"}), encoding="utf-8")
    web = tmp_path / "web"
    for relative in ("public/data", "src", "dist/data", "dist/assets"):
        (web / relative).mkdir(parents=True, exist_ok=True)
    for relative in ("package.json", "package-lock.json", "src/App.tsx", "src/styles.css"):
        (web / relative).write_text("source", encoding="utf-8")
    (web / "public/data/goalkeeper-analysis-v1.json").write_bytes(artifact.read_bytes())
    (web / "dist/data/goalkeeper-analysis-v1.json").write_bytes(artifact.read_bytes())
    (web / "dist/index.html").write_text("<html></html>", encoding="utf-8")
    tests = tmp_path / "tests.json"
    tests.write_text(json.dumps({"success": True, "numTotalTests": 4, "numPassedTests": 4, "numFailedTests": 0}), encoding="utf-8")
    return {"artifact_path": artifact, "source_manifest_path": source, "model_manifest_path": model_manifest, "web_source_path": web, "web_build_path": web / "dist", "test_results_path": tests}


def test_evidence_records_pending_manual_gate(tmp_path: Path) -> None:
    report = build_evidence(**_fixture(tmp_path))
    assert report["status"] == "technical_passed_manual_pending"
    assert report["presentation"] == "static-react-site"
    assert report["web_tests"]["passed"] == 4


def test_evidence_records_explicit_visual_approval(tmp_path: Path) -> None:
    report = build_evidence(**_fixture(tmp_path), visual_approval="approved")
    assert report["status"] == "passed"


def test_evidence_rejects_changed_public_data(tmp_path: Path) -> None:
    paths = _fixture(tmp_path)
    (paths["web_source_path"] / "public/data/goalkeeper-analysis-v1.json").write_text("{}", encoding="utf-8")
    with pytest.raises(RuntimeError, match="public analysis data differs"):
        build_evidence(**paths)


def test_evidence_rejects_failed_web_tests(tmp_path: Path) -> None:
    paths = _fixture(tmp_path)
    paths["test_results_path"].write_text(json.dumps({"success": False, "numTotalTests": 4, "numPassedTests": 3, "numFailedTests": 1}), encoding="utf-8")
    with pytest.raises(RuntimeError, match="web tests did not pass"):
        build_evidence(**paths)
