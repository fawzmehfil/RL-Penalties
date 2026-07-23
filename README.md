# RL Penalties

An open-source reinforcement-learning benchmark for isolated football penalty
shootouts, built with Unity and Unity ML-Agents.

The finished system is intended to support a trainable goalkeeper, a trainable
shooter, and goalkeeper-versus-shooter self-play—not merely a scripted penalty
game. The benchmark will target reaction time, partial observability, opponent
modelling, generalization, reward design, and self-play while remaining
visually clear enough for interactive demonstrations.

## Project direction

Development follows a staged curriculum:

1. Establish deterministic, testable ball and goal physics.
2. Train a goalkeeper against increasingly varied procedural shots.
3. Add curved and deceptive shots.
4. Train a shooter controlling placement, power, timing, and curve.
5. Train goalkeeper and shooter policies through self-play.

Agents use meaningful high-level actions rather than learning humanoid joint
control from scratch. Planned public deliverables include headless training,
scripted baselines, fixed evaluation suites, leaderboards, replay
visualizations, human-versus-agent play, and ML-Agents/Gym-compatible APIs.

## Current status: Stage 0 complete

Stage 0 establishes the physics and tooling foundation before goalkeeper
training:

- Unity `6000.0.74f1` using URP.
- Unity ML-Agents package `4.0.0`.
- Python `3.10.12` with `mlagents` and `mlagents_envs` `1.1.0`.
- A `PhysicsLab` scene with an analytical canonical shot.
- Swept, whole-ball goal detection.
- Goal, wide miss, high miss, timeout, and invalid-state outcomes.
- Automated reset and geometry tests.
- macOS Unity-to-Python connection verification.
- macOS and Linux headless builds.

Stage 0 deliberately contains no trainable goalkeeper or shooter policy.

### Verified Stage 0 results

| Check | Result |
|---|---:|
| Unity Edit Mode tests | 9/9 passed |
| Unity Play Mode acceptance test | 1/1 passed |
| Canonical PhysX attempts | 1,000/1,000 terminated as goals |
| Invalid outcomes | 0 |
| Duplicate terminal outcomes | 0 |
| Maximum target error | 0.000490 m |
| Declared target tolerance | 0.050 m |
| macOS protocol launches | 3/3 passed |
| Linux x86_64 headless build | Passed |

Portable evidence is committed under `docs/` as JSON. Generated players,
training runs, local logs, Unity caches, and Python environments are excluded
from Git.

## Environment specification v0

Environment ID: `penalty-shootout-physics-v0`

### Coordinate system and geometry

The origin is the centre of the goal line on the ground:

- `x`: goalkeeper's right when facing the penalty mark.
- `y`: upward.
- `z`: from the goal line toward the penalty mark.
- Goal plane: `z = 0`.
- Goal interior: `7.32 m` wide by `2.44 m` high.
- Penalty mark: `11.00 m` from the goal line.

Dimensions follow the current
[IFAB field](https://www.theifab.com/laws/latest/the-field-of-play/) and
[ball](https://www.theifab.com/laws/latest/the-ball/) rules.

### Ball and timing

| Property | Value |
|---|---:|
| Ball radius | `0.11 m` |
| Ball mass | `0.43 kg` |
| Gravity | `(0, -9.81, 0) m/s²` |
| Linear damping | `0.0` |
| Angular damping | `0.05` |
| Collision detection | Continuous Dynamic |
| Fixed physics timestep | `0.02 s` |
| Attempt timeout | `2.0 s` |

PhysX results are evaluated with declared tolerances. Cross-platform bitwise
determinism is not promised.

### Canonical shot

```text
launch centre = (0.00, 0.11, 11.00)
target centre = (0.00, 1.20, 0.00)
flight time   = 0.55 s
spin          = disabled
noise         = disabled
```

The no-drag analytical launch velocity is:

```text
v = (target - launch - 0.5 * gravity * flight_time²) / flight_time
```

The runtime applies a half-gravity-step correction because PhysX uses
semi-implicit Euler integration.

### Goal and save semantics

A goal is awarded only when the whole ball:

1. Passes behind the goal plane (`center.z <= -ball_radius`).
2. Fits between the inside faces of the posts.
3. Fits below the lower edge of the crossbar.
4. Remains above the ground.

Previous and current ball positions are intersected with the goal plane, so a
fast shot cannot skip the detector.

Contact is an event, not an outcome. A ball that touches a glove, body, post,
or crossbar and then fully crosses the line is still a goal. Future save
attribution will require the attempt to end without a goal and evidence that
the goalkeeper prevented it.

Reset clears ball pose, linear and angular velocity, attempt timers,
goal-plane intersection state, and the terminal-outcome latch.

The machine-readable source of truth is
`configs/physics/physics-v0.json`.

## Repository layout

```text
unity/       Unity project, scenes, runtime code, and Unity tests
python/      Environment wrappers, evaluation, baselines, and probes
configs/     Physics, training, scenario, and benchmark configuration
models/      Published policy manifests; large models use Git LFS
docs/        Portable machine-readable verification evidence
scripts/     Reproducible setup and verification commands
```

## Local setup

Requirements:

| Component | Version |
|---|---:|
| Unity Editor | `6000.0.74f1` |
| Unity ML-Agents package | `4.0.0` |
| Python | `3.10.12` |
| `mlagents` | `1.1.0` |
| `mlagents_envs` | `1.1.0` |
| `uv` | `0.11.31` |
| Git LFS | `3.7.1` |

```bash
git lfs install
./scripts/setup_python.sh
./scripts/verify_stage0.sh
```

Open `unity/` with Unity `6000.0.74f1`, then open
`Assets/PenaltyShootout/Scenes/PhysicsLab.unity`.

On Apple silicon, the setup script uses uv-managed x86_64 Python under Rosetta
because the gRPC version required by ML-Agents Release 23 has no arm64 macOS
wheel. Unity and Python resolutions are locked in
`unity/Packages/packages-lock.json` and `uv.lock`.

## Next milestone: Stage 1

Stage 1 turns the physics spike into a reusable environment kernel:

- Procedural shot generation.
- A movable goalkeeper with stand, shuffle, and dive actions.
- Goalkeeper collision volumes and contact telemetry.
- Formal episode initialization and termination.
- Initial observations and scripted baselines.
- Per-attempt replay and evaluation records.

PPO training begins after the Stage 1 environment and its invariants are
stable.

## Open-source and third-party notices

This repository is licensed under the Apache License 2.0. See `LICENSE` and
`NOTICE`.

The project uses the
[Unity ML-Agents Toolkit](https://github.com/Unity-Technologies/ml-agents),
Release 23:

- `com.unity.ml-agents` `4.0.0`
- `mlagents` `1.1.0`
- `mlagents_envs` `1.1.0`
- Apache License 2.0

The upstream Soccer example is a design reference. No Soccer example source or
art was copied into Stage 0.

Unity, the Unity runtime, URP, and related packages remain subject to their
respective Unity terms and are not relicensed by this repository. Stage 0
contains only generated primitives and project-authored configuration; it
contains no third-party art, audio, club marks, or competition branding.
