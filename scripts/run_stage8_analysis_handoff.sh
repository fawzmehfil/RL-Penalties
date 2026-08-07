#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WEB="$ROOT/web/stage8-analysis"
TEST_RESULTS="$ROOT/results/analysis/stage8-web-tests.json"

fail() { echo "FAIL: $*" >&2; exit 1; }

cd "$ROOT"
test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
test -f "$WEB/package-lock.json" || fail "Stage 8 package lock is missing"

echo "Stage 8: validate paired benchmark and deterministic artifact"
.venv/bin/python -m pytest -q \
  python/tests/test_stage8_heatmap_source.py \
  python/tests/test_stage8_analysis.py \
  python/tests/test_stage8_evidence.py
scripts/build_stage8_analysis_artifact.sh

echo "Stage 8: verify and build React analysis"
cd "$WEB"
npm ci
mkdir -p "$(dirname "$TEST_RESULTS")"
npm test -- --reporter=json --outputFile="$TEST_RESULTS"
npm run build

cd "$ROOT"
.venv/bin/python -m penalty_shootout.evaluation.stage8_evidence \
  --artifact "$ROOT/results/analysis/stage8-goalkeeper-analysis-v1.json" \
  --source-manifest "$ROOT/results/evaluations/stage8-goalkeeper-heatmap-source-20k/source-manifest.json" \
  --model-manifest "$ROOT/results/supervision/goalkeeper-control-v2-split-v2/seed-001/model-manifest.json" \
  --web-source "$WEB" \
  --web-build "$WEB/dist" \
  --test-results "$TEST_RESULTS" \
  --output "$ROOT/docs/stage8-goalkeeper-analysis-report.json"

echo "Stage 8 web analysis verified. Run scripts/open_stage8_analysis.sh to review it."
