#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD="$PROJECT_ROOT/builds/macos/PenaltyShootoutStage5.app"
BENCHMARK="$PROJECT_ROOT/configs/benchmarks/goalkeeper-control-v2-high-forward-contact-2k.json"
CONTRACT="$PROJECT_ROOT/configs/audits/stage6-high-shot-forward-contact-v1.json"
MANIFEST="$PROJECT_ROOT/results/supervision/goalkeeper-control-v2-split-v2/seed-001/model-manifest.json"
RUN_ID="stage6-high-shot-forward-contact-v1-2k"
OUTPUT="$PROJECT_ROOT/results/evaluations/$RUN_ID"

cd "$PROJECT_ROOT"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
test -d "$BUILD" || fail "Run scripts/run_stage6_low_shot_capability_audit.sh first"
test -f "$MANIFEST" || fail "Missing frozen split-controller manifest"
test -f "$BENCHMARK" || fail "Missing high-shot benchmark contract"
test -f "$CONTRACT" || fail "Missing high-shot audit contract"
test ! -e "$OUTPUT" || fail "$OUTPUT already exists; preserve or rename it before rerunning"

pgrep -f "[p]enalty_shootout.evaluation.goalkeeper|[P]enaltyShootoutStage5" \
  >/dev/null && fail "Another Stage 5 evaluation is already running"

echo "Stage 6 preflight: validating the high-shot regression tooling"
arch -x86_64 .venv/bin/python -m pytest \
  python/tests/test_stage6_high_shot_regression.py \
  python/tests/test_stage5_native_inference.py \
  -q

echo "Stage 6 preflight: evaluating 2,000 fixed high shots"
arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark "$BENCHMARK" \
  --build "$BUILD" \
  --policy "native_split_v1:$MANIFEST" \
  --worker-id-start 730 \
  --run-id "$RUN_ID"

arch -x86_64 .venv/bin/python -m \
  penalty_shootout.evaluation.high_shot_regression \
  --evaluation "$OUTPUT/report.json" \
  --contract "$CONTRACT" \
  --output "$OUTPUT/regression-report.json" \
  --summary "$OUTPUT/regression-summary.md"

echo "Regression audit complete: $OUTPUT/regression-summary.md"
