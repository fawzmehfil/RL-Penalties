# Penalty Shootout RL: Technical Report

## Abstract

Penalty Shootout RL is a deterministic Unity and ML-Agents environment for
training and evaluating a goalkeeper against football penalty shots. The final
deliverable combines a five-shot human-playable game, native ONNX goalkeeper
inference, fixed statistical benchmarks, and a separate React heatmap report.

The project began with sparse-reward PPO and a nine-action goalkeeper. It later
introduced partial-observation robustness, richer continuous interception
controls, human-like curved shots, and a playable input system. The most
important negative result was that a combined richer-control policy repeatedly
collapsed on the rare binary commit decision. The selected final controller
therefore uses split supervised imitation: one model predicts movement, aim,
and arm reach; a second balanced model predicts when to commit. Unity remains
authoritative for physics, collision, arm motion, and outcomes.

## Final System

The final Stage 9 game runs one five-shot penalty set with these frozen
contracts:

| Concern | Contract |
|---|---|
| Goalkeeper behavior | `GoalkeeperControl-v2` |
| Observation | `control-state-v2-gameplay-v1`, 35 visible-state floats |
| Observation delay | 2 physics ticks / 40 ms |
| Control output | movement, horizontal aim, vertical aim, reach, commit |
| Motor | `keeper-control-forward-v1` |
| Glove handling | `keeper-glove-handling-v1` |
| Player input | `player-penalty-input-v1` |
| Shot command | `player-shot-v1` |
| Flight physics | `football-flight-v1` |
| Match flow | `penalty-set-v1` |
| Replay | `penalty-replay-v1` |

The standalone uses two ONNX models through Unity Inference Engine. It does not
require Python, an ML-Agents trainer, TensorBoard, a network connection, or a
running Unity Editor.

## Environment Design

The origin is the centre of the goal line. The goal is 7.32 m wide and 2.44 m
high, and the penalty mark is 11 m from the goal. Physics runs at 50 Hz. Goal
resolution uses swept whole-ball crossing rather than a trigger-only test.

Each attempt follows a strict lifecycle:

```text
Resetting -> Ready -> RunUp -> BallInFlight -> Resolving -> Terminal
```

Terminal outcomes distinguish goals, controlled saves, deflections out,
misses, frame contacts, timeouts, and invalid states. A goalkeeper touch alone
does not count as a save; a touched ball may still enter the goal.

## Development Stages

### Stage 0: Physics foundation

Stage 0 established canonical ballistics, goal geometry, fixed-timestep
simulation, reset behavior, and Unity-to-Python connectivity. One thousand
canonical shots completed with no invalid outcomes and a maximum target error
of 0.000490 m.

### Stage 1: Deterministic goalkeeper kernel

Stage 1 introduced the reusable arena, seeded procedural shots, the compound
goalkeeper body, nine stable high-level actions, action masks, and contact
telemetry. The 10,000-attempt acceptance run had no invalids, timeouts, or mask
violations. At this stage the ML-Agents observation was a transport-health
placeholder; the kernel was not yet a trainable task.

### Stage 2: Trainable state contract

Stage 2 defined `state-v0`: 24 visible ball and keeper values with no generator
target, future crossing, launch parameter, or outcome leakage. The sparse
reward is +1 for a save, -1 for a goal, and 0 for abnormal/off-target outcomes.
PPO training configurations, curriculum lessons, baselines, telemetry, and
connection verification were added without changing Stage 1 physics.

### Stage 3: Fixed benchmark evidence

Three PPO seeds were trained to 5 million steps. The selected seed 001 scored
46.705% saves on the official fixed 20,000-shot clean benchmark. Stand-centre
scored 4.515% and random-legal scored 4.760%. Seed 002 scored 43.460% on its
official run; seed 003 was not promoted after a weaker 2,000-shot screen.

This result also demonstrates why training curves alone were insufficient. An
early 400-shot check incorrectly suggested policy collapse, while the fixed
20,000-shot evaluation showed substantial learned behavior.

### Stage 4: Robustness experiments

Stage 4 preserved the 24-field order while adding delayed/noisy visible state.
The clean Stage 3 model retained 46.745% on the clean partial-observation
transport but fell to 16.675% under the fixed delay/noise suite. A specialized
feed-forward robust seed reached 31.395% under that suite but only 28.050% on
clean shots. It was recorded as a robustness specialist, not a universal
replacement. Fast-shot and edge-OOD suites exposed further limitations.

### Stage 5: Richer control and the commit failure

Stage 5 replaced nine macros with four continuous interception controls plus a
binary commit decision. The motor gained lateral movement, continuous glove
aim, reach, and a physically validated two-arm presentation. Multiple sparse
PPO revisions improved isolated metrics but repeatedly produced early commits,
weak glove use, or deterministic inactivity.

`GoalkeeperControl-v2` corrected scheduler and observation defects, but the
combined policy still learned no commit. The teacher demonstrations contained
about 654,690 wait actions and only 20,000 commits. A single network could
reduce imitation loss by predicting the majority wait class while also trying
to learn movement, aim, reach, and timing.

Stage 5.6 separated the problem:

- interception model: 35 inputs -> movement, aim x, aim y, reach;
- timing model: selected visible timing inputs -> commit probability;
- Unity: combines both outputs and executes the existing physical motor.

The timing dataset was balanced by episode. The split controller passed its
400-shot gate at 57.25% saves, 72.5% glove contact, 67.39% high-shot saves, and
100% commit. Native Unity inference then matched the Python reference. The
official 20,000-shot canonical run saved 56.87% of shots and contacted the ball
with a glove on 73.95% of attempts.

This remains machine learning: both ONNX functions were learned from held-out
teacher examples. The hand-written controller generated labels, but its rules
are not executed by the final policy. PPO remains an important experimental
part of the project and established the earlier benchmark; supervised
imitation was selected for the final richer-control architecture because it
passed the behavioral evidence gate.

### Stage 6: Human-like shots and contact quality

Stage 6 added the shared player shot command, power-dependent speed, bounded
spin/Magnus physics, placed/power/curled mixtures, imperfect contact, and a fair
40 ms goalkeeper observation delay. The first 2,000-shot pretraining baseline
showed that the selected controller tracked the reactive reference closely but
that glove contacts often continued goalward.

`keeper-glove-handling-v1` introduced palm-aligned bounded deflections without
retraining. On the paired 2,000-shot evaluation it improved expected-on-target
save rate from 43.49% to 55.41% and reduced contact-then-goal rate from 16.78%
to 2.81%, with no safety failures. A more elaborate catch/punch v2 was visually
promising but failed its statistical promotion gate, so v1 remains the final
default and v2 remains experimental.

### Stage 7: Playable vertical slice

Stage 7 connected mouse/trackpad and keyboard input to `PlayerShotCommandV1`.
Pointer movement aims, button-down locks aim and composure, holding charges
power, release shoots, and Q/E adds sidespin. A separate gameplay state machine
handles five valid shots, score, pause, restart, result timing, camera framing,
and atomic replay capture.

### Stage 8: ML analysis

Stage 8 generated a fixed paired 20,000-shot source run and a static React
analysis site. On 18,242 expected-on-target human-like shots, the final native
controller saved 54.61% (95% Wilson interval 53.89%-55.33%) and the reactive
teacher saved 55.83% (55.11%-56.55%). The site visualizes a 4 x 3 goal heatmap
for final save rate and teacher gap, with intended/crossing, style, and speed
filters. It also reports height, style, speed, spin, contact, validity, and
left/right statistics.

The heatmap is important because one overall rate hides strong spatial
structure: centre regions are saved much more often than the extreme edges.

### Stage 9: Final presentation

Stage 9 makes no simulation or model changes. It presents the primitive
collision geometry as a deliberate `rounded-football-v1` toy-sports style,
adds a presentation-only shooter cloned from the keeper's visible proportions,
polishes the pitch/goal/net/background/HUD, replaces prototype tones with a
small CC0 audio library, packages Stage 8 navigation, and creates the local
macOS demo. Automated geometry tests ensure the visible keeper still matches
the frozen Stage 7 transforms, meshes, colliders, and rigidbodies.

The final verification included 133 Python tests, four Stage 8 web tests, five
Stage 9 EditMode tests, two Stage 9 PlayMode tests, and the frozen Stage 7
regression suites. A paired 400-shot Stage 7/Stage 9 gate produced 256 saves
and 256 glove contacts in each scene with identical episode keys and commit
timing. Two independently reconstructed PhysX attempts changed save/goal class,
so the release claims bounded gameplay parity rather than bit-identical
cross-scene trajectories.

## Training and Evaluation Methodology

Training seeds change network initialization, minibatch order, and stochastic
action sampling; they do not change the task contract. Fixed evaluation uses
the same per-arena seeds and attempt quotas for every compared policy. Primary
rates use expected-on-target shots for the human-like suite and all shots for
the canonical on-target suite. Wilson 95% intervals are reported for rates.

Raw training and episode CSV files remain outside Git. Compact JSON reports,
configs, source hashes, model hashes, and test evidence are committed.

## Selected Model

```text
interception model: goalkeeper-interception-v2
SHA-256: ad95050acb5032abffd005e9d5ddf78b8e1c362d79a5d9871b05c50a342b20b0

timing model: goalkeeper-commit-timing-v1
SHA-256: 26c3a80b375574a4e1c02b97183e2ab390736eae76879296ad3daaf85492850b
```

The final headline is the Stage 8 human-like result: 54.61% expected-on-target
saves on 18,242 shots. It must not be directly compared as if it were the same
population as the Stage 3 46.705% canonical nine-action PPO result.

## Conclusion

The final project is both an RL investigation and a usable ML demo. PPO
established the first learned goalkeeper and robustness evidence. The richer
final controller emerged from an evidence-driven failure analysis and split
supervised imitation. The project demonstrates deterministic environment
engineering, observations and action design, sparse-reward RL, OOD evaluation,
imitation learning, native model inference, statistical analysis, and
human-facing interaction in one reproducible system.

The principal limitation is spatial: extreme goal edges remain much harder
than centre regions. Noise robustness is also not solved universally. These
limitations are shown rather than hidden in the final heatmap and model card.
