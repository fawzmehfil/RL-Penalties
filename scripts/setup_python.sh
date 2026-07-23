#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"

command -v uv >/dev/null || {
  echo "uv is required. Install it with: brew install uv" >&2
  exit 1
}

# grpcio 1.48.2 has an x86_64 macOS wheel but no arm64 wheel.
uv python install cpython-3.10.12-macos-x86_64-none
uv venv --clear --python cpython-3.10.12-macos-x86_64-none .venv
uv sync --python .venv/bin/python --all-extras

arch -x86_64 .venv/bin/python -c \
  'import platform; assert platform.python_version() == "3.10.12"; print(platform.python_version(), platform.machine())'
