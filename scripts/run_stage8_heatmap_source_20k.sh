#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="$ROOT/configs/benchmarks/goalkeeper-control-v2-stage8-heatmap-source-20k.json"
BUILD="$ROOT/builds/macos/PenaltyShootoutStage6.app"
MODEL="$ROOT/results/supervision/goalkeeper-control-v2-split-v2/seed-001/model-manifest.json"
RUN_ID="stage8-goalkeeper-heatmap-source-20k"
OUTPUT="$ROOT/results/evaluations/$RUN_ID"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

cd "$ROOT"
test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
test -d "$BUILD" || fail "Stage 6 macOS build is missing: $BUILD"
test -f "$MODEL" || fail "Selected native split model manifest is missing: $MODEL"
test ! -e "$OUTPUT/episodes.csv" || fail "Output already exists: $OUTPUT"
pgrep -f "penalty_shootout.evaluation.goalkeeper|mlagents-learn" >/dev/null &&
  fail "Another evaluation or training process is running"

echo "Stage 8: fixed 20,000-shot paired heatmap source evaluation"
echo "Policies run sequentially: reactive teacher, final goalkeeper"
arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark "$CONFIG" \
  --build "$BUILD" \
  --policy reactive_curve_v1 \
  --policy "native_split_v1:$MODEL" \
  --worker-id-start 180 \
  --run-id "$RUN_ID"

arch -x86_64 .venv/bin/python \
  -m penalty_shootout.evaluation.stage8_heatmap_source \
  --benchmark "$CONFIG" \
  --report "$OUTPUT/report.json" \
  --episodes "$OUTPUT/episodes.csv" \
  --output "$OUTPUT/source-manifest.json"

echo
echo "Stage 8 source data is ready. No training was performed."
cat "$OUTPUT/summary.md"
