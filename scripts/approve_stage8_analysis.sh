#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WEB="$ROOT/web/stage8-analysis"

cd "$ROOT"
.venv/bin/python -m penalty_shootout.evaluation.stage8_evidence \
  --artifact results/analysis/stage8-goalkeeper-analysis-v1.json \
  --source-manifest results/evaluations/stage8-goalkeeper-heatmap-source-20k/source-manifest.json \
  --model-manifest results/supervision/goalkeeper-control-v2-split-v2/seed-001/model-manifest.json \
  --web-source "$WEB" \
  --web-build "$WEB/dist" \
  --test-results results/analysis/stage8-web-tests.json \
  --visual-approval approved \
  --output docs/stage8-goalkeeper-analysis-report.json

echo "Stage 8 web visual approval recorded."
