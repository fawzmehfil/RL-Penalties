# Stage 9 Lightweight Final Presentation and Project Handoff Plan

## Purpose

Stage 9 is the final project stage. It packages the completed ML goalkeeper and
five-shot penalty game as one solid Unity demo, alongside the separate Stage 8
React analysis site and technical report.

The objective is not to imitate the production scope of EA Sports FC. The
objective is to present the work honestly and well:

- the game looks like a deliberate stylized football prototype;
- visible contacts agree with the proven physics geometry;
- the existing penalty mechanics remain unchanged;
- the synthetic placeholder sounds are replaced;
- the ML analysis is available as a separate clean browser report;
- the project can be built and demonstrated locally without Python;
- the methodology, results, and limitations are documented clearly.

Stage 9 performs no training and makes no goalkeeper-performance changes.

## Locked Decisions

### 1. This is not a commercial macOS release

Stage 9 produces a normal local Unity macOS standalone build for demonstration.
It does not include:

- an Apple Developer ID;
- code signing or notarization;
- App Store preparation;
- a DMG or installer;
- Universal Binary requirements;
- an automated public GitHub release;
- commercial release support.

The deliverable may be zipped for convenient transfer, but packaging is kept
to the level needed for a portfolio demo and project handoff.

### 2. Prototype geometry remains visually truthful

The existing goalkeeper shape is not replaced by a realistic humanoid mesh.
The current body, arms, gloves, ball, and goal geometry already communicate
where physical contacts occur. Replacing them with a differently shaped model
would create visible saves where the ball appears not to touch the keeper, or
visible contacts that the simulation does not register.

Stage 9 therefore treats the current primitive construction as the chosen art
style rather than a temporary debug object.

For every collision-relevant object:

- mesh type remains unchanged;
- transform and scale remain unchanged;
- collider remains unchanged;
- visible surface remains aligned to the collider;
- no outer character shell is added;
- no animation is allowed to detach the visible hand from the physical glove;
- no decorative geometry may suggest extra reach.

The keeper can receive new materials, colors, markings, and restrained visual
details that sit on its current surfaces. It cannot receive a silhouette or
proportion redesign.

### 3. No texture pack is needed

Stage 9 will not import a general texture pack.

The scene is small and stylized enough to use project-authored Unity materials:

- a simple grass material with subtle mowing bands;
- clean painted-white goal and field markings;
- a small set of flat kit and glove materials;
- simple concrete/seating colors for the background;
- one neutral sky and daylight setup.

A texture pack would add download size, licensing work, visual inconsistency,
and unused assets without solving a real problem. If a specific surface remains
visibly inadequate during manual review, one individually selected CC0 texture
may be proposed later, but it requires explicit user approval before import.

### 4. Approval comes before commits

Stage 9 implementation remains uncommitted while the visual and audio design is
being reviewed.

The workflow is:

1. Implement one bounded presentation phase.
2. Verify it technically with Unity minimized and audio muted.
3. Let the user inspect it manually.
4. Revise or reject it without disturbing the frozen baseline.
5. Commit only after the user explicitly approves the result.

No Stage 9 commit or push is automatic.

### 5. Unity stays minimized and silent during development

Unity MCP may be used to:

- inspect scenes, prefabs, components, materials, and import settings;
- read Console output;
- run EditMode and PlayMode tests while the editor remains minimized;
- inspect Game View frames or screenshots;
- verify object alignment and scene hierarchy;
- prepare local builds without launching them.

During automated or MCP-driven work:

- Unity must not be focused or brought to the front;
- audio must be muted before any PlayMode operation;
- no standalone build is launched;
- no sound file is previewed;
- the user continues using the laptop normally.

The user manually opens the scene/build and approves visuals and audio when
ready.

### 6. A coherent lightweight theme is required

Stage 9 is not an unstyled cleanup pass. The final game uses the locked
`rounded-football-v1` visual language throughout: Wii-era sports clarity,
rounded primitive characters, minimal faces, matte solid colors, soft daylight,
and compact circular/capsule UI motifs. This is an inspiration point, not a
copy of Nintendo characters, proportions, interface assets, or branding.

The theme must remain lightweight and must fit the verified prototype geometry.
Presentation can change materials, color blocking, facial markings, background
shapes, and UI treatment; it cannot change any collision-relevant silhouette.
The same visual language must cover the keeper, optional shooter, pitch, goal,
venue, HUD, and menus so the Unity deliverable looks like one deliberate small
football game. The Stage 8 site remains a related but separate technical view.

## Final Deliverable

Stage 9 produces:

```text
builds/macos/PenaltyShootoutFinal.app
docs/TECHNICAL_REPORT.md
docs/MODEL_CARD_GOALKEEPER_CONTROL_V2.md
docs/USER_GUIDE.md
docs/REPRODUCIBILITY.md
docs/KNOWN_LIMITATIONS.md
docs/THIRD_PARTY_ASSETS.md
docs/stage9-final-report.json
```

The standalone starts directly in the polished five-shot penalty game. The
Stage 8 analysis remains a separate static web deliverable; it is not embedded
inside Unity. Final documentation links to both artifacts.

The application runs native goalkeeper inference and requires no Python,
ML-Agents trainer, TensorBoard, or network connection.

## Frozen ML and Simulation Contracts

| Concern | Final contract |
|---|---|
| Goalkeeper behavior | `GoalkeeperControl-v2` |
| Controller | native split seed 001 |
| Observation | `control-state-v2-gameplay-v1`, 35 floats |
| Observation delay | two fixed ticks / 40 ms |
| Actions | four continuous controls plus commit `[2]` |
| Motor | `keeper-control-forward-v1` |
| Glove behavior | `keeper-glove-handling-v1` |
| Shot input | `player-penalty-input-v1` |
| Shot command | `player-shot-v1` |
| Flight physics | `football-flight-v1` |
| Interactive suite | `player-interactive-v1` |
| Match flow | `penalty-set-v1` |
| Replay | `penalty-replay-v1` |
| Analysis | frozen Stage 8 analysis artifact |

Selected model hashes:

```text
interception:
ad95050acb5032abffd005e9d5ddf78b8e1c362d79a5d9871b05c50a342b20b0

timing:
26c3a80b375574a4e1c02b97183e2ab390736eae76879296ad3daaf85492850b
```

Stage 9 must not change observations, actions, model outputs, motor timing,
glove dimensions, contact handling, shot physics, outcome resolution, or replay
semantics.

## Scope

Stage 9 includes only:

1. Prototype-faithful goalkeeper material and visual polish.
2. A simple matching penalty-taker presentation if it passes visual review.
3. Lightweight pitch, goal, net, background, lighting, and camera polish.
4. A restrained gameplay HUD pass.
5. Replacement of synthetic sounds with properly licensed sound effects.
6. Clear final documentation linking the Unity game and Stage 8 web report.
7. Final documentation, verification, and local standalone build.

Explicitly out of scope:

- goalkeeper retraining or calibration;
- humanoid goalkeeper replacement;
- skeletal keeper animation or root motion;
- a larger or differently shaped goalkeeper model;
- a new catch, punch, contact, or reach system;
- high-detail stadium or crowd simulation;
- team selection, player customization, branded kits, or real likenesses;
- multiple stadiums, weather, day/night, or camera choices;
- commentary, music, celebrations, cinematic replays, or cutscenes;
- online play, accounts, leaderboards, or telemetry upload;
- gamepad support unless separately approved;
- additional analysis views beyond the Stage 8 contract;
- Windows, Linux, mobile, browser, or console packaging;
- commercial distribution work.

## Visual Direction

### Style contract: `rounded-football-v1`

Stage 9 uses a lightweight **rounded toy-football** style. The reference is the
clarity and personality of Wii-era sports avatars: simple shapes, minimal faces,
solid colors, soft lighting, and expressive poses. It must not copy Nintendo's
Mii proportions, face library, UI, branding, or exact character designs.

The current keeper already uses the right building blocks for this direction:
spheres, capsules, clean color blocks, and readable limbs. Stage 9 makes that
language consistent across the complete experience instead of attempting to
hide it.

The style contract applies to:

- goalkeeper and optional shooter;
- ball treatment;
- pitch, goal, net, and venue;
- reticle, power/composure controls, menus, and outcome text;
- contact effects and transitions;
- icon and app-mark design.

Every Stage 9 asset must read as part of the same small game. Do not mix
realistic characters, photographic grass, flat debug materials, and glossy
mobile-game UI.

### Character language

- Rounded spheres and capsules remain the primary forms.
- Surfaces are matte with broad, soft highlights.
- Faces use only flat project-authored eyes, brows, and an optional simple
  mouth. These markings add no geometry or collider depth.
- Hair, if used, is a flat color region inside the existing head silhouette.
- Kits use two or three solid color regions with no real team branding.
- Gloves remain visually prominent so saves are immediately understandable.
- Personality comes from color, pose, and timing rather than anatomical detail.
- The keeper's exact existing dimensions remain locked even if the optional
  non-physical shooter uses slightly different proportions.

### Intentional primitive football style

The final look should resemble a compact, readable training-game visualization,
not an unfinished attempt at realism.

Design principles:

- simple geometric players are a deliberate style;
- rounded toy-like forms and minimal faces create a recognizable theme;
- every shape has a clear gameplay purpose;
- colors separate the keeper, shooter, ball, pitch, frame, and UI;
- materials have enough shading to convey volume without noisy detail;
- the arena feels like a penalty area without becoming a stadium art project;
- the visual hierarchy keeps the goal, keeper, ball, and reticle dominant;
- no decorative object competes with the shot;
- all added design must survive `1280 x 800` and laptop performance limits.

The test for success is that a screenshot looks like a deliberately designed
small sports game, not a physics lab with extra decoration and not a failed
attempt at realism.

### Color direction

- Pitch: medium natural green with two subtle mowing tones.
- Goal and markings: soft white rather than emissive white.
- Goalkeeper body: deep blue or teal.
- Goalkeeper gloves: high-contrast amber/yellow retained for contact clarity.
- Penalty taker: restrained red or white kit that cannot be confused with the
  goalkeeper.
- Ball: white with a simple project-authored dark panel pattern.
- Background: neutral gray concrete and muted seating blocks.
- HUD: charcoal translucent surfaces, white text, amber power/composure, and
  cyan curve indication.

No gradients, glowing outlines, branded graphics, fake sponsor boards, or
single-color visual wash are added.

### Shape and UI consistency

- World props use simple repeated shapes and restrained rounded edges.
- HUD indicators echo the character language through circles, arcs, and short
  capsule bars.
- Menus remain compact and use no more than 8 px corner radii.
- Icons use simple filled or outlined symbols with consistent stroke weight.
- Contact effects use one small ring or soft burst, not realistic sparks.
- Scene transitions use the existing short fades without decorative animation.
- Stage 8 stays analytical but reuses the typography, palette, and controls so
  it still belongs to the same application.

## Goalkeeper Visual Polish

### Preserve the existing renderer geometry

Do not hide the current keeper renderers and place another model over them.
Polish the renderers already attached to the authoritative primitive parts.

Add `GoalkeeperPresentationV1` only for presentation state such as material
selection and contact feedback. It may read motor/contact state but cannot move,
rotate, or scale keeper parts.

Allowed changes:

- replace prototype materials with a consistent keeper kit palette;
- assign related but distinct shirt, shorts/leg, skin/head, and glove colors;
- apply minimal project-authored face markings directly to the existing head
  sphere;
- add a flat shirt-number decal that follows the torso surface;
- add subtle roughness and highlight differences between fabric and gloves;
- add a short material response on the glove that made contact;
- improve cast/receive shadow settings;
- improve material readability under the selected daylight.

Disallowed changes:

- replacing a capsule or sphere mesh;
- changing any primitive scale or transform;
- enlarging visible hands, arms, torso, legs, or head;
- adding cloth, hair, sleeves, or pads outside the existing silhouette;
- moving gloves for presentation separately from the motor;
- using skinned meshes or IK;
- adding presentation colliders;
- hiding a collider inside a visibly smaller object.

### Contact readability

When the ball contacts a glove or keeper body:

- the correct existing part may receive a subtle `0.08-0.12 s` material flash;
- a restrained contact particle may appear exactly at the recorded contact
  point;
- the effect must not imply a catch/punch that did not occur;
- the effect must not obscure ball direction;
- one physics contact produces at most one visible response.

This makes registered saves easier to read without changing what qualifies as
a save.

### Geometry acceptance

A release validation test records every collision-relevant renderer and checks:

- primitive mesh identity matches the frozen baseline;
- local position, rotation, and scale match the frozen baseline;
- corresponding collider type and dimensions match the frozen baseline;
- no additional collider exists under presentation objects;
- visible glove centre remains equal to physical glove centre;
- no presentation component writes to keeper transforms.

Any mismatch blocks Stage 9 approval.

## Penalty-Taker Presentation

The existing Stage 7 game does not require a detailed footballer. To keep Stage
9 coherent, use a small presentation-only primitive player assembled in the
same visual language as the keeper.

The initial implementation includes:

- capsule torso and limbs;
- sphere head;
- the same minimal face, matte material, and kit-language rules as the keeper;
- simple kit materials;
- a very short deterministic run-up/strike pose sequence;
- no visible foot collider and no physical interaction with the ball.

The existing kernel launch event remains authoritative. The shooter animation
is synchronized to that event and never causes it.

If the primitive kicker looks worse than the current clean behind-ball view,
remove it. A hidden shooter with a clear strike sound is preferable to an
awkward character. This is a manual visual decision and does not block the rest
of Stage 9.

## Pitch And Goal

### Pitch

Use one project-authored material based on color, roughness, and subtle
world-space mowing bands. No downloaded grass texture is required.

Add or refine only the football markings visible from the penalty camera:

- goal line;
- penalty spot;
- penalty-area line where visible;
- optional goal-area line where visible.

All markings are visual only. The existing ground plane and physics material
remain authoritative.

### Goal frame

Retain the existing goal frame meshes, transforms, and colliders. Improve only:

- frame material;
- lighting response;
- edge smoothness/import settings if this does not alter dimensions;
- visual connection between posts and crossbar.

Do not add a larger decorative frame over the colliders.

### Net

Add a lightweight visual net behind the existing frame:

- no collider;
- no cloth simulation;
- no ball-force response;
- one simple grid material or low-resolution procedural mesh;
- optional small event-driven ripple on goals only;
- deterministic reset between attempts.

The net is background feedback. It never determines whether a shot is a goal.

## Background And Lighting

### Lightweight venue

Create enough context to avoid an empty development plane:

- one low-detail stand shape behind and around the goal;
- a few muted seating-color bands;
- one tunnel or dark background opening if composition needs it;
- repeated rounded/simple forms that match the toy-football character style;
- no modeled spectators;
- no advertising text;
- no decorative props close to the play area.

This is a background silhouette, not a stadium system.

### Lighting

Use the existing URP setup with:

- one directional daylight;
- ambient sky lighting;
- realtime shadows only for keeper, optional shooter, ball, frame, and nearby
  ground;
- low-cost background rendering;
- anti-aliasing sufficient for the goal and net;
- no depth of field, motion blur, bloom, chromatic aberration, or heavy
  post-processing by default.

The scene should look clean through materials, framing, and contrast rather than
effects.

## Camera

Preserve the existing Stage 7 camera behavior unless a specific framing problem
is found.

Allowed changes:

- small position/FOV adjustments to include the optional primitive shooter;
- slower, smoother transition values;
- stable result framing that keeps the keeper and ball visible;
- a reduced-motion setting that keeps the camera nearly fixed.

Do not add cinematic cuts, slow motion, orbit cameras, camera shake, or replay
angles. Aim-lock coordinates must remain stable through camera changes.

## HUD And Navigation

Keep the Stage 7 gameplay information and controls. Apply a restrained styling
pass rather than redesigning the interaction.

Required gameplay HUD:

- shot number;
- goals, saves, and misses;
- reticle and composure ring;
- power bar;
- curve indicator;
- terminal outcome;
- pause menu;
- set-complete menu.

Changes:

- standardize margins, sizes, colors, and text hierarchy;
- apply the `rounded-football-v1` circles, arcs, capsule bars, and icon rules;
- reduce the prototype look of generated panels;
- keep all controls readable at `1280 x 800`;
- add `Analysis` to pause and set-complete menus;
- add Master, Effects, and Ambience volume settings;
- show model/build information only in a small About section.

The Stage 8 React site remains separate, 2D, and silent. It is not embedded as
an in-world stadium screen or Unity scene.

## Audio

### Replace the prototype tones

`Stage7PenaltyAudioV1` currently generates synthetic strike, glove, frame,
goal, and UI tones. Stage 9 replaces them with a small versioned audio library.

Add:

```text
stage9-audio-v1
Stage9AudioLibraryV1.asset
Stage9PenaltyAudioV1.cs
Stage9AudioMixer.mixer
```

Required event groups:

| Event | Target variations |
|---|---:|
| Ball strike | 3 |
| Glove contact | 3 |
| Body/arm contact | 2 |
| Post/crossbar | 2 |
| Goal/net | 2 |
| Ground bounce | 2 |
| UI confirm/back | 2 |
| Low stadium ambience | 1 loop |
| Goal/save/miss reaction | 1 each |

This is intentionally small. More clips are not automatically better.

### Sound sourcing

Use project-recorded or individually verified CC0 clips. Suitable first sources
include:

- [Kenney Impact Sounds](https://www.kenney.nl/assets/impact-sounds);
- [Kenney Interface Sounds](https://kenney.nl/assets/interface-sounds);
- individually verified CC0 files from
  [Freesound](https://freesound.org/help/faq/).

Every selected file records title, creator, source URL, license, download date,
and SHA-256 in `docs/THIRD_PARTY_ASSETS.md`. Do not use non-commercial,
share-alike, unclear, or extracted commercial-game audio.

### Playback behavior

- World impacts use 3D spatial sound at the exact event position.
- UI sounds remain 2D.
- The ambience loop stays low and stops cleanly on scene changes.
- Variation selection is deterministic from session seed and event ordinal.
- Pitch variation is bounded to `+/-3%`.
- Contact volume may use existing measured impact speed.
- One gameplay event produces at most one sound.
- No continuous music is added.

### Silent development requirement

All automated tests and MCP-driven PlayMode work force Master volume to zero
before entering Play Mode. The tests validate event selection and counts without
playing audible output.

Audio clips are never previewed through MCP. The user performs the only
subjective audio review and may reject or replace clips before any commit.

## Asset Policy

Stage 9 should contain very few new binary assets:

- selected audio clips;
- optional project-authored app icon;
- optional tiny project-authored ball/kit decals;
- no character pack;
- no animation pack;
- no general texture pack;
- no HDRI pack;
- no Unity Asset Store pack.

Add `docs/THIRD_PARTY_ASSETS.md` with:

- local path;
- source title and creator;
- exact URL;
- license and license URL;
- source and imported SHA-256;
- modifications;
- whether the file is committed or only bundled locally.

Missing or unclear licensing blocks approval.

## Stage 9 Scene Architecture

Create Stage 9 copies rather than modifying frozen Stage 6/7/8 assets:

```text
Assets/PenaltyShootout/Prefabs/Stage9PlayableArena.prefab
Assets/PenaltyShootout/Prefabs/Stage9GameplayHud.prefab
Assets/PenaltyShootout/Scenes/PenaltyShootoutFinal.unity
web/stage8-analysis/dist/
```

The Stage 9 arena copy preserves all authoritative components and adds only:

- material assignments;
- presentation-only keeper feedback;
- optional primitive shooter;
- non-colliding net and background;
- final lighting/camera/HUD/audio components.

Stage 7 and Stage 8 scenes remain reproducible rollback baselines.

## Documentation

### Technical report

Create `docs/TECHNICAL_REPORT.md` as the main portfolio document. It should
explain:

- project goal and environment design;
- why using ML-Agents PPO still makes this an RL project;
- Stage 1 kernel validation;
- Stage 2 observation/reward/training setup;
- Stage 3 benchmark and multi-seed evidence;
- Stage 4 robustness experiments;
- Stage 5 richer control attempts, commit collapse, and split-supervision fix;
- Stage 6 human-like shots, delay, curve, forward contact, and glove handling;
- Stage 7 playable controls;
- Stage 8 heatmaps and final statistical findings;
- final selected model and limitations.

Use committed JSON evidence and honest metrics. Do not combine incompatible
benchmarks into one headline number.

### Model card

Create `docs/MODEL_CARD_GOALKEEPER_CONTROL_V2.md` with:

- intended use;
- observation/action contracts;
- training and demonstration provenance;
- model hashes;
- canonical and human-shot performance;
- heatmap weaknesses;
- robustness limitations;
- runtime requirements;
- inappropriate uses.

### User and reproduction guides

`docs/USER_GUIDE.md` explains controls, five-shot flow, settings, analysis site,
and replay location.

`docs/REPRODUCIBILITY.md` distinguishes:

1. Running the standalone with no Python.
2. Building from the Unity project.
3. Reproducing benchmark evidence using pinned Python tooling.

`docs/KNOWN_LIMITATIONS.md` records the stylized motor, fixed penalty spot,
limited player presentation, procedural shot assumptions, robustness results,
and absence of commercial packaging.

No PDF is produced.

## Local Build

Add:

```text
Assets/PenaltyShootout/Editor/Stage9ProjectBuilder.cs
scripts/prepare_stage9_final.sh
scripts/verify_stage9_final.sh
scripts/build_stage9_final.sh
```

### Prepare

The prepare script:

1. Validates required Stage 7, Stage 8, model, and evidence assets.
2. Validates third-party audio licenses and hashes.
3. Runs Python contract tests.
4. Runs Unity EditMode and PlayMode tests while muted.
5. Prepares Stage 9 copied scenes and prefabs.
6. Produces an ignored candidate report.
7. Does not launch Unity or a standalone build.

### Verify

The verification script:

1. Checks prototype geometry invariance.
2. Checks simulation outcome invariance.
3. Checks gameplay-to-analysis navigation.
4. Validates replay output.
5. Captures muted screenshots at supported resolutions.
6. Measures basic frame rate and build errors.
7. Stops for user visual/audio approval.

### Build

After approval, the build script creates:

```text
builds/macos/PenaltyShootoutFinal.app
```

It records the Git commit, Unity version, model hashes, Stage 8 data hash, and
build executable hash in `docs/stage9-final-report.json`.

No signing, notarization, installer, or automatic upload step is included.

## Verification

### Geometry invariance

Compare the frozen Stage 7 arena and Stage 9 arena programmatically.

For ball, goal frame, keeper root, torso, head, arms, forearms, gloves, and all
contact colliders, require:

- same transform hierarchy;
- same local position, rotation, and scale;
- same primitive/mesh identity;
- same collider type and dimensions;
- same Rigidbody properties;
- same physics material;
- same layer and contact marker;
- no new transform writer.

Materials and renderer shadow settings may differ. Geometry may not.

### Simulation invariance

Run the frozen and Stage 9 arenas on the same paired 400 fixed shots with the
same native models.

Require:

- identical episode-key digest;
- identical terminal outcome for every shot;
- identical keeper command digest;
- identical contact-part sequence;
- no new contact source;
- no invalids, timeouts, masks, clamps, duplicate submissions, missing actions,
  or inference errors.

If any outcome changes, treat it as a Stage 9 defect. Do not accept it as a
visual side effect.

### EditMode

- Frozen ML, shot, motor, glove, and replay IDs remain unchanged.
- Presentation components do not write authoritative transforms.
- Net/background/shooter contain no ball-contact collider.
- Contact feedback uses exact drained events and emits once.
- Audio routing and deterministic variation selection.
- Missing or unlicensed audio fails configuration validation.
- Stage 8 data artifact loads unchanged.
- Scene list and navigation are valid.

### PlayMode

- Complete one five-shot set with native inference while muted.
- Pause, restart, fullscreen, and focus behavior remain correct.
- Open analysis and return to a fresh set.
- Net and contact effects reset between shots.
- Optional shooter never affects launch or collision.
- Audio event counts match gameplay events without audible output.
- Replays continue validating as `penalty-replay-v1`.
- No Console errors or missing materials.

### Visual review

Inspect manually at:

```text
1920 x 1080
1440 x 900
1280 x 800
```

Approve only when:

- the simple visual style looks intentional;
- keeper, optional shooter, arena, HUD, and effects clearly share the same
  rounded toy-football design language;
- the keeper still has the exact readable prototype silhouette;
- every glove/body save visually corresponds to contact;
- no added detail implies unregistered reach;
- the optional shooter improves rather than obstructs the shot;
- the goal, ball, keeper, reticle, power, and outcome remain readable;
- the net and background add context without distraction;
- HUD elements fit without overlap;
- Stage 8 remains clear and separate;
- there are no missing materials, blank views, or visual artifacts.

### Audio review

The user manually listens on laptop speakers and headphones.

Approve only when:

- strike sounds like football contact;
- glove and body contacts are distinguishable;
- frame contact is clear but not painfully loud;
- goal, save, and miss reactions are restrained;
- ambience is subtle;
- repeated shots do not sound identical;
- no events double-play;
- sliders and mute work;
- scene changes do not leak audio.

### Performance

On the target laptop at `1440 x 900`:

- target 60 FPS during a five-shot set;
- no repeated frame spikes over 50 ms;
- no meaningful performance regression from Stage 7;
- no persistent allocations from effects or audio;
- analysis filters remain immediate;
- standalone build remains reasonably sized.

If performance regresses, simplify shadows, net, background, and effects. Do
not modify physics or inference to recover presentation performance.

## Implementation Order

### Phase 0: Freeze and compare

- Complete Stage 8 first.
- Record frozen Stage 7 geometry and paired 400-shot results.
- Create Stage 9 scene/prefab copies.
- Add automated geometry invariance checks.

Exit: there is a verified rollback baseline before visual work.

### Phase 1: Prototype-faithful visual pass

- Finalize project-authored material palette.
- Apply materials without geometry changes.
- Add minimal contact feedback.
- Add pitch markings, simple net, and lightweight background.
- Tune one daylight setup.
- Use MCP for silent minimized verification.

Exit: user visually approves the keeper, goal, pitch, and venue direction.

### Phase 2: Optional primitive shooter and camera

- Add the matching primitive penalty taker.
- Synchronize its visual strike to the authoritative launch.
- Make only necessary camera adjustments.
- Compare with the shooter disabled.

Exit: user chooses the better version. The shooter is removed if it harms the
presentation.

### Phase 3: HUD and analysis navigation

- Apply the restrained HUD styling pass.
- Add analysis navigation.
- Verify both supported aspect-ratio families.
- Keep Stage 8 visually separate and silent.

Exit: complete gameplay-to-analysis flow passes manual review.

### Phase 4: Audio replacement

- Select the smallest acceptable CC0 sound set.
- Record every license and hash.
- Implement mixer, spatial events, variations, and settings.
- Verify event counts silently.
- Hand audio review entirely to the user.

Exit: user approves the sound set and no synthetic Stage 7 tone remains in the
Stage 9 scene.

### Phase 5: Documentation and final build

- Write the technical report, model card, user guide, reproduction guide, and
  limitations.
- Run all invariance and integration checks.
- Build the local standalone.
- Have the user perform the final gameplay, analysis, and audio review.
- Leave all Stage 9 changes uncommitted until explicit approval.

Exit: final approval is recorded.

### Phase 6: Approved commit and handoff

Only after explicit approval:

- inspect Git status and separate unrelated Unity/MCP changes;
- create scoped commits for presentation, audio, verification, and docs;
- push to `main` only when requested;
- record the committed source hash in the final report;
- rebuild once from the approved commit if a final committed build is needed.

## Suggested Commit Structure After Approval

1. `feat(stage9): add prototype-faithful final presentation`
2. `feat(stage9): replace prototype gameplay audio`
3. `test(stage9): verify geometry and simulation invariance`
4. `docs(stage9): add final technical handoff`

These commits are suggestions only. None are created before user approval.

Do not mix the Unity MCP package changes, existing Stage 6 prefab modification,
Stage 8 work, downloaded audio, and Stage 9 runtime code into one commit.

## Stop Conditions

Stop and retain the previous version if:

- any collision-relevant visible shape changes;
- a save no longer looks like registered contact;
- presentation changes alter a terminal outcome or keeper command;
- the optional shooter looks awkward or obscures gameplay;
- a texture or character pack becomes necessary to make the chosen direction
  coherent;
- an audio license cannot be proven;
- the design starts expanding into a full stadium/character production;
- the user does not approve the visual or audio result;
- the standalone requires Python or a trainer;
- performance materially regresses.

The response is to simplify or roll back presentation work, not retrain the
goalkeeper.

## Definition Of Done

Stage 9 and the project are complete when:

- the five-shot game runs locally and the Stage 8 static analysis opens in a browser;
- the goalkeeper retains the exact prototype geometry and contact readability;
- materials, pitch, goal, net, background, camera, and HUD look simple but
  intentional;
- the complete presentation follows `rounded-football-v1` rather than mixing
  prototype, realistic, and unrelated asset styles;
- the optional primitive shooter is included only if it genuinely improves the
  presentation;
- synthetic prototype sounds are replaced with user-approved licensed audio;
- no texture or character pack is required;
- native inference works offline with no Python process;
- geometry and paired simulation invariance checks pass;
- no invalids, timeouts, inference failures, or replay regressions occur;
- the technical report, model card, user guide, reproducibility guide, licenses,
  and limitations are complete;
- the local macOS build runs reliably on the target laptop;
- the user approves visuals and audio before any Stage 9 commit is created.
