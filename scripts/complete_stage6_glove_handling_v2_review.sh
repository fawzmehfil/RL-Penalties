#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BUILD="$ROOT/builds/macos/PenaltyShootoutStage6.app"
BASE="$ROOT/configs/benchmarks/goalkeeper-control-v2-human-shot-v1-glove-handling-2k.json"
MODEL="$ROOT/results/supervision/goalkeeper-control-v2-split-v2/seed-001/model-manifest.json"
CALIBRATION="$ROOT/results/glove-handling-v2/calibration"
CONFIGS="$CALIBRATION/configs"
SELECTION="$CALIBRATION/selection-report.json"
FROZEN="$CALIBRATION/frozen-profile.json"
CATALOG="$CALIBRATION/manual-review-catalog.json"
REVIEW_SEED=20260822
REVIEW_ATTEMPTS_PER_ARENA=125
REVIEW_TOTAL_ATTEMPTS=2000
RUN_ID=stage6-glove-v2-review-catalog-2000
CONFIG="$CONFIGS/$RUN_ID.json"
EPISODES="$ROOT/results/evaluations/$RUN_ID/episodes.csv"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

cd "$ROOT"
test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
test -d "$BUILD" || fail "Stage 6 evaluator build is missing"
test -f "$MODEL" || fail "Stage 5 native split model manifest is missing"
test -f "$SELECTION" || fail "Run scripts/prepare_stage6_glove_handling_v2.sh first"
mkdir -p "$CONFIGS"

PROFILE="$(.venv/bin/python -c \
  'import json,sys; data=json.load(open(sys.argv[1])); assert data.get("passed"); print(data["selected_profile"])' \
  "$SELECTION")"

echo "Stage 6.5: preparing deterministic review-only search for $PROFILE"
.venv/bin/python -m penalty_shootout.evaluation.stage6_glove_handling_v2 \
  render-config \
  --base "$BASE" \
  --output "$CONFIG" \
  --benchmark-id "$RUN_ID" \
  --master-seed "$REVIEW_SEED" \
  --attempts-per-arena "$REVIEW_ATTEMPTS_PER_ARENA" \
  --version 2 \
  --profile "$PROFILE"

if test ! -f "$EPISODES"; then
  echo "Stage 6.5: collecting 2,000 review-only attempts"
  arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
    --benchmark "$CONFIG" \
    --build "$BUILD" \
    --policy "native_split_v1:$MODEL" \
    --run-id "$RUN_ID"
else
  echo "Stage 6.5: reusing existing review-only episodes"
fi

.venv/bin/python -m penalty_shootout.evaluation.stage6_glove_handling_v2 \
  catalog \
  --selection "$SELECTION" \
  --source "$EPISODES" \
  --master-seed "$REVIEW_SEED" \
  --expected-attempts "$REVIEW_TOTAL_ATTEMPTS" \
  --expected-benchmark-id "$RUN_ID" \
  --expected-arena-count 16 \
  --expected-attempts-per-arena "$REVIEW_ATTEMPTS_PER_ARENA" \
  --frozen "$FROZEN" \
  --catalog "$CATALOG"

echo
echo "Review artifacts are ready. Open ShotVarietyLab in Unity, press B until"
echo "glove handling v2 is selected, and inspect all 12 N/P catalog cases."
echo "After visual approval run: scripts/approve_stage6_glove_handling_v2.sh"
