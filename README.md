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

## Current status: Stage 1 environment kernel complete

Stage 0 established the physics and tooling foundation before goalkeeper
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

Stage 1 now turns that spike into the reusable environment kernel. It exposes
goalkeeper actions with one constant transport-health sensor, but no semantic
observations, rewards, or trainable policy; those form the next versioned
contract.

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

### Stage 1 kernel

Environment ID: `penalty-shootout-kernel-v1`

The kernel adds:

- A strict attempt lifecycle:
  `Resetting -> Ready -> RunUp -> BallInFlight -> Resolving -> Terminal`.
- PCG32-seeded procedural on-target shots with independent per-arena streams.
- Targets distributed across the legal goal mouth and flight times from
  `0.38 s` to `0.85 s`.
- A compound physics goalkeeper with torso, head, articulated arm capsules,
  gloves, and legs.
- Physics-authoritative shuffle and dive macros.
- Deterministic action-conditioned two-arm reaches for every dive tier.
- Manual, scripted, and action-only ML-Agents control adapters.
- A reusable `TrainingArena` prefab and `KernelLab` demonstration scene.
- Sixteen-arena accelerated acceptance testing.
- A hashed machine-readable compatibility manifest.

The goalkeeper uses one stable discrete action branch:

| ID | Action |
|---:|---|
| 0 | Hold |
| 1 | Shuffle left |
| 2 | Shuffle right |
| 3 | Dive left low |
| 4 | Dive left middle |
| 5 | Dive left high |
| 6 | Dive right low |
| 7 | Dive right middle |
| 8 | Dive right high |

Physics runs at `50 Hz`; decisions are accepted at `25 Hz`. A dive initiates
a complete calibrated macro, and additional motion actions are masked until
recovery finishes. Animation can later consume the motor pose but will not
become the authority for collision movement.

The final pre-training motor profile is `keeper-proxy-hands-v1`. Low, middle,
and high dives automatically move both hands: reach begins at 8% of the dive,
reaches full extension at 55%, remains extended through the dive, and returns
smoothly during recovery. The leading glove reaches farther than the trailing
glove. Targets are exact left/right mirrors in the arena coordinate frame.
They depend only on the selected action and dive phase—never on ball position,
shot target, or hidden trajectory parameters. Arm capsules are capped at the
declared maximum arm length so the proxy remains visually proportional even
while the body is rolled during high dives.

This hand motion does not enlarge the RL action or observation spaces. The
policy still learns when to select one of the same nine high-level actions;
Unity executes the associated body-and-hands macro. Glove and arm colliders
remain part of the authoritative kinematic compound rigidbody, so their
deflections are genuine PhysX interactions rather than visual animation.

### Outcome semantics

Every attempt produces exactly one of:

- `Goal`
- `Saved`
- `MissWide`
- `MissHigh`
- `PostOrCrossbarOut`
- `BlockedThenOut`
- `Timeout`
- `Invalid`

Goal-line crossing has priority over prior contacts. A goalkeeper touch is
recorded but is not immediately called a save: the ball can rebound from a
glove or body and still produce `Goal`. A controlled/resting ball or the
declared post-contact safety horizon produces `Saved`; a keeper deflection
that leaves the danger region is preserved separately as `BlockedThenOut`.
Telemetry identifies left glove, right glove, arm, torso/head, and leg
contacts while preserving the aggregate goalkeeper-contact contract.

### Verified Stage 1 results

| Check | Result |
|---|---:|
| Unity Edit Mode tests | 29/29 passed |
| Unity Play Mode tests | 2/2 passed |
| Procedural kernel attempts | 10,000/10,000 terminated |
| Invalid outcomes | 0 |
| Timeout outcomes | 0 |
| Duplicate terminal outcomes | 0 |
| Action-mask violations | 0 |
| Keeper-touch-then-goal cases | 796 |
| Physical glove contacts | 539 |
| Glove-touch-then-goal cases | 211 |
| Dive actions with glove contacts | 6/6 |
| Acceptance throughput | 942 attempts/s |
| Maximum unobstructed target error | 0.000798 m |
| Declared target tolerance | 0.050 m |

All nine actions were exercised in the acceptance run and every low, middle,
and high dive family made physical ball contact. See
`docs/stage1-acceptance.json` and
`configs/environment/kernel-v1.json` for the complete evidence and manifest.

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
# Close this Unity project before running the full Stage 1 batch verification:
./scripts/verify_stage1.sh
```

Open `unity/` with Unity `6000.0.74f1`, then open
`Assets/PenaltyShootout/Scenes/KernelLab.unity`. Press Play to watch procedural
attempts. Use `A`/`D` to shuffle, `Q`/`W`/`E` for left low/middle/high dives,
and `U`/`I`/`O` for right low/middle/high dives. Dive taps are buffered until
the next legal policy decision, so short key presses are not lost between
physics ticks.

`Assets/PenaltyShootout/Scenes/PhysicsLab.unity` remains available as the
Stage 0 canonical-shot regression scene.

On Apple silicon, the setup script uses uv-managed x86_64 Python under Rosetta
because the gRPC version required by ML-Agents Release 23 has no arm64 macOS
wheel. Unity and Python resolutions are locked in
`unity/Packages/packages-lock.json` and `uv.lock`.

## Stage 2 goalkeeper trainability

Stage 2 adds the first trainable goalkeeper contract on top of the Stage 1
kernel while keeping the Stage 1 transport profile available.

Environment behavior: `GoalkeeperState-v0`

Versioned contracts:

- Observation profile: `state-v0`, a fixed 24-float vector of visible ball and
  goalkeeper state.
- Reward profile: `goalkeeper-sparse-v0`, with `+1` for `Saved` or
  `BlockedThenOut`, `-1` for `Goal`, and `0` for abnormal/non-goalkeeper-task
  terminal outcomes.
- Action profile: unchanged `goalkeeper-discrete-v0`, one discrete branch with
  the nine Stage 1 action IDs.

The `state-v0` observation manifest is committed at
`configs/environment/goalkeeper-state-v0.json`. It deliberately excludes the
requested target, future goal-plane intersection, launch velocity, sampled
flight-time parameter, and terminal outcome.

The first PPO training configuration is
`configs/training/goalkeeper-state-v0-ppo.yaml`. Its four curriculum lessons
are `Mechanics`, `HorizontalPlacement`, `Height`, and `Speed`; Unity maps the
`stage2.lesson` environment parameter to the corresponding target, timing, and
launch-delay ranges.

Scripted Stage 2 baselines:

- `StandCenter`: always holds from the reset center.
- `RandomLegal`: uniformly samples currently legal actions from the action
  mask.

Run the Stage 2 verification batch with:

```bash
./scripts/verify_stage2.sh
```

The long PPO acceptance gate remains experimental evidence rather than a unit
test: at least three training seeds must beat both scripted baselines by the
declared margin, and a known checkpoint must run in Unity inference mode. The
placeholder summary is tracked in `docs/stage2-training-summary.json` until
those runs are produced.

## Stage 3 goalkeeper benchmarking

Stage 3 adds fixed-batch evaluation for the Stage 2 `GoalkeeperState-v0`
task. The canonical in-distribution suite is
`configs/benchmarks/goalkeeper-state-v0-id-20k.json`: 16 arenas, 1,250
attempts per arena, and 20,000 total full-range on-target shots at
`stage2.lesson = 3`.

Run the Stage 3 smoke verification with:

```bash
./scripts/verify_stage3.sh
```

The benchmark runner writes raw artifacts under `results/evaluations/<run-id>/`
and a compact evidence report at
`docs/stage3-goalkeeper-benchmark-report.json`. It supports scripted policies
(`stand_center`, `random_legal`, `reactive_side`, `linear_intercept`) and
exported ML-Agents checkpoints via `onnx:/path/to/model.onnx`.

Example checkpoint evaluation:

```bash
.venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --build builds/macos/PenaltyShootoutStage2.app \
  --policy stand_center \
  --policy random_legal \
  --policy onnx:results/gk-state-v0_ppo_seed-001/GoalkeeperState-v0/GoalkeeperState-v0-499992.onnx \
  --run-id gk-state-v0-stage3-eval \
  > docs/stage3-eval.log 2>&1
```

The full Stage 3 gate is not a unit test: a trained checkpoint must beat both
`StandCenter` and `RandomLegal` by at least five percentage points on the same
20,000 fixed shots, with no invalid, timeout, or off-target inflation.

## Later milestones

Full replay tooling, Gymnasium APIs, partial observability, shooter training,
self-play, and visual polish remain later milestones.

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
art has been copied into the project.

Unity, the Unity runtime, URP, and related packages remain subject to their
respective Unity terms and are not relicensed by this repository. Stages 0 and
1 contain only generated primitives and project-authored configuration; they
contain no third-party art, audio, club marks, or competition branding.
