#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/builds/macos/PenaltyShootoutFinal.app"
test -d "$APP" || {
  echo "Stage 9 build is missing. Run scripts/build_stage9_final.sh first." >&2
  exit 1
}
open "$APP"
