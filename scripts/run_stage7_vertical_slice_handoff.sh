#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.0.74f1/Unity.app/Contents/MacOS/Unity"
PROJECT="$ROOT/unity"
BUILD="$ROOT/builds/macos/PenaltyShootoutStage7.app"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

cd "$ROOT"
test -x "$UNITY" || fail "Unity 6000.0.74f1 is not installed"
test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
pgrep -f "Unity.app/Contents/MacOS/Unity.*-project[Pp]ath $PROJECT" >/dev/null &&
  fail "Close the Unity editor for this project before running Stage 7"

echo "Stage 7: Python contracts and replay loader"
.venv/bin/python -m pytest -q

echo "Stage 7: preparing isolated playable assets"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage7ProjectBuilder.PrepareProject \
  -logFile "$ROOT/docs/stage7-prepare.log"

echo "Stage 7: Unity EditMode contracts"
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform EditMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage7GameplayTests \
  -testResults "$ROOT/docs/stage7-editmode-results.xml" \
  -logFile "$ROOT/docs/stage7-editmode.log"

echo "Stage 7: Unity PlayMode lifecycle and five-shot set"
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform PlayMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage7PlayablePlayModeTests \
  -testResults "$ROOT/docs/stage7-playmode-results.xml" \
  -logFile "$ROOT/docs/stage7-playmode.log"

echo "Stage 7: standalone macOS build"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage7ProjectBuilder.BuildMac \
  -logFile "$ROOT/docs/stage7-macos-build.log"

test -d "$BUILD" || fail "Stage 7 app was not produced"
echo "Stage 7 vertical slice verified: $BUILD"
echo "Launch it with: scripts/open_stage7_playable.sh"
