#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

SEED="${1:-1}"
[[ "$SEED" =~ ^[0-9]+$ ]] || fail "Seed must be a non-negative integer"
printf -v SEED_LABEL "%03d" "$SEED"
RUN_ID="${2:-gk-control-v1_reach-v4-policy-faithful_seed-$SEED_LABEL}"
EVALUATION_RUN_ID="${RUN_ID}-checkpoint-screen-400"
TRAINING_DIR="$PROJECT_ROOT/results/$RUN_ID"
EVALUATION_DIR="$PROJECT_ROOT/results/evaluations/$EVALUATION_RUN_ID"
BUILD="$PROJECT_ROOT/builds/macos/PenaltyShootoutStage5.app"

test -x .venv/bin/mlagents-learn ||
  fail "Run scripts/setup_python.sh first"
test -d "$BUILD" ||
  fail "Missing Stage 5 build. Run scripts/verify_stage5_reach_v4.sh first"
test ! -e "$TRAINING_DIR" ||
  fail "Training run already exists: $TRAINING_DIR"
test ! -e "$EVALUATION_DIR" ||
  fail "Evaluation run already exists: $EVALUATION_DIR"
pgrep -f "mlagents-learn|PenaltyShootoutStage5|penalty_shootout.evaluation.goalkeeper" >/dev/null &&
  fail "Another Stage 5 training or evaluation process is running"

echo "Training Stage 5.4 diagnostic: $RUN_ID"
arch -x86_64 .venv/bin/mlagents-learn \
  configs/training/goalkeeper-control-v1-ppo-reach-v4-diagnostic.yaml \
  --env "$BUILD" \
  --run-id "$RUN_ID" \
  --seed "$SEED" \
  --no-graphics

CHECKPOINT_DIR="$TRAINING_DIR/GoalkeeperControl-v1"
test -d "$CHECKPOINT_DIR" ||
  fail "Training completed without a checkpoint directory"

POLICY_ARGS=(
  --policy stand_center_v1
  --policy random_hybrid_v1
  --policy reactive_reach_v1
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
for path in root.glob("GoalkeeperControl-v1-*.onnx"):
    match = re.search(r"-(\d+)\.onnx$", path.name)
    if match:
        candidates.append((int(match.group(1)), path.resolve()))
if not candidates:
    raise SystemExit("No ONNX checkpoints found")

selected = []
for target in (200_000, 400_000, 600_000, 800_000, 1_000_000):
    choice = min(candidates, key=lambda item: abs(item[0] - target))
    if choice[1] not in selected:
        selected.append(choice[1])
for path in selected:
    print(path)
PY
)

echo "Screening retained checkpoints on 400 fixed shots each"
arch -x86_64 .venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark configs/benchmarks/goalkeeper-control-v1-id-20k.json \
  --build "$BUILD" \
  "${POLICY_ARGS[@]}" \
  --attempts-per-arena 25 \
  --worker-id-start "$((200 + SEED * 10))" \
  --run-id "$EVALUATION_RUN_ID"

echo
cat "$EVALUATION_DIR/summary.md"
