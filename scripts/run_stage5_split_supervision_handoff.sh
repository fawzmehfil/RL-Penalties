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

CONTRACT="$PROJECT_ROOT/configs/supervision/goalkeeper-control-v2-split-supervision-v1.json"
DEMO_DIR="$PROJECT_ROOT/results/demonstrations/goalkeeper-control-v2-reactive-demo-v1-20k"
OUTPUT_DIR="$PROJECT_ROOT/results/supervision/goalkeeper-control-v2-split-v1/seed-$SEED_LABEL"
MODEL_MANIFEST="$OUTPUT_DIR/model-manifest.json"
BUILD="$PROJECT_ROOT/builds/macos/PenaltyShootoutStage5.app"
BENCHMARK="$PROJECT_ROOT/configs/benchmarks/goalkeeper-control-v2-bc-id-20k.json"
NO_BC_CHECKPOINT="$PROJECT_ROOT/results/gk-control-v2-lifecycle-ballistics_seed-001/GoalkeeperControl-v2/GoalkeeperControl-v2-149997.onnx"
RUN_PREFIX="gk-control-v2-split-supervision_seed-$SEED_LABEL"
SMOKE_RUN_ID="$RUN_PREFIX-smoke-64"
INTERCEPTION_RUN_ID="$RUN_PREFIX-interception-gate-400"
COMBINED_RUN_ID="$RUN_PREFIX-combined-gate-400"
SMOKE_REPORT="$PROJECT_ROOT/results/evaluations/$SMOKE_RUN_ID/report.json"
INTERCEPTION_REPORT="$PROJECT_ROOT/results/evaluations/$INTERCEPTION_RUN_ID/report.json"
COMBINED_REPORT="$PROJECT_ROOT/results/evaluations/$COMBINED_RUN_ID/report.json"
WORKER_ID_START="${STAGE5_SPLIT_WORKER_ID_START:-1400}"

test -x "$PROJECT_ROOT/.venv/bin/python" || fail "Run scripts/setup_python.sh first"
test -f "$CONTRACT" || fail "Missing Stage 5.6 supervision contract"
test -f "$DEMO_DIR/manifest.json" || fail "Missing validated 20k demonstrations"
test -d "$BUILD" || fail "Missing Stage 5 build. Run scripts/verify_stage5_imitation.sh first"
test -f "$NO_BC_CHECKPOINT" || fail "Missing no-BC comparison checkpoint"
test ! -e "$PROJECT_ROOT/results/evaluations/$SMOKE_RUN_ID" || fail "Smoke run already exists"
test ! -e "$PROJECT_ROOT/results/evaluations/$INTERCEPTION_RUN_ID" || fail "Interception gate run already exists"
test ! -e "$PROJECT_ROOT/results/evaluations/$COMBINED_RUN_ID" || fail "Combined gate run already exists"
[[ "$WORKER_ID_START" =~ ^[0-9]+$ ]] || fail "Worker ID must be non-negative"

pgrep -f "mlagents-learn|PenaltyShootoutStage5|penalty_shootout.evaluation.goalkeeper" \
  >/dev/null && fail "Another Stage 5 training or evaluation process is running"

arch -x86_64 .venv/bin/python - "$WORKER_ID_START" <<'PY'
import socket
import sys

start = int(sys.argv[1])
busy = []
for worker_id in range(start, start + 16):
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        sock.bind(("127.0.0.1", 5005 + worker_id))
    except OSError:
        busy.append((worker_id, 5005 + worker_id))
    finally:
        sock.close()
if busy:
    raise SystemExit(f"Stage 5.6 worker ports are busy: {busy}")
PY

echo "Stage 5.6A: validating demonstrations and training split supervised models"
if ! arch -x86_64 .venv/bin/python \
  -m penalty_shootout.training.stage5_split_supervision offline \
  --contract "$CONTRACT" \
  --demo-dir "$DEMO_DIR" \
  --output-dir "$OUTPUT_DIR" \
  --seed "$SEED"; then
  if [[ -f "$MODEL_MANIFEST" ]]; then
    arch -x86_64 .venv/bin/python \
      -m penalty_shootout.training.stage5_split_evidence \
      --model-manifest "$MODEL_MANIFEST" \
      --require-stage offline || true
  fi
  fail "Offline supervision gate failed. Unity was not launched."
fi

arch -x86_64 .venv/bin/python \
  -m penalty_shootout.training.stage5_split_evidence \
  --model-manifest "$MODEL_MANIFEST" \
  --require-stage offline

echo "Stage 5.6A: running 16 x 4 composite-policy Unity smoke gate"
arch -x86_64 .venv/bin/python \
  -m penalty_shootout.evaluation.goalkeeper \
  --benchmark "$BENCHMARK" \
  --build "$BUILD" \
  --policy "interception_teacher_timing:$MODEL_MANIFEST" \
  --policy "split_supervised:$MODEL_MANIFEST" \
  --attempts-per-arena 4 \
  --worker-id-start "$WORKER_ID_START" \
  --run-id "$SMOKE_RUN_ID"

arch -x86_64 .venv/bin/python \
  -m penalty_shootout.training.stage5_split_evidence \
  --model-manifest "$MODEL_MANIFEST" \
  --smoke-report "$SMOKE_REPORT" \
  --require-stage smoke

echo "Stage 5.6A: running Model A with teacher timing on 400 fixed shots"
arch -x86_64 .venv/bin/python \
  -m penalty_shootout.evaluation.goalkeeper \
  --benchmark "$BENCHMARK" \
  --build "$BUILD" \
  --policy reactive_reach_v1 \
  --policy "interception_teacher_timing:$MODEL_MANIFEST" \
  --attempts-per-arena 25 \
  --worker-id-start "$((WORKER_ID_START + 4))" \
  --run-id "$INTERCEPTION_RUN_ID"

arch -x86_64 .venv/bin/python \
  -m penalty_shootout.training.stage5_split_evidence \
  --model-manifest "$MODEL_MANIFEST" \
  --interception-report "$INTERCEPTION_REPORT" \
  --require-stage interception

echo "Stage 5.6A: running combined learned policy and comparisons on 400 fixed shots"
arch -x86_64 .venv/bin/python \
  -m penalty_shootout.evaluation.goalkeeper \
  --benchmark "$BENCHMARK" \
  --build "$BUILD" \
  --policy stand_center_v1 \
  --policy random_hybrid_v1 \
  --policy reactive_reach_v1 \
  --policy "onnx:$NO_BC_CHECKPOINT" \
  --policy "split_supervised:$MODEL_MANIFEST" \
  --attempts-per-arena 25 \
  --worker-id-start "$((WORKER_ID_START + 8))" \
  --run-id "$COMBINED_RUN_ID"

arch -x86_64 .venv/bin/python \
  -m penalty_shootout.training.stage5_split_evidence \
  --model-manifest "$MODEL_MANIFEST" \
  --combined-report "$COMBINED_REPORT" \
  --require-stage combined

echo
cat "$PROJECT_ROOT/results/evaluations/$COMBINED_RUN_ID/summary.md"
echo
echo "PASS: Stage 5.6 supervised gate passed. PPO was not started."
