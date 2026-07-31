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

TRAINING_RUN_ID="${2:-gk-control-v2-bc-bootstrap_seed-$SEED_LABEL}"
BASE_EVALUATION_RUN_ID="${TRAINING_RUN_ID}-checkpoint-screen-400"
if [[ -n "${3:-}" ]]; then
  EVALUATION_RUN_ID="$3"
elif [[ -e "$PROJECT_ROOT/results/evaluations/$BASE_EVALUATION_RUN_ID" ]]; then
  EVALUATION_RUN_ID="${BASE_EVALUATION_RUN_ID}-resume-$(date +%Y%m%d-%H%M%S)"
else
  EVALUATION_RUN_ID="$BASE_EVALUATION_RUN_ID"
fi

DEMO_DIR="$PROJECT_ROOT/results/demonstrations/goalkeeper-control-v2-reactive-demo-v1-20k"
TRAINING_DIR="$PROJECT_ROOT/results/$TRAINING_RUN_ID"
CHECKPOINT_DIR="$TRAINING_DIR/GoalkeeperControl-v2"
EVALUATION_DIR="$PROJECT_ROOT/results/evaluations/$EVALUATION_RUN_ID"
TRAINING_BUILD="$PROJECT_ROOT/builds/macos/PenaltyShootoutStage5.app"
NO_BC_CHECKPOINT="$PROJECT_ROOT/results/gk-control-v2-lifecycle-ballistics_seed-001/GoalkeeperControl-v2/GoalkeeperControl-v2-149997.onnx"
WORKER_ID_START="${STAGE5_EVAL_WORKER_ID_START:-1200}"

[[ "$WORKER_ID_START" =~ ^[0-9]+$ ]] ||
  fail "STAGE5_EVAL_WORKER_ID_START must be a non-negative integer"
test -d "$TRAINING_BUILD" ||
  fail "Missing Stage 5 build. Run scripts/verify_stage5_imitation.sh first"
test -f "$DEMO_DIR/manifest.json" ||
  fail "Missing validated demonstration manifest: $DEMO_DIR/manifest.json"
test -d "$CHECKPOINT_DIR" ||
  fail "Missing BC checkpoint directory: $CHECKPOINT_DIR"
test -f "$NO_BC_CHECKPOINT" ||
  fail "Missing no-BC comparison checkpoint: $NO_BC_CHECKPOINT"
test ! -e "$EVALUATION_DIR" ||
  fail "Evaluation run already exists: $EVALUATION_DIR"
pgrep -f "PenaltyShootoutStage5|penalty_shootout.evaluation.goalkeeper" \
  >/dev/null && fail "Another Stage 5 evaluation process is running"

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

POLICY_COUNT=$((${#POLICY_ARGS[@]} / 2))
arch -x86_64 .venv/bin/python - \
  "$WORKER_ID_START" "$POLICY_COUNT" <<'PY'
import socket
import sys

worker_id_start = int(sys.argv[1])
policy_count = int(sys.argv[2])
base_port = 5005
busy = []
for worker_id in range(worker_id_start, worker_id_start + policy_count):
    port = base_port + worker_id
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        sock.bind(("127.0.0.1", port))
    except OSError:
        busy.append((worker_id, port))
    finally:
        sock.close()

if busy:
    formatted = ", ".join(
        f"worker {worker_id} (port {port})"
        for worker_id, port in busy
    )
    raise SystemExit(
        "Evaluation worker range is unavailable: " + formatted + ". "
        "Set STAGE5_EVAL_WORKER_ID_START to a free range."
    )
PY

echo "Screening $POLICY_COUNT policies on 400 fixed shots each"
echo "Evaluation run: $EVALUATION_RUN_ID"
echo "Worker range: $WORKER_ID_START-$((WORKER_ID_START + POLICY_COUNT - 1))"
arch -x86_64 .venv/bin/python \
  -m penalty_shootout.evaluation.goalkeeper \
  --benchmark configs/benchmarks/goalkeeper-control-v2-bc-id-20k.json \
  --build "$TRAINING_BUILD" \
  "${POLICY_ARGS[@]}" \
  --attempts-per-arena 25 \
  --worker-id-start "$WORKER_ID_START" \
  --run-id "$EVALUATION_RUN_ID"

arch -x86_64 .venv/bin/python \
  -m penalty_shootout.training.stage5_imitation_evidence \
  --manifest "$DEMO_DIR/manifest.json" \
  --evaluation-report "$EVALUATION_DIR/report.json" \
  --training-run-id "$TRAINING_RUN_ID" \
  --seed "$SEED"

echo
cat "$EVALUATION_DIR/summary.md"
