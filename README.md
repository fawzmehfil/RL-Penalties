# Penalty Shootout RL

An open-source reinforcement-learning benchmark for isolated football penalty
attempts, built with Unity and Unity ML-Agents. The finished system is intended
to contain a trainable goalkeeper, a trainable shooter, and goalkeeper-versus-
shooter self-play—not just a scripted penalty game.

## Project goal

The benchmark will test reaction time, partial observability, opponent
modelling, generalization, self-play, and reward design while remaining
visually polished enough for interactive demonstrations. Planned deliverables
include headless training, scripted baselines, fixed evaluation suites,
leaderboards, replays, human-versus-agent play, and public ML-Agents/Gym APIs.

The curriculum progresses from a goalkeeper facing predictable procedural
shots, through randomized and curved shots, to a learned shooter and finally
two-agent self-play. High-level actions such as shuffling and directional dive
choices keep the RL problem focused on decisions rather than humanoid joint
control.

## Current status: Stage 0

Stage 0 establishes the physics and toolchain before any goalkeeper training:

- Unity 6000.0.74f1 URP and ML-Agents Unity package 4.0.0.
- Python 3.10.12 with `mlagents`/`mlagents-envs` 1.1.0.
- A generated `PhysicsLab` scene with a canonical ballistic shot.
- Swept, whole-ball goal-line detection and Goal/Miss/Timeout outcomes.
- Nine focused Edit Mode tests and a 1,000-shot Play Mode acceptance test.
- macOS ML-Agents connection verification and a Linux headless build.

See [STAGE_0_REPORT.md](STAGE_0_REPORT.md) for measured results and
[docs/environment-spec-v0.md](docs/environment-spec-v0.md) for the frozen
environment contract.

## What counts as a save?

Glove or body contact is an event, not automatically a save. A contacted ball
that later crosses fully over the goal line inside the posts and below the
crossbar is still a goal. A save is assigned only when the attempt terminates
without a goal and the attribution rules can connect the prevention to the
goalkeeper. This avoids rewarding cosmetic touches that do not change the
outcome.

## Local setup

1. Open `unity/` with Unity 6000.0.74f1.
2. Run `scripts/setup_python.sh`.
3. Run `scripts/verify_stage0.sh`.

The Python environment intentionally uses x86_64 Python under Rosetta on Apple
silicon because the exact gRPC version required by ML-Agents Release 23 has no
arm64 macOS wheel.

## License

Apache License 2.0. Third-party attribution is recorded in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
