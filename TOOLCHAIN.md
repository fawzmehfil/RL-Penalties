# Pinned toolchain

Stage 0 uses the following exact versions:

| Component | Version |
|---|---:|
| Unity Editor | `6000.0.74f1` |
| Unity ML-Agents package | `4.0.0` |
| Python | `3.10.12` |
| `mlagents` | `1.1.0` |
| `mlagents_envs` | `1.1.0` |
| `uv` | `0.11.31` |
| Git LFS | `3.7.1` |

The Unity editor version is also recorded by
`unity/ProjectSettings/ProjectVersion.txt`. Unity package resolution is locked
by `unity/Packages/packages-lock.json`. Python resolution is locked by
`uv.lock`.

The operating-system Python is intentionally unused. ML-Agents Release 23
requires Python 3.10.1 through 3.10.12, so commands must use `.venv/bin/python`
or run through `uv run`. On Apple silicon, setup uses uv-managed x86_64 Python
under Rosetta because pinned `grpcio==1.48.2` has no arm64 macOS wheel.
