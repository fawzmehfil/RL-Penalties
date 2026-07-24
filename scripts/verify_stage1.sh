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

grep -q '"com.unity.ml-agents": "4.0.0"' unity/Packages/manifest.json ||
  fail "Unity ML-Agents is not pinned to 4.0.0"

"$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT_ROOT/unity" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage1ProjectBuilder.PrepareProject \
  -logFile "$PROJECT_ROOT/docs/stage1-prepare.log"

"$UNITY" \
  -batchmode \
  -nographics \
  -projectPath "$PROJECT_ROOT/unity" \
  -runTests \
  -testPlatform EditMode \
  -testResults "$PROJECT_ROOT/docs/stage1-editmode-results.xml" \
  -logFile "$PROJECT_ROOT/docs/stage1-editmode.log"

"$UNITY" \
  -batchmode \
  -nographics \
  -projectPath "$PROJECT_ROOT/unity" \
  -runTests \
  -testPlatform PlayMode \
  -testResults "$PROJECT_ROOT/docs/stage1-playmode-results.xml" \
  -logFile "$PROJECT_ROOT/docs/stage1-playmode.log"

"$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT_ROOT/unity" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage1ProjectBuilder.BuildMacHeadless \
  -logFile "$PROJECT_ROOT/docs/stage1-macos-build.log"

arch -x86_64 .venv/bin/python -m penalty_shootout.stage1_probe \
  --build builds/macos/PenaltyShootoutStage1.app \
  --platform macos \
  --repeats 3 \
  --report docs/stage1-macos-connection-report.json

arch -x86_64 .venv/bin/python -m pytest -q
echo "Stage 1 verification passed."
