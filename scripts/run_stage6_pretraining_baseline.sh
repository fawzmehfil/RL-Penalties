#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.0.74f1/Unity.app/Contents/MacOS/Unity"
PROJECT="$ROOT/unity"
CONFIG="$ROOT/configs/benchmarks/goalkeeper-control-v2-human-shot-v1-pretraining-2k.json"
BUILD="$ROOT/builds/macos/PenaltyShootoutStage6.app"
NATIVE_CONTRACT="$ROOT/configs/inference/goalkeeper-control-v2-split-native-v1.json"
MODEL_MANIFEST="$ROOT/results/supervision/goalkeeper-control-v2-split-v2/seed-001/model-manifest.json"
RUN_ID="stage6-human-shot-v1-pretraining-2k"
OUTPUT="$ROOT/results/evaluations/$RUN_ID"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

cd "$ROOT"
test -x "$UNITY" || fail "Unity 6000.0.74f1 is not installed"
test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
test -f "$NATIVE_CONTRACT" || fail "Frozen Stage 5 native contract is missing"
test -f "$MODEL_MANIFEST" || fail "Passing Stage 5 split model manifest is missing"
pgrep -f "Unity.app/Contents/MacOS/Unity.*-project[Pp]ath $PROJECT" >/dev/null &&
  fail "Close the Unity editor for this project before running Stage 6"

echo "Stage 6: Python contracts"
.venv/bin/python -m pytest -q

echo "Stage 6: preparing isolated assets"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage6ProjectBuilder.PrepareProject \
  -logFile "$ROOT/docs/stage6-prepare.log"

echo "Stage 6: Unity EditMode"
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform EditMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage6HumanShotTests \
  -testResults "$ROOT/docs/stage6-editmode-results.xml" \
  -logFile "$ROOT/docs/stage6-editmode.log"

echo "Stage 6: Unity PlayMode"
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform PlayMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage6HumanShotPlayModeTests \
  -testResults "$ROOT/docs/stage6-playmode-results.xml" \
  -logFile "$ROOT/docs/stage6-playmode.log"

echo "Stage 6: macOS build"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage6ProjectBuilder.BuildMac \
  -logFile "$ROOT/docs/stage6-macos-build.log"

POLICIES=(
  --policy stand_center_v1
  --policy random_hybrid_v1
  --policy reactive_curve_v1
  --policy "native_split_v1:$MODEL_MANIFEST"
)

echo "Stage 6: 16 x 4 integration smoke"
arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark "$CONFIG" \
  --build "$BUILD" \
  "${POLICIES[@]}" \
  --attempts-per-arena 4 \
  --run-id stage6-human-shot-v1-smoke-64

echo "Stage 6: fixed 2,000-shot pre-training evaluation"
arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark "$CONFIG" \
  --build "$BUILD" \
  "${POLICIES[@]}" \
  --run-id "$RUN_ID" \
  --canonical-report "$ROOT/docs/stage6-pretraining-baseline-report.json"

echo "Stage 6 pre-training evaluation complete. No training was started."
cat "$OUTPUT/summary.md"
