# Reproducibility Guide

## 1. Run the final standalone

The final app requires no Python process or trainer:

```bash
scripts/open_stage9_final.sh
```

It uses the two ONNX files packaged through the Stage 7 runtime manifest. Their
expected hashes are listed in the model card and Stage 9 runtime manifest.

## 2. Verify and build from source

Requirements:

- macOS;
- Unity `6000.0.74f1` installed at the standard Unity Hub path;
- Python 3.10 environment created by `scripts/setup_python.sh`;
- Node/npm for the Stage 8 static analysis artifact.

Run the full muted handoff:

```bash
scripts/verify_stage9_final.sh
```

This validates CC0 audio hashes, all Python tests, the Stage 8 web tests/build,
Stage 9 asset generation, geometry invariance, Stage 9 EditMode/PlayMode tests,
the frozen Stage 7 gameplay regression, and the paired Stage 7/Stage 9
gameplay-parity gate.

Build without launching:

```bash
scripts/build_stage9_final.sh
```

Output:

```text
builds/macos/PenaltyShootoutFinal.app
```

The build is intentionally unsigned and not notarized.

## 3. Reproduce benchmark evidence

Raw training/evaluation artifacts are ignored because they are large. Compact
reports and every benchmark/config contract are tracked. The selected source
benchmark for the final heatmap is:

```text
configs/benchmarks/goalkeeper-control-v2-stage8-heatmap-source-20k.json
```

To regenerate the fixed policy episodes when the Stage 6 evaluation build and
model manifest are available:

```bash
scripts/run_stage8_heatmap_source_20k.sh
scripts/run_stage8_analysis_handoff.sh
```

The source uses 16 arenas, fixed per-arena quotas, master seed `20260803`, and
the same episode keys for the final native policy and reactive teacher. The
analysis report records source, episode, model, and artifact hashes.

## Determinism Boundaries

- Scenario sampling and policy inference are deterministic for fixed contracts
  and seeds.
- The release gate reconstructs the frozen Stage 7 and final Stage 9 scenes and
  runs 400 matching shots through each. Episode keys, aggregate saves
  (`256/400` in both scenes), aggregate glove contacts (`256/400` in both), and
  first-commit decisions matched. Two attempts changed save/goal class across
  the independently reconstructed PhysX scenes, a `0.5%` paired-class drift
  within the declared `1%` tolerance.
- Runtime PhysX is verified on the pinned Unity/macOS environment; exact
  floating-point trajectories are not claimed across independently rebuilt
  scenes, unrelated engine versions, or CPU versions.
- Audio variation is deterministic from session seed and event ordinal.
- Human input and wall-clock rendering are intentionally not deterministic.
