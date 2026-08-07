#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.0.74f1/Unity.app/Contents/MacOS/Unity"
PROJECT="$ROOT/unity"
RESULTS="$ROOT/results/stage9-final"

cd "$ROOT"
mkdir -p "$RESULTS"
scripts/prepare_stage9_final.sh

echo "Stage 9: explicit frozen-geometry validation"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage9ProjectBuilder.ValidateGeometryMenu \
  -logFile "$RESULTS/geometry.log"

echo "Stage 9: Stage 7 gameplay regression"
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform EditMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage7GameplayTests \
  -testResults "$RESULTS/stage7-regression-editmode.xml" \
  -logFile "$RESULTS/stage7-regression-editmode.log"
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform PlayMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage7PlayablePlayModeTests \
  -testResults "$RESULTS/stage7-regression-playmode.xml" \
  -logFile "$RESULTS/stage7-regression-playmode.log"

echo "Stage 9: paired 400-shot Stage 7/Stage 9 bounded gameplay parity"
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform PlayMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage9SimulationInvariancePlayModeTests \
  -testResults "$RESULTS/stage9-simulation-invariance.xml" \
  -logFile "$RESULTS/stage9-simulation-invariance.log"

echo "Stage 9 technical verification passed."
echo "Manual visual and audio approval remains required before commit."
