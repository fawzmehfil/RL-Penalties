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
  -executeMethod PenaltyShootout.Stage0.Editor.Stage5ProjectBuilder.PrepareProject \
  -logFile "$PROJECT_ROOT/docs/stage5-prepare.log"

"$UNITY" \
  -batchmode \
  -nographics \
  -projectPath "$PROJECT_ROOT/unity" \
  -runTests \
  -testPlatform EditMode \
  -testResults "$PROJECT_ROOT/docs/stage5-editmode-results.xml" \
  -logFile "$PROJECT_ROOT/docs/stage5-editmode.log"

"$UNITY" \
  -batchmode \
  -nographics \
  -projectPath "$PROJECT_ROOT/unity" \
  -runTests \
  -testPlatform PlayMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage5ControlPlayModeTests \
  -testResults "$PROJECT_ROOT/docs/stage5-playmode-results.xml" \
  -logFile "$PROJECT_ROOT/docs/stage5-playmode.log"

"$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT_ROOT/unity" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage5ProjectBuilder.BuildMacHeadless \
  -logFile "$PROJECT_ROOT/docs/stage5-macos-build.log"

arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark configs/benchmarks/goalkeeper-control-v1-id-20k.json \
  --build builds/macos/PenaltyShootoutStage5.app \
  --policy stand_center_v1 \
  --policy random_hybrid_v1 \
  --policy reactive_reach_v1 \
  --attempts-per-arena 4 \
  --run-id stage5-control-smoke \
  --canonical-report docs/stage5-control-benchmark-report.json \
  > "$PROJECT_ROOT/docs/stage5-smoke.log" \
  2>&1

arch -x86_64 .venv/bin/python -m pytest -q
echo "Stage 5 verification passed."
