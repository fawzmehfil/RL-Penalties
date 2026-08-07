#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE="$ROOT/results/evaluations/stage8-goalkeeper-heatmap-source-20k"
CONFIG="$ROOT/configs/analysis/stage8-goalkeeper-analysis-v1.json"
BENCHMARK="$ROOT/configs/benchmarks/goalkeeper-control-v2-stage8-heatmap-source-20k.json"
OUTPUT="$ROOT/results/analysis/stage8-goalkeeper-analysis-v1.json"
WEB_OUTPUT="$ROOT/web/stage8-analysis/public/data/goalkeeper-analysis-v1.json"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

cd "$ROOT"
test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
test -f "$SOURCE/report.json" || fail "Stage 8 source report is missing"
test -f "$SOURCE/episodes.csv" || fail "Stage 8 source episodes are missing"

.venv/bin/python -m penalty_shootout.evaluation.stage8_heatmap_source \
  --benchmark "$BENCHMARK" \
  --report "$SOURCE/report.json" \
  --episodes "$SOURCE/episodes.csv" \
  --output "$SOURCE/source-manifest.json"

.venv/bin/python -m penalty_shootout.evaluation.stage8_analysis \
  --config "$CONFIG" \
  --source-manifest "$SOURCE/source-manifest.json" \
  --report "$SOURCE/report.json" \
  --episodes "$SOURCE/episodes.csv" \
  --output "$OUTPUT"

mkdir -p "$(dirname "$WEB_OUTPUT")"
cp "$OUTPUT" "$WEB_OUTPUT"
cmp -s "$OUTPUT" "$WEB_OUTPUT" || fail "Web analysis data differs from generated artifact"

echo "Stage 8 analysis data ready: $OUTPUT"
echo "Stage 8 web data ready: $WEB_OUTPUT"
