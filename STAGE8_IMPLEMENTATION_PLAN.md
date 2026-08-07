# Stage 8 Goalkeeper Analysis Web Implementation

## Purpose

Stage 8 is a compact ML presentation layer for the completed goalkeeper. It
does not train or modify a model. It turns the fixed paired benchmark into a
clear static React report that can be viewed in any browser, separately from
the Unity penalty game.

The original Unity analysis prototype was rejected because a fixed game canvas
compressed the heatmap and tables into overlapping, unreadable regions. The
React implementation replaces that prototype; Unity contains no Stage 8
analysis scene or runtime code.

## Locked Scope

The page contains exactly two related 4 x 3 heatmaps:

1. Final goalkeeper save rate.
2. Reactive teacher save rate minus final goalkeeper save rate.

The stand-centre comparison is not part of Stage 8. There are no additional
charts, replay tools, PDFs, live telemetry, or training controls.

Each final-policy cell displays save rate, paired shot count, Wilson 95%
confidence interval, and glove-contact rate. Each teacher-gap cell displays the
percentage-point gap, both underlying save rates and intervals, common shot
count, and final-policy glove-contact rate.

Filters are limited to:

- intended target or unopposed actual crossing;
- all, placed, power, or curled shots;
- all, slow, medium, or fast shots.

Supporting report tables contain overall save and goal counts, save-rate
intervals, glove contact and glove saves, contact-then-goal, breakdowns by
height/style/speed/spin, left/right difference, invalids, timeouts, inference
errors, and aggregate contract failures.

## Data Contract

The source is the completed paired fixed benchmark:

```text
results/evaluations/stage8-goalkeeper-heatmap-source-20k/
```

Both policies saw the same 20,000 attempt keys under master seed `20260803`.
The primary population is the same 18,242 expected-on-target shots for each:

- `native_split_v1:seed-001`;
- `reactive_curve_v1`.

Python validates the source and creates:

```text
results/analysis/stage8-goalkeeper-analysis-v1.json
```

The build script copies those exact bytes to:

```text
web/stage8-analysis/public/data/goalkeeper-analysis-v1.json
```

The web application never parses raw evaluation CSV, recalculates rates, or
changes bin definitions. All 32 filter slices and 12 cells per slice are
precomputed by Python. Evidence generation verifies that source, public, and
built data hashes are identical.

## React Presentation

The application lives at `web/stage8-analysis/` and uses React, TypeScript, and
Vite. It is a static site with no backend or network dependency.

The visual hierarchy is:

1. concise title and benchmark identity;
2. four overall headline metrics;
3. the heatmap mode and filters;
4. a full-width 4 x 3 goal grid;
5. conventional supporting tables;
6. source benchmark and episode digest.

The heatmap uses a discrete multi-hue scale with exact values printed in every
cell. Teacher-gap colors use a zero-centred diverging scale and never depend on
red/green distinction alone. On narrow screens the goal grid scrolls
horizontally instead of shrinking text or overlapping sections. Tables also
scroll independently where necessary.

## Verification

React tests verify:

- the frozen artifact schema and all 32 slices;
- exactly 12 visible heatmap cells;
- both requested heatmap modes;
- intended/crossing, style, and speed filters;
- required supporting sections;
- absence of the removed stand-centre view.

The production build must include `index.html`, compiled assets, and a data
file byte-identical to the frozen artifact. Python evidence also checks the
paired benchmark digest, selected ONNX hashes, 20,000/18,242 counts, zero
safety failures, source-tree hash, build-tree hash, and web-test result.

Run the complete handoff with:

```bash
scripts/run_stage8_analysis_handoff.sh
```

Review locally with:

```bash
scripts/open_stage8_analysis.sh
```

Then open `http://127.0.0.1:4178/`. After manual approval, record it with:

```bash
scripts/approve_stage8_analysis.sh
```

## Delivery Rules

- No Unity process is required or launched.
- No audio is used.
- Raw evaluations and generated web builds remain ignored.
- The compact JSON artifact, source code, lockfile, tests, scripts, and evidence
  report are tracked.
- No commit is made until the user explicitly approves the browser result.
