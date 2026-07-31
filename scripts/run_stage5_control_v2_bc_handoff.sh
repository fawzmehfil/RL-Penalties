#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

SEED="${1:-1}"
[[ "$SEED" =~ ^[0-9]+$ ]] ||
  fail "Seed must be a non-negative integer"
printf -v SEED_LABEL "%03d" "$SEED"

DEMO_ID="goalkeeper-control-v2-reactive-demo-v1-20k"
DEMO_DIR="$PROJECT_ROOT/results/demonstrations/$DEMO_ID"
DEMO_LOG="$PROJECT_ROOT/results/demonstrations/$DEMO_ID.log"
DEMO_BUILD="$PROJECT_ROOT/builds/macos/PenaltyShootoutStage5Demo.app"
TRAINING_BUILD="$PROJECT_ROOT/builds/macos/PenaltyShootoutStage5.app"
RUN_ID="${2:-gk-control-v2-bc-bootstrap_seed-$SEED_LABEL}"
EVALUATION_RUN_ID="${RUN_ID}-checkpoint-screen-400"
TRAINING_DIR="$PROJECT_ROOT/results/$RUN_ID"
EVALUATION_DIR="$PROJECT_ROOT/results/evaluations/$EVALUATION_RUN_ID"
NO_BC_CHECKPOINT="$PROJECT_ROOT/results/gk-control-v2-lifecycle-ballistics_seed-001/GoalkeeperControl-v2/GoalkeeperControl-v2-149997.onnx"
MIN_START_FREE_KB=$((20 * 1024 * 1024))
MIN_RUNTIME_FREE_KB=$((5 * 1024 * 1024))
DEMO_STALL_TIMEOUT_SECONDS=$((45 * 60))
DEMO_POLL_SECONDS=30

available_disk_kb() {
  df -Pk "$PROJECT_ROOT" | awk 'NR == 2 { print $4 }'
}

demo_size_kb() {
  if [[ -d "$DEMO_DIR" ]]; then
    du -sk "$DEMO_DIR" 2>/dev/null | awk '{ print $1 }'
  else
    printf '0\n'
  fi
}

test -x .venv/bin/mlagents-learn ||
  fail "Run scripts/setup_python.sh first"
test -d "$DEMO_BUILD" ||
  fail "Missing demonstration build. Run scripts/verify_stage5_imitation.sh first"
test -d "$TRAINING_BUILD" ||
  fail "Missing Stage 5 build. Run scripts/verify_stage5_imitation.sh first"
test -f "$NO_BC_CHECKPOINT" ||
  fail "Missing no-BC v2 comparison checkpoint: $NO_BC_CHECKPOINT"
test ! -e "$TRAINING_DIR" ||
  fail "Training run already exists: $TRAINING_DIR"
test ! -e "$EVALUATION_DIR" ||
  fail "Evaluation run already exists: $EVALUATION_DIR"
pgrep -f \
  "mlagents-learn|PenaltyShootoutStage5|penalty_shootout.evaluation.goalkeeper" \
  >/dev/null &&
  fail "Another Stage 5 recording, training, or evaluation process is running"

FREE_KB="$(available_disk_kb)"
[[ "$FREE_KB" =~ ^[0-9]+$ ]] ||
  fail "Could not determine available disk space"
if (( FREE_KB < MIN_START_FREE_KB )); then
  fail "At least 20 GiB free is required before the unattended handoff"
fi

mkdir -p "$PROJECT_ROOT/results/demonstrations"
if [[ ! -e "$DEMO_DIR" ]]; then
  echo "Recording the canonical 20,000-attempt reactive demonstration set"
  if [[ -e "$DEMO_LOG" ]]; then
    mv "$DEMO_LOG" "$DEMO_LOG.previous-$(date +%Y%m%d-%H%M%S)"
  fi

  "$DEMO_BUILD/Contents/MacOS/Penalty Shootout RL" \
    -batchmode \
    -nographics \
    "--stage5-demo-output=$DEMO_DIR" \
    --stage5-demo-attempts-per-arena=1250 \
    --stage5-demo-master-seed=20260723 \
    -logFile "$DEMO_LOG" &
  DEMO_PID=$!
  LAST_DEMO_SIZE_KB=0
  LAST_DEMO_PROGRESS_AT="$(date +%s)"

  stop_demo_recorder() {
    if kill -0 "$DEMO_PID" 2>/dev/null; then
      kill "$DEMO_PID" 2>/dev/null || true
    fi
    wait "$DEMO_PID" 2>/dev/null || true
  }

  abort_demo_recorder() {
    local reason="$1"
    stop_demo_recorder
    fail "$reason Partial output remains at $DEMO_DIR"
  }

  trap stop_demo_recorder EXIT
  trap 'abort_demo_recorder "Demonstration recording interrupted."' INT TERM

  while kill -0 "$DEMO_PID" 2>/dev/null; do
    sleep "$DEMO_POLL_SECONDS"

    if [[ -f "$DEMO_LOG" ]] &&
      grep -q "IOException: Disk full" "$DEMO_LOG"; then
      abort_demo_recorder "Demonstration recording exhausted disk space."
    fi

    FREE_KB="$(available_disk_kb)"
    [[ "$FREE_KB" =~ ^[0-9]+$ ]] ||
      abort_demo_recorder "Could not monitor available disk space."
    if (( FREE_KB < MIN_RUNTIME_FREE_KB )); then
      abort_demo_recorder \
        "Demonstration recording stopped below 5 GiB free space."
    fi

    CURRENT_DEMO_SIZE_KB="$(demo_size_kb)"
    NOW="$(date +%s)"
    if (( CURRENT_DEMO_SIZE_KB > LAST_DEMO_SIZE_KB )); then
      LAST_DEMO_SIZE_KB="$CURRENT_DEMO_SIZE_KB"
      LAST_DEMO_PROGRESS_AT="$NOW"
    elif (( NOW - LAST_DEMO_PROGRESS_AT >= DEMO_STALL_TIMEOUT_SECONDS )); then
      abort_demo_recorder \
        "Demonstration files did not grow for 45 minutes."
    fi
  done

  if ! wait "$DEMO_PID"; then
    fail "Demonstration build exited unsuccessfully. See $DEMO_LOG"
  fi
  trap - EXIT INT TERM
else
  echo "Reusing existing demonstration directory after strict validation"
fi

arch -x86_64 .venv/bin/python \
  -m penalty_shootout.training.goalkeeper_demo \
  --demo-dir "$DEMO_DIR" \
  --contract \
    configs/demonstrations/goalkeeper-control-v2-reactive-demo-v1.json \
  --manifest "$DEMO_DIR/manifest.json"

echo "Training the 500k BC + PPO diagnostic: $RUN_ID"
arch -x86_64 .venv/bin/mlagents-learn \
  configs/training/goalkeeper-control-v2-bc-diagnostic.yaml \
  --env "$TRAINING_BUILD" \
  --run-id "$RUN_ID" \
  --seed "$SEED" \
  --no-graphics

CHECKPOINT_DIR="$TRAINING_DIR/GoalkeeperControl-v2"
test -d "$CHECKPOINT_DIR" ||
  fail "Training completed without a checkpoint directory"

POLICY_ARGS=(
  --policy stand_center_v1
  --policy random_hybrid_v1
  --policy reactive_reach_v1
  --policy "onnx:$NO_BC_CHECKPOINT"
)
while IFS= read -r CHECKPOINT; do
  POLICY_ARGS+=(--policy "onnx:$CHECKPOINT")
done < <(
  arch -x86_64 .venv/bin/python - "$CHECKPOINT_DIR" <<'PY'
import re
import sys
from pathlib import Path

root = Path(sys.argv[1])
candidates = []
for path in root.glob("GoalkeeperControl-v2-*.onnx"):
    match = re.search(r"-(\d+)\.onnx$", path.name)
    if match:
        candidates.append((int(match.group(1)), path.resolve()))
if not candidates:
    raise SystemExit("No ONNX checkpoints found")

selected = []
for target in range(50_000, 500_001, 50_000):
    choice = min(candidates, key=lambda item: abs(item[0] - target))
    if choice[1] not in selected:
        selected.append(choice[1])
for path in selected:
    print(path)
PY
)

echo "Screening BC checkpoints on 400 fixed shots each"
arch -x86_64 .venv/bin/python \
  -m penalty_shootout.evaluation.goalkeeper \
  --benchmark configs/benchmarks/goalkeeper-control-v2-bc-id-20k.json \
  --build "$TRAINING_BUILD" \
  "${POLICY_ARGS[@]}" \
  --attempts-per-arena 25 \
  --worker-id-start "$((400 + SEED * 20))" \
  --run-id "$EVALUATION_RUN_ID"

arch -x86_64 .venv/bin/python \
  -m penalty_shootout.training.stage5_imitation_evidence \
  --manifest "$DEMO_DIR/manifest.json" \
  --evaluation-report "$EVALUATION_DIR/report.json" \
  --training-run-id "$RUN_ID" \
  --seed "$SEED"

echo
cat "$EVALUATION_DIR/summary.md"
