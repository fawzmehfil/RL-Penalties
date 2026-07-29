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

## Current status: Stage 5 control prototype implemented

Stages 0-4 established and evaluated the deterministic environment, the first
trainable nine-action goalkeeper, fixed 20,000-shot benchmarks, and
partial-observation robustness. Stage 5 now adds a separate richer goalkeeper
control task and physically validated motor. Its PPO configuration and
benchmark runner are ready, but no Stage 5 learned checkpoint has been trained
or selected yet. The Stage 3 seed `001` model remains the main clean
goalkeeper until that new training gate is passed.

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

## Stage 4 goalkeeper robustness

Stage 4 keeps the Stage 3 clean benchmark intact and adds a separate robustness
layer for `GoalkeeperRobust-v0`. The new `state-po-v0` observation profile keeps
the same 24-float order as `state-v0`, but the values can be delayed, noised, or
dropped through ML-Agents environment parameters. Terminal benchmark telemetry
records those perturbation settings; observations still exclude target, launch,
future-impact, and outcome fields.

Prepare and verify the Stage 4 robust scene/build with:

```bash
./scripts/verify_stage4.sh
```

The Stage 4 benchmark configs live under `configs/benchmarks/`:

- `goalkeeper-robust-v0-id-20k`: in-distribution shots through `state-po-v0`
- `goalkeeper-robust-v0-delay-noise-20k`: delayed/noisy visible state
- `goalkeeper-robust-v0-speed-ood-20k`: faster flight-time OOD range
- `goalkeeper-robust-v0-edge-ood-20k`: high-right edge/corner OOD range

Example robust smoke evaluation:

```bash
.venv/bin/python -m penalty_shootout.evaluation.goalkeeper \
  --benchmark configs/benchmarks/goalkeeper-robust-v0-delay-noise-20k.json \
  --build builds/macos/PenaltyShootoutStage4.app \
  --policy stand_center \
  --policy random_legal \
  --policy onnx:results/gk-state-v0_ppo_seed-001/GoalkeeperState-v0/GoalkeeperState-v0-5000019.onnx \
  --attempts-per-arena 4 \
  --run-id stage4-delay-noise-smoke
```

Robust training configs:

- `configs/training/goalkeeper-robust-v0-ppo.yaml`
- `configs/training/goalkeeper-robust-v0-ppo-recurrent.yaml`

Stage 4 reports are intentionally separate from Stage 3 evidence:
`docs/stage4-robustness-report.json`,
`docs/stage4-ablation-report.json`, and
`docs/stage4-training-summary.json`.

Stage 4 completed three feed-forward and three recurrent 5-million-step runs.
The final fixed-batch evidence does not support a universal Stage 4
replacement:

| 20k suite | Stage 3 seed 001 | Robust feed-forward seed 003 |
|---|---:|---:|
| clean `state-po-v0` | 46.745% | 28.050% |
| delay/noise | 16.675% | 31.395% |
| speed OOD | 35.215% | 20.095% |
| edge OOD | 0.830% | 0.030% |

Stage 3 seed `001` therefore remains the main clean goalkeeper. Stage 4
feed-forward seed `003` is retained as a delayed/noisy-observation specialist,
not as a general replacement. Recurrent seed `002` was the strongest recurrent
screen at 30.1% on the 2,000-shot delay/noise batch, but it did not beat the
feed-forward specialist and was not promoted to a full 20,000-shot benchmark.
All reported runs had zero invalid outcomes, timeouts, and action-mask
violations.

## Stage 5 richer goalkeeper control

Stage 5 introduces a new task rather than changing any trained v0 behavior:

- Behavior: `GoalkeeperControl-v1`
- Observation profile: `control-state-v1`, exactly 32 visible-state floats
- Action profile: `goalkeeper-hybrid-v1`
- Motor profile: `keeper-control-v1`
- Reward: unchanged sparse `goalkeeper-sparse-v0`

The hybrid action contains four continuous values:

| Index | Control | Meaning |
|---:|---|---|
| 0 | `move_x` | Ground movement left/right |
| 1 | `aim_x` | Horizontal save target |
| 2 | `aim_y` | Vertical save target |
| 3 | `reach` | Arm-extension demand |

One discrete branch `[2]` selects `NoCommit` or `CommitSave`. Movement remains
adjustable while standing. A commitment latches the save target and runs a
deterministic `Planting -> Diving -> Recovering` motor sequence; another
commitment is masked until recovery. Small bounded aim corrections and reach
changes remain possible in flight.

Both arms are now articulated upper-arm and forearm capsules solved by
deterministic two-bone IK. Segment lengths stay fixed, glove targets are
speed-limited, the leading and trailing hands remain coordinated, and all
visible segments/gloves carry the authoritative compound colliders. Animation
may later follow these poses, but it does not control collisions.

The ready body uses versioned torso, head, and leg collider geometry with a
slight forward crouch and symmetric leg splay. Reach begins during planting,
continues monotonically through the dive, and retracts only during recovery,
preventing learned or manual action jitter from producing arm-flapping saves.

Stage 5 adds:

- `Stage5ControlArena.prefab`
- `GoalkeeperControlLab.unity` for one-arena visual/manual validation
- `ControlTraining.unity` with 16 lightweight arenas
- `PenaltyShootoutStage5.app` headless build
- scripted `stand_center_v1`, `random_hybrid_v1`, and
  `reactive_reach_v1` evaluator policies
- fixed ID, speed-OOD, and edge-OOD 20,000-shot contracts
- PPO curriculum in `configs/training/goalkeeper-control-v1-ppo.yaml`

Run the complete implementation verification with:

```bash
./scripts/verify_stage5.sh
```

To inspect the motor, open `GoalkeeperControlLab` in Unity and enter Play mode.
Use `A`/`D` to move, arrow keys to aim, `Shift` or the left mouse button to
request full reach, and `Space` to commit. This is a motor-validation tool, not
a trained policy demo.

The automated motor batch currently completes 128/128 attempts with zero
invalid outcomes, timeouts, or mask violations. It records successful glove
contacts, and the live Python smoke evaluator validates observation shape `[32]`,
continuous size `4`, branch `[2]`, and terminal telemetry. These are
implementation checks only; the 64-shot reactive result is not an official
benchmark claim.

After visual approval of the motor, start the first Stage 5 PPO seed with:

```bash
arch -x86_64 .venv/bin/mlagents-learn \
  configs/training/goalkeeper-control-v1-ppo.yaml \
  --env builds/macos/PenaltyShootoutStage5.app \
  --run-id gk-control-v1_ppo_seed-001 \
  --seed 1 \
  --no-graphics
```

Train at least three seeds. Screen checkpoints on 400-2,000 fixed shots, then
run the full 20,000-shot ID and OOD suites only for the strongest candidates.
Promotion requires beating the v1 stand/random baselines by at least five
percentage points with zero invalid, timeout, or mask-violation inflation.

## Later milestones

Future stages extend the goalkeeper benchmark into a fuller football AI and
playable demo:

- Stage 6: broader shot variety, including curve, spin, deception, and harder
  procedural distributions.
- Stage 7: replay and analysis UI for heatmaps, per-quadrant results,
  trajectory review, and dive-choice inspection.
- Stage 8: final model packaging, Unity inference import, model cards, and
  reproducible release artifacts.
- Stage 9: a human-playable penalty mode where the user shoots against the
  trained goalkeeper with polished laptop controls, cameras, feedback, replay,
  and game feel closer to a compact football game than a lab scene.

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
