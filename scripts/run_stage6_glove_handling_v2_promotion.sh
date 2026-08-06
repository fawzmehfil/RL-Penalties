#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/unity"
UNITY="/Applications/Unity/Hub/Editor/6000.0.74f1/Unity.app/Contents/MacOS/Unity"
BUILD="$ROOT/builds/macos/PenaltyShootoutStage6.app"
BASE="$ROOT/configs/benchmarks/goalkeeper-control-v2-human-shot-v1-glove-handling-2k.json"
MODEL="$ROOT/results/supervision/goalkeeper-control-v2-split-v2/seed-001/model-manifest.json"
CALIBRATION="$ROOT/results/glove-handling-v2/calibration"
PROMOTION="$ROOT/results/glove-handling-v2/promotion"
CONFIGS="$PROMOTION/configs"
FROZEN="$CALIBRATION/frozen-profile.json"
APPROVAL="$CALIBRATION/visual-approval.json"
# 20260821 is calibration and 20260822 is visual-catalog discovery.
# Keep holdout and promotion blind to both datasets.
HOLDOUT_SEED=20260824
PROMOTION_SEED=20260823

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

test "$HOLDOUT_SEED" != "$PROMOTION_SEED" ||
  fail "Holdout and promotion seeds must remain distinct"

cd "$ROOT"
test -x "$UNITY" || fail "Unity 6000.0.74f1 is not installed"
test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
test -f "$FROZEN" || fail "Run the Stage 6.5 preparation handoff first"
test -f "$APPROVAL" || fail "Run scripts/approve_stage6_glove_handling_v2.sh first"
test -f "$MODEL" || fail "Stage 5 native split model manifest is missing"
pgrep -f "Unity.app/Contents/MacOS/Unity.*-project[Pp]ath $PROJECT" >/dev/null &&
  fail "Close the Unity editor for this project before promotion"
mkdir -p "$CONFIGS"

PROFILE="$(.venv/bin/python -c 'import json,sys; print(json.load(open(sys.argv[1]))["profile_id"])' "$FROZEN")"

render() {
  local name="$1"
  local seed="$2"
  local attempts="$3"
  local version="$4"
  .venv/bin/python -m penalty_shootout.evaluation.stage6_glove_handling_v2 \
    render-config \
    --base "$BASE" \
    --output "$CONFIGS/$name.json" \
    --benchmark-id "$name" \
    --master-seed "$seed" \
    --attempts-per-arena "$attempts" \
    --version "$version" \
    --profile "$PROFILE"
}

evaluate() {
  local name="$1"
  arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
    --benchmark "$CONFIGS/$name.json" \
    --build "$BUILD" \
    --policy "native_split_v1:$MODEL" \
    --run-id "$name"
}

echo "Stage 6.5: rebuilding the visually approved candidate"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage6ProjectBuilder.BuildMac \
  -logFile "$ROOT/docs/stage6-glove-v2-promotion-build.log"

render stage6-glove-v2-holdout-v1-400 "$HOLDOUT_SEED" 25 1
render stage6-glove-v2-holdout-v2-400 "$HOLDOUT_SEED" 25 2
evaluate stage6-glove-v2-holdout-v1-400
evaluate stage6-glove-v2-holdout-v2-400

.venv/bin/python -m penalty_shootout.evaluation.stage6_glove_handling_v2 \
  promote \
  --frozen "$FROZEN" \
  --approval "$APPROVAL" \
  --baseline "$ROOT/results/evaluations/stage6-glove-v2-holdout-v1-400/episodes.csv" \
  --candidate "$ROOT/results/evaluations/stage6-glove-v2-holdout-v2-400/episodes.csv" \
  --stage holdout \
  --master-seed "$HOLDOUT_SEED" \
  --output "$PROMOTION/holdout-report.json"

echo "Stage 6.5: holdout passed; starting paired 2,000-shot promotion"
render stage6-glove-v2-promotion-v1-2k "$PROMOTION_SEED" 125 1
render stage6-glove-v2-promotion-v2-2k "$PROMOTION_SEED" 125 2
evaluate stage6-glove-v2-promotion-v1-2k
evaluate stage6-glove-v2-promotion-v2-2k

.venv/bin/python -m penalty_shootout.evaluation.stage6_glove_handling_v2 \
  promote \
  --frozen "$FROZEN" \
  --approval "$APPROVAL" \
  --baseline "$ROOT/results/evaluations/stage6-glove-v2-promotion-v1-2k/episodes.csv" \
  --candidate "$ROOT/results/evaluations/stage6-glove-v2-promotion-v2-2k/episodes.csv" \
  --stage promotion \
  --master-seed "$PROMOTION_SEED" \
  --output "$PROMOTION/promotion-report.json"

.venv/bin/python -m penalty_shootout.evaluation.stage6_glove_handling_v2 \
  finalize \
  --selection "$CALIBRATION/selection-report.json" \
  --holdout "$PROMOTION/holdout-report.json" \
  --promotion "$PROMOTION/promotion-report.json" \
  --approval "$APPROVAL" \
  --output "$ROOT/docs/stage6-glove-handling-v2-report.json"

echo "Stage 6.5 promotion gates passed. No default was changed and no training ran."
