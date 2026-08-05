# Stage 6 Glove Handling v1

Status: promoted as the Stage 6 gameplay default.

## Purpose

`keeper-glove-handling-v1` replaces the spherical glove collision shape with a
compound palm-and-fingers shape and adds deterministic post-contact handling.
It does not change the selected goalkeeper models, the 35-float observation,
the hybrid action contract, shot physics, or the goalkeeper motor.

The pre-change fallback is tagged `stage6-pre-glove-handling-v1`. Legacy
spherical gloves remain selectable with `stage6.glove_handling_v1=0` and are
identified as `keeper-glove-physx-legacy-v1` /
`goalkeeper-sphere-gloves-legacy-v1` in telemetry.

## Contact Outcomes

- `Catch`: an aligned, central contact below the one- or two-hand speed limit.
  The ball follows the glove for 0.12 seconds before the attempt is saved.
- `Parry`: a front-palm contact that redirects the ball away from goal.
- `Punch`: a parry where the glove also has sufficient forward speed.
- `WeakDeflection`: a finger or edge contact with a reduced redirect.
- `Uncontrolled`: a back-of-hand or poorly aligned contact left to PhysX.

Redirect speed is capped at 95% of incoming kinetic energy. A glove touch by
itself is not promoted to a save; normal goal-plane and danger-region outcomes
still apply unless possession was established.

## Geometry

The compound geometry uses a 0.15 x 0.13 m palm and a 0.11 x 0.05 m finger
section. Its maximum radial extent is 0.11 m, equal to the previous sphere
radius, so the change does not expand the goalkeeper's trained reach.

## Evaluation

The `ShotVarietyLab` contact button or `B` key cycles the same fixed replay
through:

1. legacy spherical gloves;
2. the earlier 0.35 / 0.15 bounce candidate;
3. `keeper-glove-handling-v1`.

Terminal telemetry includes the handling outcome, contact region, palm
alignment, incoming/outgoing speed, energy ratio, two-hand geometry, applied
impulse, and possession duration.

The candidate benchmark config is
`configs/benchmarks/goalkeeper-control-v2-human-shot-v1-glove-handling-2k.json`.
The compound glove system is the default in Stage 6 gameplay arenas. The lab
intentionally starts in legacy mode so identical-shot regression comparisons
remain available.

## Promotion Evidence

The corrected paired screen used identical episode key digest
`f5c0c1bd773c48a8c972d61bf6085b82b648d229edebcd8507a695342e1aea23`.
Across 365 expected-on-target shots, Glove Handling v1 improved save rate from
42.47% to 54.25% and reduced contact-then-goal from 15.89% to 2.47%.

The official paired benchmark used identical episode key digest
`9d3f2f60a0c745d3910d4643dc140907a9e95aa8c54031eb3ee17db0884c6d22`.
Across 1,812 expected-on-target shots:

- save rate improved from 43.49% (788 saves) to 55.41% (1,004 saves);
- contact-then-goal fell from 16.78% (304) to 2.81% (51);
- curled saves improved from 38.33% to 52.74%;
- placed saves improved from 57.45% to 71.63%;
- power saves improved from 27.30% to 34.73%;
- invalids, timeouts, mask violations, command clamps, decision lifecycle
  failures, native inference errors, and energy-cap violations were all zero.

The legacy run reported only the legacy contract IDs and the candidate run
reported only the v1 contract IDs. This verifies that the environment selector
was active and the comparison was a real contract A/B test.
