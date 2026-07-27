#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.0.74f1/Unity.app/Contents/MacOS/Unity"
cd "$PROJECT_ROOT"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

test -x "$UNITY" || fail "Unity 6000.0.74f1 is not installed"
test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
pgrep -f "Unity.app/Contents/MacOS/Unity.*-projectpath $PROJECT_ROOT/unity" >/dev/null &&
  fail "Close the Unity editor for this project before running full verification"

"$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT_ROOT/unity" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage4ProjectBuilder.PrepareProject \
  -logFile "$PROJECT_ROOT/docs/stage4-prepare.log"

"$UNITY" \
  -batchmode \
  -nographics \
  -projectPath "$PROJECT_ROOT/unity" \
  -runTests \
  -testPlatform EditMode \
  -testResults "$PROJECT_ROOT/docs/stage4-editmode-results.xml" \
  -logFile "$PROJECT_ROOT/docs/stage4-editmode.log"

"$UNITY" \
  -batchmode \
  -nographics \
  -projectPath "$PROJECT_ROOT/unity" \
  -runTests \
  -testPlatform PlayMode \
  -testResults "$PROJECT_ROOT/docs/stage4-playmode-results.xml" \
  -logFile "$PROJECT_ROOT/docs/stage4-playmode.log"

"$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT_ROOT/unity" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage4ProjectBuilder.BuildMacHeadless \
  -logFile "$PROJECT_ROOT/docs/stage4-macos-build.log"

arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark configs/benchmarks/goalkeeper-robust-v0-id-20k.json \
  --build builds/macos/PenaltyShootoutStage4.app \
  --policy stand_center \
  --policy random_legal \
  --policy onnx:results/gk-state-v0_ppo_seed-001/GoalkeeperState-v0/GoalkeeperState-v0-5000019.onnx \
  --attempts-per-arena 4 \
  --run-id stage4-robust-smoke \
  --canonical-report docs/stage4-robustness-report.json \
  > "$PROJECT_ROOT/docs/stage4-smoke.log" \
  2>&1

arch -x86_64 .venv/bin/python -m pytest -q
echo "Stage 4 verification passed."
