#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.0.74f1/Unity.app/Contents/MacOS/Unity"
BUILD="$PROJECT_ROOT/builds/macos/PenaltyShootoutStage5.app"
BENCHMARK="$PROJECT_ROOT/configs/benchmarks/goalkeeper-control-v2-id-20k.json"
CONTRACT="$PROJECT_ROOT/configs/inference/goalkeeper-control-v2-split-native-v1.json"
MODEL_MANIFEST="$PROJECT_ROOT/results/supervision/goalkeeper-control-v2-split-v2/seed-001/model-manifest.json"
SMOKE_RUN="gk-control-v2-native-split-v1-smoke-64"
GATE_RUN="gk-control-v2-native-split-v1-gate-400"

cd "$PROJECT_ROOT"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

test -x "$UNITY" || fail "Unity 6000.0.74f1 is not installed"
test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
test -f "$MODEL_MANIFEST" || fail "Missing passing Stage 5.6A model manifest"
test -f "$CONTRACT" || fail "Missing Stage 5.6B native contract"
pgrep -f \
  "Unity.app/Contents/MacOS/Unity.*-projectpath $PROJECT_ROOT/unity" \
  >/dev/null &&
  fail "Close the Unity editor for this project before the handoff"

echo "Stage 5.6B: validating package, evaluator, and evidence gates"
arch -x86_64 .venv/bin/python -m pytest \
  python/tests/test_stage5_native_inference.py \
  python/tests/test_stage5_split_supervision.py \
  python/tests/test_stage5_control.py \
  -q

echo "Stage 5.6B: importing selected ONNX models and preparing the arena"
"$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT_ROOT/unity" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage5ProjectBuilder.PrepareProject \
  -logFile "$PROJECT_ROOT/docs/stage5-native-prepare.log"

echo "Stage 5.6B: running focused Unity contract tests"
"$UNITY" \
  -batchmode \
  -nographics \
  -projectPath "$PROJECT_ROOT/unity" \
  -runTests \
  -testPlatform PlayMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage5ControlPlayModeTests \
  -testResults "$PROJECT_ROOT/docs/stage5-native-playmode-results.xml" \
  -logFile "$PROJECT_ROOT/docs/stage5-native-playmode.log"

echo "Stage 5.6B: building the native-inference-capable macOS player"
"$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT_ROOT/unity" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage5ProjectBuilder.BuildMacHeadless \
  -logFile "$PROJECT_ROOT/docs/stage5-native-macos-build.log"

"$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT_ROOT/unity" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage5ProjectBuilder.BuildMacNativeInference \
  -logFile "$PROJECT_ROOT/docs/stage5-native-standalone-build.log"

echo "Stage 5.6B: running 16 x 4 native shadow-parity smoke test"
arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark "$BENCHMARK" \
  --build "$BUILD" \
  --policy "split_supervised:$MODEL_MANIFEST" \
  --policy "native_split_v1:$MODEL_MANIFEST" \
  --attempts-per-arena 4 \
  --worker-id-start 620 \
  --run-id "$SMOKE_RUN"

echo "Stage 5.6B: running fixed 400-shot native promotion gate"
arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark "$BENCHMARK" \
  --build "$BUILD" \
  --policy "split_supervised:$MODEL_MANIFEST" \
  --policy "native_split_v1:$MODEL_MANIFEST" \
  --attempts-per-arena 25 \
  --worker-id-start 630 \
  --run-id "$GATE_RUN"

arch -x86_64 .venv/bin/python -m \
  penalty_shootout.training.stage5_native_inference \
  --evaluation \
  "$PROJECT_ROOT/results/evaluations/$GATE_RUN/report.json"

echo "PASS: Stage 5.6B native Unity inference matched the Python reference."
echo "PPO was not started. Evidence: docs/stage5-native-inference-report.json"
