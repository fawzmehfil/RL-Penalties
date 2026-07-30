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
pgrep -f \
  "Unity.app/Contents/MacOS/Unity.*-projectpath $PROJECT_ROOT/unity" \
  >/dev/null &&
  fail "Close the Unity editor for this project before verification"

"$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT_ROOT/unity" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage5ProjectBuilder.PrepareProject \
  -logFile "$PROJECT_ROOT/docs/stage5-control-v2-prepare.log"

"$UNITY" \
  -batchmode \
  -nographics \
  -projectPath "$PROJECT_ROOT/unity" \
  -runTests \
  -testPlatform EditMode \
  -testResults "$PROJECT_ROOT/docs/stage5-control-v2-editmode-results.xml" \
  -logFile "$PROJECT_ROOT/docs/stage5-control-v2-editmode.log"

"$UNITY" \
  -batchmode \
  -nographics \
  -projectPath "$PROJECT_ROOT/unity" \
  -runTests \
  -testPlatform PlayMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage5ControlPlayModeTests \
  -testResults "$PROJECT_ROOT/docs/stage5-control-v2-playmode-results.xml" \
  -logFile "$PROJECT_ROOT/docs/stage5-control-v2-playmode.log"

arch -x86_64 .venv/bin/python -m pytest \
  python/tests/test_stage5_control.py \
  -q

"$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT_ROOT/unity" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage5ProjectBuilder.BuildMacHeadless \
  -logFile "$PROJECT_ROOT/docs/stage5-control-v2-macos-build.log"

echo "GoalkeeperControl-v2 verification and build passed."
