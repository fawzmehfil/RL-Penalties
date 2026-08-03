#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD="$PROJECT_ROOT/builds/macos/PenaltyShootoutStage5.app"
BENCHMARK="$PROJECT_ROOT/configs/benchmarks/goalkeeper-control-v2-low-forward-contact-2k.json"
CONTRACT="$PROJECT_ROOT/configs/audits/stage6-low-shot-forward-contact-v1.json"
MANIFEST="$PROJECT_ROOT/results/supervision/goalkeeper-control-v2-split-v2/seed-001/model-manifest.json"
RUN_ID="stage6-low-shot-forward-contact-v1-2k"
OUTPUT="$PROJECT_ROOT/results/evaluations/$RUN_ID"

cd "$PROJECT_ROOT"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
test -f "$MANIFEST" || fail "Missing frozen split-controller manifest"
test -f "$BENCHMARK" || fail "Missing low-shot benchmark contract"
test -f "$CONTRACT" || fail "Missing low-shot audit contract"
test ! -e "$OUTPUT" || fail "$OUTPUT already exists; preserve or rename it before rerunning"

pgrep -f "[p]enalty_shootout.evaluation.goalkeeper|[P]enaltyShootoutStage5" \
  >/dev/null && fail "Another Stage 5 evaluation is already running"

echo "Stage 6 preflight: verifying Unity and rebuilding the Stage 5 player"
"$PROJECT_ROOT/scripts/verify_stage5_control_v2.sh"
test -d "$BUILD" || fail "Missing rebuilt Stage 5 macOS evaluation build"

echo "Stage 6 preflight: validating the low-shot audit tooling"
arch -x86_64 .venv/bin/python -m pytest \
  python/tests/test_stage6_low_shot_capability.py \
  python/tests/test_stage5_native_inference.py \
  -q

echo "Stage 6 preflight: evaluating 2,000 fixed low shots per policy"
arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark "$BENCHMARK" \
  --build "$BUILD" \
  --policy stand_center_v1 \
  --policy reactive_reach_v1:0.25 \
  --policy reactive_reach_v1:0.35 \
  --policy reactive_reach_v1:0.45 \
  --policy reactive_reach_v1:0.55 \
  --policy reactive_reach_v1:0.62 \
  --policy "native_split_v1:$MANIFEST" \
  --worker-id-start 720 \
  --run-id "$RUN_ID"

arch -x86_64 .venv/bin/python -m \
  penalty_shootout.evaluation.low_shot_capability \
  --evaluation "$OUTPUT/report.json" \
  --contract "$CONTRACT" \
  --output "$OUTPUT/capability-report.json" \
  --summary "$OUTPUT/capability-summary.md"

echo "Audit complete: $OUTPUT/capability-summary.md"
