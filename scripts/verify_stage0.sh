#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.0.74f1/Unity.app/Contents/MacOS/Unity"
cd "$PROJECT_ROOT"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

test -x "$UNITY" || fail "Unity 6000.0.74f1 is not installed"
test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
command -v git-lfs >/dev/null || fail "Git LFS is not installed"
git branch --show-current | grep -qx main || fail "Current Git branch is not main"

grep -q '"com.unity.ml-agents": "4.0.0"' unity/Packages/manifest.json ||
  fail "Unity ML-Agents is not pinned to 4.0.0"
grep -q '"version": "4.0.0"' unity/Packages/packages-lock.json ||
  fail "Unity package lock does not contain ML-Agents 4.0.0"

arch -x86_64 .venv/bin/python - <<'PY'
import json
import platform
from importlib.metadata import version
from pathlib import Path

assert platform.python_version() == "3.10.12"
assert version("mlagents") == "1.1.0"
assert version("mlagents-envs") == "1.1.0"

physics = json.loads(Path("configs/physics/physics-v0.json").read_text())
assert physics["physics"]["fixed_timestep_s"] == 0.02
assert physics["goal"]["inside_width_m"] == 7.32
assert physics["goal"]["crossbar_lower_edge_m"] == 2.44
assert physics["ball"]["radius_m"] == 0.11
assert physics["ball"]["mass_kg"] == 0.43

tests = json.loads(Path("docs/stage0-test-summary.json").read_text())
assert tests["edit_mode"] == {"passed": 9, "failed": 0}
assert tests["play_mode"] == {"passed": 1, "failed": 0}

acceptance = json.loads(Path("docs/stage0-acceptance.json").read_text())
assert acceptance["passed"] is True
assert acceptance["terminal_attempts"] == 1000
assert acceptance["invalid_outcomes"] == 0

connection = json.loads(
    Path("docs/stage0-macos-connection-report.json").read_text()
)
assert connection["all_passed"] is True and connection["repeats"] >= 3
PY

test -x builds/linux/PenaltyShootoutStage0.x86_64 ||
  fail "Linux headless build is missing"

if rg -l 'class .*Goalkeeper.*: Agent|class .*Shooter.*: Agent' \
  unity/Assets/PenaltyShootout >/dev/null; then
  fail "Stage 0 contains premature goalkeeper or shooter training code"
fi

git lfs env >/dev/null
echo "Stage 0 verification passed."
