# Penalty Shootout RL environment specification v0

Status: Stage 0 frozen specification  
Environment ID: `penalty-shootout-physics-v0`  
Reference Unity editor: `6000.0.74f1`

## Purpose

This specification defines the geometry, coordinate system, ball model, goal
decision, and Stage 0 acceptance tolerances. It deliberately excludes learned
goalkeeper movement, shooter control, rewards, curricula, and self-play.

## Canonical coordinate system

The origin is the centre of the goal line on the ground:

- `x`: goalkeeper's right when facing the penalty mark.
- `y`: upward.
- `z`: from the goal line toward the penalty mark.
- The mathematical goal plane is `z = 0`.
- The pitch is on the positive-`z` side of the goal plane.

All public scenario positions use metres in this frame.

## Geometry

| Property | Value |
|---|---:|
| Distance between inside edges of posts | `7.32 m` |
| Lower edge of crossbar | `2.44 m` |
| Post and crossbar thickness | `0.12 m` |
| Penalty-mark distance | `11.00 m` |
| Goal half-width | `3.66 m` |

The dimensions follow the current IFAB Laws of the Game:

- <https://www.theifab.com/laws/latest/the-field-of-play/>
- <https://www.theifab.com/laws/latest/the-ball/>

## Ball

| Property | Value |
|---|---:|
| Radius | `0.11 m` |
| Diameter | `0.22 m` |
| Mass | `0.43 kg` |
| Linear damping | `0.0` |
| Angular damping | `0.05` |
| Collision detection | Continuous Dynamic |
| Interpolation | None |
| Gravity | `(0, -9.81, 0) m/s²` |

The initial ball values are representative values inside IFAB's permitted size
and mass ranges. They are benchmark constants rather than claims about one
specific manufactured football.

## Timing

| Property | Value |
|---|---:|
| Physics fixed timestep | `0.02 s` (`50 Hz`) |
| Canonical attempt timeout | `2.0 s` |
| Canonical shot flight time to centre plane | `0.55 s` |

Physics is advanced at a fixed timestep. Evaluation records the Unity editor,
operating system, CPU architecture, package lock, and physics manifest.
PhysX results are compared using declared tolerances; bitwise equality across
platforms is not promised.

## Canonical Stage 0 shot

```text
launch centre = (0.00, 0.11, 11.00)
target centre = (0.00, 1.20, 0.00)
flight time   = 0.55 s
spin          = (0.00, 0.00, 0.00)
noise         = disabled
```

With constant gravity and zero linear damping, initial velocity is:

```text
v = (target - launch - 0.5 * gravity * flight_time²) / flight_time
```

At runtime, the launcher applies the half-gravity-step correction
`-0.5 * gravity * fixed_timestep`. Unity PhysX integrates gravity with
semi-implicit Euler, so this correction keeps the simulated crossing aligned
with the continuous analytical target at the fixed 0.02 s timestep.

Stage 0 uses this analytical solver. Numerical curve calibration is deferred.

## Goal decision

A goal is scored only when all of the following are true:

1. The whole ball has passed the goal plane. For a ball approaching from
   positive `z`, the centre must reach `z <= -ball_radius`.
2. The whole ball is between the inside faces of the posts:
   `abs(center.x) + ball_radius <= goal_half_width`.
3. The whole ball is below the lower edge of the crossbar:
   `center.y + ball_radius <= crossbar_height`.
4. The whole ball remains above the ground:
   `center.y - ball_radius >= 0`.

Crossings are calculated from the previous and current ball positions with a
swept line-plane intersection. A trigger callback alone is not authoritative.

The centre-plane intersection at `z = 0` is also recorded for shot-placement
error, but it does not by itself declare a goal.

## Contact and saves

Contact is an event, not an outcome. A future goalkeeper touch will record the
time, contact point, body part, and impulse while simulation continues.

A touched ball that subsequently crosses the goal plane is a `Goal`. A `Saved`
outcome can only be declared after the ball is controlled, safely out, or
otherwise unable to score. Stage 0 has no moving goalkeeper and does not
implement save rewards.

## Stage 0 terminal outcomes

- `Goal`: whole-ball goal conditions are satisfied.
- `MissWide`: the ball passes behind the goal plane outside the horizontal
  goal bounds.
- `MissHigh`: the ball passes behind the goal plane above the legal vertical
  bounds.
- `Timeout`: no terminal spatial outcome occurs before `2.0 s`.
- `Invalid`: a non-finite state, duplicate terminal transition, or malformed
  scenario is detected.

Every attempt produces exactly one terminal outcome.

## Reset invariant

Reset restores:

- Ball position and identity rotation.
- Zero linear and angular velocity.
- Attempt timer and previous position.
- Centre-plane intersection state.
- Terminal-outcome latch.
- Counters that belong to the attempt rather than the environment session.

## Stage 0 acceptance criteria

- The canonical centre-plane intersection is within `0.05 m` of its target.
- Whole-ball, partial-crossing, wide, high, and high-speed swept tests pass.
- Reset invariants pass.
- No non-finite value is observed.
- A batch of 1,000 canonical PhysX attempts terminates 1,000 times.
- The batch records zero invalid outcomes and zero duplicate terminal events.
- A macOS headless player connects to Python, exposes the expected behavior,
  accepts a no-op action, returns terminal steps, and shuts down cleanly.
- The same connection check succeeds on Linux when the Linux module is
  installed.

Changing a value in this specification requires a new environment manifest
hash and, after public release, a new environment version.
