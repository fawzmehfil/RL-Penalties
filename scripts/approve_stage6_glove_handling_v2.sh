#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CALIBRATION="$ROOT/results/glove-handling-v2/calibration"
REVIEWER="${1:-fawzmehfil}"

cd "$ROOT"
test -f "$CALIBRATION/frozen-profile.json" || {
  echo "Run scripts/prepare_stage6_glove_handling_v2.sh first" >&2
  exit 1
}
.venv/bin/python -m penalty_shootout.evaluation.stage6_glove_handling_v2 \
  approve \
  --frozen "$CALIBRATION/frozen-profile.json" \
  --output "$CALIBRATION/visual-approval.json" \
  --reviewer "$REVIEWER"
