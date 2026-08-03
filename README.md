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

## Current status: Stage 5 frozen; Stage 6 authorized

Stages 0-4 established and evaluated the deterministic environment, the first
trainable nine-action goalkeeper, fixed 20,000-shot benchmarks, and
partial-observation robustness. Stage 5 now adds a separate richer goalkeeper
control task and physically validated motor. One 8-million-step Stage 5 seed
reached a 45.7% save rate on a fixed 2,000-shot screen, but used glove contact
on only 12.7% of attempts. The first 1-million-step Stage 5.1 correction
improved high-shot coverage but still committed immediately and produced glove
contact on only 10.75% of its fixed 400-shot evaluation. Stage 5.2 then reached
a best 21.0% save rate, but still committed on the first decision in every
attempt, used glove contact on only 8.75%, and saved only 1.45% of high shots.
Stage 5.3 added visible-state guidance and reached 24.75% saves, but its best
checkpoint still committed immediately on every attempt because the training
scaffold could replace the policy's chosen action. Stage 5.4 removed those
overrides, but its best 1-million-step checkpoint saved only 10.75% of the
fixed 400-shot batch. A forensic audit then found that the remaining problem
was broader than PPO tuning: the v1 agent combined a `DecisionRequester` with
manual requests, requested actions during non-flight phases, had insufficient
explicit vertical interception state, and rewarded a timing window that was
incompatible with the fastest shots and the motor's reach latency.

`GoalkeeperControl-v2` corrects those contracts before more training. It uses
one request for one executed command, starts decisions only after launch,
appends three visible-derived ballistic values to a 35-float observation,
uses result-first terminal training rewards, removes final-lesson distribution
bias, and disables duplicate observation normalization. Its 250,000-step
diagnostic proved that the scheduler is exact, but the best deterministic
checkpoint saved only 5.75% and selected no commit on all 400 evaluation
shots. The Stage 3 seed `001` model remains the main clean goalkeeper until
this richer control gate is passed. Stage 5.5 recorded and validated 20,000
visible-state teacher demonstrations, but its combined behavioral-cloning plus
PPO policy still collapsed to 0% commit. Stage 5.6 fixed the task architecture:
one supervised model learns interception and arm reach, while a second balanced
model learns commit timing. The phase-aware split controller passed its fixed
400-shot gate at 57.25% saves, 72.5% glove contact, 67.39% high-shot saves, and
100% commit. Stage 5.6B packages those exact models in Unity and passed native
runtime parity. Its official 20,000-shot benchmark saved 56.87%, contacted the
ball with a glove on 73.95% of attempts, saved 68.82% of high shots, and
committed on every attempt with no lifecycle or safety errors. Stage 5 is now
frozen and Stage 6 shot-distribution work is authorized.

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

### Stage 5.1 reach-learning diagnostic

The original sparse Stage 5 seed learned a torso-first strategy. Across its
retained 5M-8M checkpoints, glove-contact rate stayed between roughly 10% and
16%, while `reactive_reach_v1` exceeded 70% glove contact with the same motor.
This establishes a training credit-assignment problem rather than a reach or
collision limitation.

Stage 5.1 preserves `GoalkeeperControl-v1`, `control-state-v1`, all 32
observations, `goalkeeper-hybrid-v1`, and `keeper-control-v1`. Its optional
training overlay is enabled only by:

```text
stage5.reach_training_enabled = 1
```

The overlay uses deterministic reach-focused on-target samples, full-reach and
automatic-commit scaffolding only in the first lesson, a temporary reach floor
in the second lesson, and unassisted actions thereafter. Its bounded terminal
training reward is `+1` for a glove save, `+0.8` for another save, `-1` for a
goal, and `0` for abnormal outcomes. Canonical evaluation remains unchanged on
`goalkeeper-sparse-v0`; target or future-impact data is never observed or
rewarded.

Use
`configs/training/goalkeeper-control-v1-ppo-reach-diagnostic.yaml` for the
1-million-step gate. If glove usage, aim error, high-shot coverage, and overall
save rate improve together, use
`configs/training/goalkeeper-control-v1-ppo-reach.yaml` for the full run.

### Stage 5.2 reach and timing correction

The completed Stage 5.1 diagnostic saved 19.5% of 400 fixed shots, reached
14.49% on high shots, and terminated with zero invalids, timeouts, or mask
violations. It failed the richer-control gate because glove contact was only
10.75%, mean commit aim error was 1.38 m, mean peak reach was 0.581, and every
attempt committed on the first 0.04-second decision.

Stage 5.2 is enabled by:

```text
stage5.reach_training_enabled = 1
stage5.reach_training_version = 2
```

It preserves the released 32-float observation and hybrid action contracts.
Its training-only curriculum uses ball position and velocity to estimate
visible time remaining to the goal plane. Early lessons guide or guard commit
timing, then remove every extra action constraint before canonical full-range
training. Reach-focused targets are balanced across low, middle, and high
bands. The v2 terminal reward gives `+1.0` to glove saves, `+0.25` to other
saves, `-0.75` to glove-contact goals, between `-1.0` and `-0.85` to other
goals based on measured glove proximity, and `0.0` to abnormal outcomes.
Failed attempts always remain negative, and hidden target/future-impact fields
are never observed or rewarded.

Use
`configs/training/goalkeeper-control-v1-ppo-reach-v2-diagnostic.yaml` for the
new 1-million-step gate. Do not use
`configs/training/goalkeeper-control-v1-ppo-reach-v2.yaml` for full training
until that diagnostic passes.

After closing the Unity editor, verify and rebuild without starting training or
evaluation:

```bash
scripts/verify_stage5_reach_v2.sh
```

Then start the diagnostic:

```bash
arch -x86_64 .venv/bin/mlagents-learn \
  configs/training/goalkeeper-control-v1-ppo-reach-v2-diagnostic.yaml \
  --env builds/macos/PenaltyShootoutStage5.app \
  --run-id gk-control-v1_reach-v2-diagnostic_seed-001 \
  --seed 1 \
  --no-graphics
```

The completed v2 checkpoint screen did not pass. Its best checkpoint was
`GoalkeeperControl-v1-749967.onnx` at 749,967 steps: 21.0% overall saves,
8.75% glove contact, 1.45% high-shot saves, 0.538 mean peak reach, and 1.17 m
mean commit aim error. All 400 attempts still committed at the first
0.04-second decision. Invalids, timeouts, action-mask violations, and command
clamps remained zero. Physical root-target saturation occurred on 53.5% of
attempts; that is expected motor reach limiting and is now reported separately
from malformed command clamping.

### Stage 5.3 deliberate-save curriculum

Stage 5.3 keeps the released behavior, 32-float observation, hybrid action,
motor, and canonical sparse evaluation reward unchanged. It is enabled by:

```text
stage5.reach_training_enabled = 1
stage5.reach_training_version = 3
```

Its training layer addresses the failure observed in v2:

- A prediction derived only from current visible ball position, velocity, and
  gravity estimates where the ball will cross the goal plane. No generator
  target, launch parameters, or future-impact state enters the observation.
- Early curriculum lessons blend that visible prediction into the policy aim.
  Guidance fades through the lessons and is exactly zero in the final
  canonical lesson.
- Commit scaffolding waits for observed ball flight and a useful visible
  time-to-plane window. Automatic commitment exists only in the first lesson;
  the policy must learn timing before assistance is removed.
- Terminal training credit ranks glove-first saves above later glove saves,
  arm saves, and body saves. Goals remain negative, with bounded penalties for
  premature commits and poor visible-state aim.
- Telemetry records raw policy aim, guided aim, first eligible commit timing,
  premature commits, contact-specific saves, and physical root-target
  saturation. The evaluator selects only checkpoints that pass the complete
  gate, rather than selecting a failed model by save rate alone.

The 400-shot diagnostic gate requires at least 20% saves, 25% glove contact,
12% glove saves, 15% high-shot saves, and 0.65 mean peak reach. It also
requires commit aim error at or below 1.0 m, immediate commits at or below
10%, premature commits at or below 15%, and zero invalids, timeouts, action
mask violations, or malformed command clamps.

After closing the Unity editor, verify and rebuild without starting training
or evaluation:

```bash
scripts/verify_stage5_reach_v3.sh
```

Then start the new 1-million-step diagnostic seed:

```bash
arch -x86_64 .venv/bin/mlagents-learn \
  configs/training/goalkeeper-control-v1-ppo-reach-v3-diagnostic.yaml \
  --env builds/macos/PenaltyShootoutStage5.app \
  --run-id gk-control-v1_reach-v3-diagnostic_seed-001 \
  --seed 1 \
  --no-graphics
```

The completed Stage 5.3 screen did not pass its control-quality gate. The best
checkpoint, `GoalkeeperControl-v1-749975.onnx`, saved 24.75% of 400 shots, but
committed immediately and prematurely on every attempt. It used glove contact
on 15.25% of attempts, saved 7.25% with gloves, saved 5.07% of high shots, and
reached a mean peak extension of 0.565. It had zero invalids, timeouts, action
mask violations, and malformed command clamps.

### Stage 5.4 policy-faithful training

The Stage 5.4 peripheral audit found no evidence that body dimensions, glove
size, root travel, fixed timestep, decision cadence, shot speed, or collision
geometry are the limiting factor. The same motor reached full extension in
scripted validation, and the visible-state reactive controller produced 73.7%
glove contact and 62.8% high-shot saves on the fixed evaluation distribution.
The learned policy's low reach and first-decision commitment are therefore
treated as policy/training failures. Stage 5.4 deliberately does not lengthen
the arms, enlarge colliders, slow shots, or increase root travel.

Stage 5.4 is enabled by:

```text
stage5.reach_training_enabled = 1
stage5.reach_training_version = 4
```

Its training contract changes only the training layer:

- The policy's raw move, aim, reach, and commit choices execute unchanged in
  every lesson. There are no timing masks, automatic commits, aim blends, or
  reach floors.
- Decision-time reward uses only visible ball state and the submitted command.
  It penalizes immediate, premature, and late commitment, visible aim error,
  and insufficient reach, while giving a small bonus for a useful
  time-to-plane commit window.
- Terminal reward still dominates. Glove-first saves rank above glove, arm,
  and body saves; a premature save cannot become a positive shortcut.
- The curriculum retains focused early lessons but spends 35% of training on
  the unassisted canonical distribution.
- Telemetry proves that the deployed policy was evaluated: policy-action
  override count must remain zero, alongside timing, reach, contact, physics,
  and validity metrics.

The Stage 5.4 diagnostic gate keeps the existing save, glove, high-shot,
aim-error, reach, validity, and physics checks. It also requires at least 70%
timely commits, no more than 15% late commits, mean reach shortfall at or below
0.20, and zero policy-action overrides.

After closing the Unity editor, verify the Stage 5.4 implementation and rebuild
without starting training or evaluation:

```bash
scripts/verify_stage5_reach_v4.sh
```

Then train one 1-million-step seed and screen the retained checkpoints on the
same 400 fixed shots:

```bash
scripts/run_stage5_reach_v4_diagnostic.sh 1
```

The handoff script trains only seed `001`, retains checkpoints near 200k,
400k, 600k, 800k, and 1M, evaluates them with stand-center, random-hybrid, and
reactive-reach baselines, and writes the comparison under
`results/evaluations/gk-control-v1_reach-v4-policy-faithful_seed-001-checkpoint-screen-400/`.

### Stage 5 control lifecycle remediation

The completed Stage 5.4 screen ruled out a simple motor-capability problem.
Its best checkpoint saved 10.75% of 400 fixed shots, compared with 54.25% for
the visible-state reactive controller. The reactive controller also reached
72% glove contact and saved 64.49% of high shots using the same body, arm,
glove, shot, and physics configuration.

The audit identified four coupled defects in the v1 learning task:

- `DecisionRequester` and controller-driven `RequestDecision()` calls were
  both active, so ML-Agents policy steps were not in one-to-one correspondence
  with executed controller commands.
- Decisions were requested during reset, ready, and run-up. TensorBoard
  therefore reported substantially longer episodes than terminal telemetry's
  accepted command count.
- `control-state-v1` exposed raw visible ball state but required PPO to derive
  vertical goal-plane interception during a short reaction window. The
  reactive baseline's explicit ballistic derivation showed that this missing
  representation was consequential.
- The v4 reward penalized early commitment even where the fastest valid shots
  required immediate motor preparation. The final lesson also retained a 35%
  focused-shot overlay instead of matching canonical evaluation.

The versioned replacement preserves the physical motor and action surface:

- Behavior: `GoalkeeperControl-v2`
- Observation: `control-state-v2`, exactly 35 floats
- Action: four continuous controls plus commit branch `[2]`
- Motor: unchanged `keeper-control-v1`
- Benchmark: `goalkeeper-control-v2-id-20k`

The first 32 observations remain in their v1 order. The final three are
`visible_time_to_goal_plane`, `visible_predicted_aim_x`, and
`visible_predicted_aim_y`. They are computed only from the current visible ball
position, velocity, fixed gravity, and public goal geometry. Generator target,
launch parameters, sampled flight time, future generator impact, and outcome
remain excluded.

The v2 lifecycle has no `DecisionRequester`. The controller requests the first
decision only after ball launch, consumes that decision once on the following
control tick, and then requests the next. Terminal telemetry records requested,
consumed, discarded, duplicate, and missing decision counts. The evaluator
requires:

```text
requests = consumed + terminal discards
consumed = accepted controller commands
duplicate requests = 0
missing actions = 0
terminal discards <= 1 per attempt
```

Training version `5` removes timing, target-error, proximity, and reach
shortfall shaping. Terminal rewards rank glove-first, later-glove, arm, and
body saves while keeping goals at `-1` and abnormal outcomes at `0`. The final
lesson uses the exact canonical shot generator with no reach-focus overlay.
The v2 PPO configs also set `normalize: false` because every observation is
already explicitly bounded.

After closing the Unity editor, verify and rebuild without starting training
or evaluation:

```bash
scripts/verify_stage5_control_v2.sh
```

The completed deliberately short 250,000-step diagnostic was run with:

```bash
scripts/run_stage5_control_v2_diagnostic.sh 1
```

It retained checkpoints every 50,000 steps and screened them, plus
stand-center, random-hybrid, and reactive-reach baselines, on the same 400
fixed shots. Lifecycle telemetry passed exactly, but the behavioral gate did
not: the best learned checkpoint saved 5.75% versus 57.25% for
`reactive_reach_v1` and produced no deterministic commits. Do not start the
4-million-step config from this result.

### Stage 5.5 reactive imitation bootstrap

Stage 5.5 preserves the validated `GoalkeeperControl-v2` motor, 35-float
observation, four continuous controls, binary commit branch, shot physics, and
terminal reward. It changes how the policy is initialized. The
`reactive_reach_v1` teacher computes movement, goal-plane aim, reach, and
commit timing from visible ball and goalkeeper state, gravity, and public goal
geometry. It never reads the sampled target, launch parameters, future
generator impact, flight-time parameter, or outcome.

The versioned demonstration contract is
`goalkeeper-control-v2-reactive-demo-v1`:

- 16 arenas and 1,250 completed attempts per arena.
- 20,000 canonical `on-target-v0` attempts with master seed `20260723`.
- One `.demo` file per arena, closed only after a terminal episode.
- Exactly one legal commit per episode and bounded finite continuous actions.
- Minimum teacher quality of 50% saves, 65% glove contact, and 55% high-shot
  saves.
- Zero invalids, timeouts, off-target outcomes, action-mask violations,
  command clamps, duplicate decision requests, or missing policy actions.

After the first accepted commit, the commit branch remains masked until the
next attempt. This prevents a recovered goalkeeper from performing an
unrealistic second dive against the same shot.

The inspector loads the actual ML-Agents `.demo` files, checks the `[[35]]`,
continuous `4`, branch `[2]` behavior spec, validates all episode/action
constraints, and writes hashes and dataset metrics to the ignored
`results/demonstrations/.../manifest.json`. Existing data is reused only after
strict validation; partial or invalid output fails without being deleted or
overwritten. ML-Agents records an executed action with the following
observation, so action legality is checked against the preceding decision's
observation and action mask.

The 500,000-step diagnostic uses behavioral cloning at strength `0.5` for
300,000 steps, with PPO becoming dominant for the final 200,000 steps.
Checkpoints are retained every 50,000 steps and screened on the same 400 fixed
shots against stand-center, random-hybrid, the reactive teacher, and the
no-BC v2 checkpoint. Promotion requires all of:

```text
save rate >= 35%
commit rate >= 85%
glove-contact rate >= 40%
glove-save rate >= 20%
high-shot save rate >= 30%
mean first-commit aim error <= 0.75 m
mean peak reach >= 0.65
exact request/consume/discard balance
zero invalids, timeouts, masks, clamps, duplicates, and missing actions
```

Close the Unity editor, then verify both Stage 5 builds without starting the
long recording or training job:

```bash
scripts/verify_stage5_imitation.sh
```

Run the full record, validate, train, and checkpoint-screen handoff with:

```bash
scripts/run_stage5_control_v2_bc_handoff.sh 1
```

If recording and training complete but checkpoint screening is interrupted,
resume only the fixed 400-shot evaluation with:

```bash
scripts/run_stage5_control_v2_bc_evaluation.sh 1
```

The evaluator preflights a dedicated worker-port range before launching Unity.
Override it with `STAGE5_EVAL_WORKER_ID_START` only when another local service
uses that range.

The unattended handoff requires at least 20 GiB free at launch. During
demonstration recording it aborts on a disk-full error, below 5 GiB free, or
45 minutes without `.demo` file growth, leaving partial output intact for
inspection rather than silently continuing.

Raw `.demo` files, checkpoints, and evaluation CSVs remain ignored under
`results/`. The diagnostic did not pass, so the three-seed 2-million-step
promotion config remains unused. Training length is not increased; Stage 5.6
separates movement/aim/reach learning from commit timing.

### Stage 5.6 split supervision

The completed Stage 5.5 diagnostic did remain deterministically inactive. Its
best checkpoint saved 7.5% and committed on 0% of the fixed 400 shots, despite
the teacher dataset passing at 55.04% saves, 74.03% glove contact, and 64.98%
high-shot saves. The demonstrations contain 674,690 aligned decisions but only
20,000 commits, so Stage 5.6 no longer asks one loss to learn the four
continuous interception controls and the rare timing decision together.

`goalkeeper-control-v2-split-supervision-v2` preserves the 35-float
observation, four continuous actions, commit branch `[2]`, motor, arms,
physics, reward, and canonical shot set. It trains two offline supervised
models:

- `goalkeeper-interception-v2` predicts movement, aim, and reach before and at
  commit, then continues learning aim and reach while the committed dive is in
  progress.
- `goalkeeper-commit-timing-v1` reads only commit availability, ball-flight
  time, and visible time-to-plane and predicts wait versus commit.

The first split attempt trained interception only through the commit row. It
passed that incomplete offline gate but saved 42.5% against the teacher's
57.25%, with 42.5% glove contact against 73.25%. Runtime inspection proved
that committed dives continue reading `activeCommand` aim values for glove
correction. Held-out post-commit aim MAE was `0.290` horizontally and `0.373`
vertically, versus `0.007` and `0.008` through commit. Version 2 corrects this
specific contract error with equal pre-commit, commit, and post-commit phase
sampling. Post-commit movement is ignored because the root trajectory is
already latched, while aim and reach remain supervised and independently
gated offline.

ML-Agents stores each executed action with the following observation. The
extractor therefore shifts every action back to the preceding observation and
mask, includes the final pre-terminal action, and splits complete episodes
into exactly 16,000 training, 2,000 validation, and 2,000 test shots. The real
dataset has 10,584 shots where the teacher commits on the first usable
decision. Those shots have no same-episode wait; their balanced negative is
selected deterministically from another legal wait in the same arena and
split, and the manifest reports this fallback count.

The first evaluation runs ONNX in Python while Unity executes the unchanged
motor, arm IK, collisions, shots, and telemetry. It stops in order at:

1. held-out offline gates for both models;
2. a `16 x 4` Unity integration smoke test;
3. 400 shots with learned interception and teacher timing;
4. 400 shots with both learned models.

Run the complete evidence-first handoff with the Unity editor closed:

```bash
scripts/run_stage5_split_supervision_handoff.sh 1
```

The command never starts PPO. Any failed gate writes available compact
evidence and exits without retrying or increasing training length. A passing
combined gate authorizes planning Stage 5.6B native inference and short PPO
refinement; it does not launch that work automatically.

#### Stage 5.6A confirmed fix

The phase-aware v2 correction passed every offline, smoke, interception, and
combined gate. On the fixed 400 shots it achieved:

| Metric | Split supervised v2 |
|---|---:|
| Save rate | 57.25% |
| Commit rate | 100.0% |
| Glove contact rate | 72.50% |
| Glove save rate | 50.00% |
| High-shot save rate | 67.39% |
| Mean first-commit aim error | 0.052 m |
| Mean peak reach | 0.991 |

The cause and correction are now evidence-backed. The v1 interception model
was trained only through the commit decision, while Unity continued consuming
aim and reach on every in-dive decision for midair glove correction. Supervising
aim and reach after commit reduced held-out post-commit aim MAE from
`0.290/0.373` to `0.009/0.012` and restored teacher-level Unity behavior.

#### Stage 5.6B native Unity inference

`goalkeeper-control-v2-split-native-v1` packages the selected interception and
timing ONNX models as Git LFS assets and runs both with
`Unity.InferenceEngine` on CPU. It preserves `GoalkeeperControl-v2`, the
35-float observation, the hybrid action contract, motor, arm IK, masks,
physics, and deferred request/consume lifecycle.

The evaluator sends the Python model's action as a shadow reference while
Unity independently computes and executes the native action. The official
20,000-shot gate recorded 668,796 native evaluations, maximum
continuous-action error `1.42e-6`, zero commit mismatches, and zero invalid
outputs. Native Unity saved 56.87% versus Python's 56.84%, with identical
episode keys and all parity, behavioral, and safety checks passing. Native
save rates were 68.82% for high shots, 54.77% for middle shots, and 48.08% for
low shots.

Reproduce the import, tests, build, 64-shot smoke, and 400-shot gate with:

```bash
scripts/run_stage5_native_inference_handoff.sh
```

The handoff also creates
`builds/macos/PenaltyShootoutStage5Native.app`. That player uses
`HeuristicOnly` agents with native inference enabled by default, so the
goalkeeper runs without a Python evaluator or trainer. It is a deployment
validation build, not the final Stage 9 playable presentation.

The handoff never starts PPO. Directly refining two custom split ONNX networks
through the stock ML-Agents PPO trainer is not a valid continuation of the
passing architecture. Any future refinement must use a separately versioned,
bounded residual or distillation contract and is limited to 250,000 diagnostic
steps before another promotion decision.

## Later milestones

Future stages extend the goalkeeper benchmark into a fuller football AI and
playable demo:

- Stage 6: train and evaluate on a versioned mixture of standard procedural
  shots, human-like aim and power distributions, imperfect timing and
  directional noise, spin and curve, common player tendencies, and rare
  edge or unusually fast shots. The human-like generator will use the same
  shot-control parameters and launch physics planned for Stage 9 so the
  playable mode is part of the training design rather than a late,
  incompatible input source.
- Stage 7: replay and analysis UI for heatmaps, per-quadrant results,
  trajectory review, and dive-choice inspection.
- Stage 8: final model packaging, Unity inference import, model cards, and
  reproducible release artifacts.
- Stage 9: a human-playable penalty mode where the user shoots against the
  trained goalkeeper with polished laptop controls, cameras, feedback, replay,
  and game feel closer to a compact football game than a lab scene. Real
  player-shot telemetry will first validate the Stage 6 distribution; only a
  short calibration fine-tune should be needed if measured player shots expose
  a meaningful distribution mismatch.

Stage 6 is expected to produce the broadest goalkeeper checkpoint, but it is
not the only training stage that matters. Stage 2 established perception and
basic save learning, Stage 4 measured and trained observation robustness, and
Stage 5 teaches the richer movement, commitment, aiming, and arm-reach policy
that Stage 6 will continue training. Stage 6 broadens that learned controller;
it does not replace the control capability or evidence built earlier.

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
