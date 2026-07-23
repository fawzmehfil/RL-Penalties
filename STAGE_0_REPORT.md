# Stage 0 completion report

Stage 0 freezes and verifies the penalty-shot physics foundation. It contains
no trainable goalkeeper or shooter policy.

## Pinned stack

| Component | Version |
|---|---:|
| Unity | 6000.0.74f1 |
| Unity ML-Agents package | 4.0.0 |
| Python | 3.10.12 |
| `mlagents` | 1.1.0 |
| `mlagents-envs` | 1.1.0 |

Unity GUI and batch-mode licensing were both authenticated. The project uses
the 3D URP template. Package pins are stored in
`unity/Packages/manifest.json` and `unity/Packages/packages-lock.json`.

## Verified results

- Edit Mode: 9/9 tests passed.
- Play Mode: 1/1 acceptance test passed.
- Canonical acceptance: 1,000/1,000 attempts terminated as goals.
- Invalid outcomes: 0.
- Duplicate terminal events: 0.
- Maximum goal-plane target error: 0.000490 m (limit: 0.050 m).
- macOS protocol probe: 3/3 independent launches passed.
- Each probe observed the 8-float observation, one-branch no-op action,
  28 decisions, a terminal step, and clean shutdown.
- Linux Mono support was installed through Unity Hub.
- Linux x86_64 headless player build succeeded (254,503,053 bytes).

The Linux binary is compile-verified on this Mac. Runtime protocol execution is
performed by the same probe on a Linux host; Linux executables cannot run
natively on macOS.

## Key semantics

A goal occurs only when the whole ball has crossed the goal plane and the ball
fits fully inside the goal interior. Contact with a glove, body, post, or
crossbar never decides the result by itself. Position crossing is swept from
the previous physics state to the current state so fast shots cannot skip the
detector.

Evidence files:

- `docs/stage0-test-summary.json`
- `docs/stage0-acceptance.json`
- `docs/stage0-macos-connection-report.json`

Run `scripts/setup_python.sh`, then `scripts/verify_stage0.sh` to reproduce the
local environment checks.
