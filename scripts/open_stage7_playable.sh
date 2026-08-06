#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/builds/macos/PenaltyShootoutStage7.app"

if [[ ! -d "$APP" ]]; then
  echo "Stage 7 build is missing. Run scripts/run_stage7_vertical_slice_handoff.sh first." >&2
  exit 1
fi

open -n "$APP"
