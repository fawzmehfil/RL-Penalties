#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/unity"
UNITY="/Applications/Unity/Hub/Editor/6000.0.74f1/Unity.app/Contents/MacOS/Unity"
BUILD="$ROOT/builds/macos/PenaltyShootoutStage6.app"
BASE="$ROOT/configs/benchmarks/goalkeeper-control-v2-human-shot-v1-glove-handling-2k.json"
MODEL="$ROOT/results/supervision/goalkeeper-control-v2-split-v2/seed-001/model-manifest.json"
CALIBRATION="$ROOT/results/glove-handling-v2/calibration"
CONFIGS="$CALIBRATION/configs"
DEVELOPMENT_SEED=20260821

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

cd "$ROOT"
test -x "$UNITY" || fail "Unity 6000.0.74f1 is not installed"
test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
test -f "$MODEL" || fail "Stage 5 native split model manifest is missing"
pgrep -f "Unity.app/Contents/MacOS/Unity.*-project[Pp]ath $PROJECT" >/dev/null &&
  fail "Close the Unity editor for this project before calibration"
mkdir -p "$CONFIGS"

echo "Stage 6.5: Python contracts"
.venv/bin/python -m pytest -q \
  python/tests/test_stage6_human_shots.py \
  python/tests/test_stage6_glove_handling_v2.py

echo "Stage 6.5: preparing Unity assets"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage6ProjectBuilder.PrepareProject \
  -logFile "$ROOT/docs/stage6-glove-v2-prepare.log"

echo "Stage 6.5: Unity EditMode contracts"
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform EditMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage6HumanShotTests \
  -testResults "$ROOT/docs/stage6-glove-v2-editmode-results.xml" \
  -logFile "$ROOT/docs/stage6-glove-v2-editmode.log"

echo "Stage 6.5: Unity PlayMode smoke and impact fixtures"
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform PlayMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage6HumanShotPlayModeTests \
  -testResults "$ROOT/docs/stage6-glove-v2-playmode-results.xml" \
  -logFile "$ROOT/docs/stage6-glove-v2-playmode.log"

echo "Stage 6.5: rebuilding macOS evaluator"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage6ProjectBuilder.BuildMac \
  -logFile "$ROOT/docs/stage6-glove-v2-build.log"

render() {
  local name="$1"
  local version="$2"
  local profile="$3"
  .venv/bin/python -m penalty_shootout.evaluation.stage6_glove_handling_v2 \
    render-config \
    --base "$BASE" \
    --output "$CONFIGS/$name.json" \
    --benchmark-id "$name" \
    --master-seed "$DEVELOPMENT_SEED" \
    --attempts-per-arena 25 \
    --version "$version" \
    --profile "$profile"
}

evaluate() {
  local name="$1"
  arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
    --benchmark "$CONFIGS/$name.json" \
    --build "$BUILD" \
    --policy "native_split_v1:$MODEL" \
    --run-id "$name"
}

render stage6-glove-v2-development-v1-400 1 balanced
for profile in conservative balanced permissive; do
  render "stage6-glove-v2-development-$profile-400" 2 "$profile"
done

echo "Stage 6.5: fixed paired development calibration"
evaluate stage6-glove-v2-development-v1-400
for profile in conservative balanced permissive; do
  evaluate "stage6-glove-v2-development-$profile-400"
done

.venv/bin/python -m penalty_shootout.evaluation.stage6_glove_handling_v2 \
  select \
  --baseline "$ROOT/results/evaluations/stage6-glove-v2-development-v1-400/episodes.csv" \
  --profile "conservative=$ROOT/results/evaluations/stage6-glove-v2-development-conservative-400/episodes.csv" \
  --profile "balanced=$ROOT/results/evaluations/stage6-glove-v2-development-balanced-400/episodes.csv" \
  --profile "permissive=$ROOT/results/evaluations/stage6-glove-v2-development-permissive-400/episodes.csv" \
  --master-seed "$DEVELOPMENT_SEED" \
  --output "$CALIBRATION/selection-report.json" \
  --frozen "$CALIBRATION/frozen-profile.json" \
  --catalog "$CALIBRATION/manual-review-catalog.json"

"$ROOT/scripts/complete_stage6_glove_handling_v2_review.sh"
