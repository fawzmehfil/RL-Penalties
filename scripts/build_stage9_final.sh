#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.0.74f1/Unity.app/Contents/MacOS/Unity"
PROJECT="$ROOT/unity"
BUILD="$ROOT/builds/macos/PenaltyShootoutFinal.app"
RESULTS="$ROOT/results/stage9-final"

cd "$ROOT"
mkdir -p "$RESULTS"
test -x "$UNITY" || { echo "Unity 6000.0.74f1 is not installed" >&2; exit 1; }
pgrep -f "Unity.app/Contents/MacOS/Unity.*-project[Pp]ath $PROJECT" >/dev/null && {
  echo "Close the Unity editor for this project before building" >&2
  exit 1
}

"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage9ProjectBuilder.BuildMac \
  -logFile "$RESULTS/macos-build.log"

test -d "$BUILD" || { echo "Stage 9 app was not produced" >&2; exit 1; }
echo "Stage 9 local build created: $BUILD"
echo "No signing, notarization, upload, or launch was performed."
