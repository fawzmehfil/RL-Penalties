#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.0.74f1/Unity.app/Contents/MacOS/Unity"
PROJECT="$ROOT/unity"
BUILD="$ROOT/builds/macos/PenaltyShootoutStage6.app"
DELAY2_CONFIG="$ROOT/configs/benchmarks/goalkeeper-control-v2-human-shot-v1-pretraining-2k.json"
DELAY0_CONFIG="$ROOT/configs/benchmarks/goalkeeper-control-v2-human-shot-v1-delay0-audit-400.json"
CONTACT_CONFIG="$ROOT/configs/benchmarks/goalkeeper-control-v2-human-shot-v1-contact-audit-400.json"
CANONICAL_CONFIG="$ROOT/configs/benchmarks/goalkeeper-control-v2-forward-canonical-audit-400.json"
CANONICAL_CONTACT_CONFIG="$ROOT/configs/benchmarks/goalkeeper-control-v2-contact-canonical-audit-400.json"
HIGH_CONFIG="$ROOT/configs/benchmarks/goalkeeper-control-v2-high-forward-contact-2k.json"
HIGH_CONTACT_CONFIG="$ROOT/configs/benchmarks/goalkeeper-control-v2-contact-high-audit-400.json"
AUDIT_CONFIG="$ROOT/configs/audits/stage6-gameplay-readiness-v1.json"
MODEL_MANIFEST="$ROOT/results/supervision/goalkeeper-control-v2-split-v2/seed-001/model-manifest.json"
BASELINE="$ROOT/results/evaluations/stage6-human-shot-v1-pretraining-2k/episodes.csv"
DELAY2_RUN="stage6-gameplay-readiness-delay2-400"
DELAY0_RUN="stage6-gameplay-readiness-delay0-400"
CONTACT_RUN="stage6-gameplay-readiness-contact-candidate-400"
CANONICAL_RUN="stage6-gameplay-readiness-canonical-forward-baseline-400"
CANONICAL_CONTACT_RUN="stage6-gameplay-readiness-canonical-contact-400"
HIGH_RUN="stage6-gameplay-readiness-high-baseline-400"
HIGH_CONTACT_RUN="stage6-gameplay-readiness-high-contact-400"
OUTPUT="$ROOT/results/evaluations/stage6-gameplay-readiness-v1"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

cd "$ROOT"
test -x "$UNITY" || fail "Unity 6000.0.74f1 is not installed"
test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
test -f "$MODEL_MANIFEST" || fail "Stage 5 native model manifest is missing"
test -f "$BASELINE" || fail "Run scripts/run_stage6_pretraining_baseline.sh first"
pgrep -f "Unity.app/Contents/MacOS/Unity.*-project[Pp]ath $PROJECT" >/dev/null &&
  fail "Close the Unity editor for this project before running the audit"

echo "Stage 6.1: focused Python verification"
.venv/bin/python -m pytest -q \
  python/tests/test_stage6_human_shots.py \
  python/tests/test_stage6_gameplay_readiness.py

echo "Stage 6.1: preparing Unity assets"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage6ProjectBuilder.PrepareProject \
  -logFile "$ROOT/docs/stage6-gameplay-readiness-prepare.log"

echo "Stage 6.1: Unity motor and shot contracts"
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform EditMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage6HumanShotTests \
  -testResults "$ROOT/docs/stage6-gameplay-readiness-editmode-results.xml" \
  -logFile "$ROOT/docs/stage6-gameplay-readiness-editmode.log"

echo "Stage 6.1: rebuilding macOS evaluator"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage6ProjectBuilder.BuildMac \
  -logFile "$ROOT/docs/stage6-gameplay-readiness-build.log"

echo "Stage 6.1: fixed 400-shot delayed matrix"
arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark "$DELAY2_CONFIG" \
  --build "$BUILD" \
  --policy reactive_curve_v1 \
  --policy reactive_motor_v1 \
  --policy "native_split_v1:$MODEL_MANIFEST" \
  --attempts-per-arena 25 \
  --run-id "$DELAY2_RUN"

echo "Stage 6.1: fixed 400-shot zero-delay native control"
arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark "$DELAY0_CONFIG" \
  --build "$BUILD" \
  --policy "native_split_v1:$MODEL_MANIFEST" \
  --run-id "$DELAY0_RUN"

echo "Stage 6.1: fixed 400-shot audit-only glove contact candidate"
arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark "$CONTACT_CONFIG" \
  --build "$BUILD" \
  --policy "native_split_v1:$MODEL_MANIFEST" \
  --run-id "$CONTACT_RUN"

echo "Stage 6.1: rebuilding canonical evaluator"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage5ProjectBuilder.BuildMacHeadless \
  -logFile "$ROOT/docs/stage6-gameplay-readiness-stage5-build.log"

for spec in \
  "$CANONICAL_CONFIG|$CANONICAL_RUN" \
  "$CANONICAL_CONTACT_CONFIG|$CANONICAL_CONTACT_RUN" \
  "$HIGH_CONFIG|$HIGH_RUN" \
  "$HIGH_CONTACT_CONFIG|$HIGH_CONTACT_RUN"
do
  benchmark="${spec%%|*}"
  run_id="${spec##*|}"
  extra=()
  if [[ "$benchmark" == "$HIGH_CONFIG" ]]; then
    extra=(--attempts-per-arena 25)
  fi
  arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
    --benchmark "$benchmark" \
    --build "$ROOT/builds/macos/PenaltyShootoutStage5.app" \
    --policy "native_split_v1:$MODEL_MANIFEST" \
    "${extra[@]}" \
    --run-id "$run_id"
done

echo "Stage 6.1: paired diagnosis and replay manifest"
.venv/bin/python -m penalty_shootout.evaluation.stage6_gameplay_readiness \
  --config "$AUDIT_CONFIG" \
  --delayed-csv "$ROOT/results/evaluations/$DELAY2_RUN/episodes.csv" \
  --zero-delay-csv "$ROOT/results/evaluations/$DELAY0_RUN/episodes.csv" \
  --baseline-csv "$BASELINE" \
  --contact-candidate-csv "$ROOT/results/evaluations/$CONTACT_RUN/episodes.csv" \
  --canonical-baseline-csv "$ROOT/results/evaluations/$CANONICAL_RUN/episodes.csv" \
  --canonical-candidate-csv "$ROOT/results/evaluations/$CANONICAL_CONTACT_RUN/episodes.csv" \
  --high-baseline-csv "$ROOT/results/evaluations/$HIGH_RUN/episodes.csv" \
  --high-candidate-csv "$ROOT/results/evaluations/$HIGH_CONTACT_RUN/episodes.csv" \
  --output-dir "$OUTPUT" \
  --canonical-report "$ROOT/docs/stage6-gameplay-readiness-report.json"

echo "Stage 6.1 audit complete. No training was started."
