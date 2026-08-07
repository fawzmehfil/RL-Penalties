# Known Limitations

## Goalkeeper and ML

- The final controller is split supervised imitation rather than one end-to-end
  PPO policy. PPO remains part of the earlier learned goalkeeper and robustness
  experiments.
- Exact delayed simulator state is observed; there is no vision model.
- Extreme goal edges remain much harder than central target regions.
- No single Stage 4 policy improved clean, delayed/noisy, speed-OOD, and
  edge-OOD performance together.
- The commit model commits on every evaluated valid shot. It does not model
  keeper indecision or intentional non-commit behavior.

## Physics and Football Scope

- The penalty mark is fixed at 11 m.
- The player is represented by a first-person shooting view; there is no
  rendered body, physical foot/ball collision, run-up biomechanics, body cue,
  or goalkeeper anticipation before contact.
- Shot families approximate placed, power, and curled penalties using bounded
  commands and Magnus acceleration. They are not measured player motion.
- The goalkeeper body uses high-level kinematic control and stylized compound
  collision shapes rather than articulated human dynamics.
- Glove Handling v1 is a bounded deflection model. Experimental catch/punch v2
  was not promoted because it failed the statistical holdout gate.

## Presentation

- The toy-sports goalkeeper deliberately reuses the authoritative primitive
  geometry. It is not anatomically realistic.
- The net ripple, contact flash, stands, and crowd audio are presentation-only.
- There is one pitch, camera flow, goalkeeper, and five-shot mode.
- No gamepad, team selection, player customization, replay viewer, commentary,
  music, online mode, leaderboard, or difficulty selection is included.

## Packaging

- The macOS app is a local demonstration build only.
- It is not signed, notarized, installed through a DMG, submitted to the App
  Store, or guaranteed on clean machines/other platforms.
- Stage 8 analysis is a separate, self-contained static browser artifact
  packaged with the final build and opened outside Unity.
- Training runs and raw 20,000-shot CSV files are excluded from Git; compact
  evidence and hashes are tracked.

## Reproduction Precision

- The frozen Stage 7 and Stage 9 scenes produced identical aggregate saves,
  glove contacts, and commit timing over 400 paired shots. Two paired attempts
  changed save/goal class when the PhysX scene was independently reconstructed.
  The release therefore claims bounded gameplay parity, not bit-identical
  cross-scene physics replay.
