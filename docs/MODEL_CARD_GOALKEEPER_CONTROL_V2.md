# Model Card: GoalkeeperControl-v2

## Model Summary

`GoalkeeperControl-v2` is a split supervised imitation policy for an isolated
football penalty goalkeeper. It consumes visible ball and goalkeeper state and
controls lateral movement, two-dimensional glove aim, arm reach, and commit
timing. Unity executes the learned command through a fixed physical motor.

This is the goalkeeper selected for the Stage 7/9 playable game.

## Intended Use

- Local penalty-shot gameplay and portfolio demonstrations.
- Reproducible evaluation inside this repository's fixed Unity environment.
- Research/teaching examples for control decomposition, imitation learning,
  sparse-reward RL failure analysis, and fixed-shot benchmarking.

It is not intended to model professional goalkeeper skill, biomechanics,
injury risk, scouting, wagering, or real-player evaluation.

## Inputs

Observation contract: `control-state-v2-gameplay-v1`

- 35 finite visible-state floats.
- Ball position, velocity, angular velocity, flight time, visible time-to-plane,
  and visible predicted aim are delayed by 2 fixed ticks (40 ms).
- Current keeper root, motor, glove, reach, and commit state are included.
- Shot target, style, player command, generated crossing, future trajectory,
  and outcome are excluded.

## Outputs

The interception model produces four bounded continuous values:

1. lateral movement;
2. horizontal glove aim;
3. vertical glove aim;
4. reach extension.

The timing model produces commit probability. The runtime applies the legal
action mask and permits one commit per attempt.

## Architecture and Provenance

The final controller is imitation-assisted machine learning, not a hard-coded
runtime controller. A visible-state reactive controller generated 20,000
teacher episodes. Complete episodes were split without leakage. The
interception and timing responsibilities were trained separately after a
combined behavioral-cloning/PPO policy collapsed to predicting wait.

The final runtime does not execute the teacher's mathematical rules.

| Model | ID | SHA-256 |
|---|---|---|
| Interception | `goalkeeper-interception-v2` | `ad95050acb5032abffd005e9d5ddf78b8e1c362d79a5d9871b05c50a342b20b0` |
| Timing | `goalkeeper-commit-timing-v1` | `26c3a80b375574a4e1c02b97183e2ab390736eae76879296ad3daaf85492850b` |

## Evaluation

### Split-supervision gate

On the fixed 400-shot combined gate:

- save rate: 57.25%;
- glove-contact rate: 72.50%;
- high-shot save rate: 67.39%;
- commit rate: 100%;
- mean first-commit aim error: 0.052 m;
- no invalids, timeouts, masks, clamps, missing actions, or lifecycle errors.

### Canonical benchmark

The native split controller saved 56.87% of 20,000 canonical on-target shots,
with 73.95% glove contact and 100% commit.

### Human-like benchmark

The Stage 8 paired benchmark contains 20,000 attempts, of which 18,242 are
expected on target:

- final save rate: 54.61% (95% CI 53.89%-55.33%);
- final glove-contact rate: 54.56%;
- reactive teacher save rate: 55.83% (55.11%-56.55%).

These populations are not interchangeable with the Stage 3 clean PPO suite.

## Runtime Environment

- Unity `6000.0.74f1`.
- Unity ML-Agents package `4.0.0` for behavior contracts.
- Unity Inference Engine/Sentis for native ONNX inference.
- 50 Hz physics and 40 ms observation delay.
- `keeper-control-forward-v1` motor.
- `keeper-glove-handling-v1` contact response.

Python and an ML-Agents trainer are not required at runtime.

## Limitations

- Extreme left/right goal edges are much harder than central regions.
- A Stage 4 specialist improved delayed/noisy state, but no model was best on
  clean, noisy, fast-OOD, and edge-OOD suites simultaneously.
- The policy observes exact delayed simulated state, not camera pixels.
- The fixed penalty mark has no kicker body or pre-contact deception cue.
- The goalkeeper is a stylized compound body, not a biomechanical human.
- Catch/punch calibration v2 was not promoted; Glove Handling v1 is the final
  bounded deflection model.
- Evaluation is specific to this repository's physics, dimensions, shot
  command, and outcome contracts.

## Ethical and Inappropriate Use

Do not use this model to assess real athletes, infer human ability, make safety
claims, or support betting/scouting decisions. Its percentages describe a
synthetic fixed benchmark only.
