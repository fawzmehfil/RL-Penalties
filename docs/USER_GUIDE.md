# Penalty Shootout Final: User Guide

## Start

Build once with:

```bash
scripts/build_stage9_final.sh
```

Launch the local demo with:

```bash
scripts/open_stage9_final.sh
```

The game starts directly behind the penalty spot. One set contains five valid
shots.

## Controls

| Action | Mouse/trackpad | Keyboard |
|---|---|---|
| Aim | Move pointer | Arrow keys |
| Lock aim and start power | Hold left mouse | Hold Space |
| Shoot | Release left mouse | Release Space |
| Bend left/right | Q / E | Q / E |
| Pause | Escape | Escape |

Aim locks when charging begins. Holding longer increases power. The shrinking
composure ring determines contact quality when charging starts. Q/E adjusts
sidespin before or during charging.

## Scoring

- `Goal`: player scores.
- `Saved` or `BlockedThenOut`: goalkeeper save.
- Frame, wide, or high: miss.
- A technical invalid/timeout does not consume a scored shot and offers a retry.

After five valid shots the results panel shows goals, saves, and misses. Use
`Play Again` for a fresh set.

## Pause and Settings

The pause menu provides resume, restart, fullscreen, analysis, audio/about, and
quit controls. Master, effects, and ambience levels are stored locally.

`Analysis` opens the separate Stage 8 static heatmap in the default browser.
It reports fixed benchmark evidence; it does not analyze the current five-shot
set.

## Replays

Each completed set writes a `penalty-replay-v1` JSON file under Unity's
`Application.persistentDataPath/Replays/` directory. Replay capture does not
upload data. A disk-write failure does not change gameplay.

## Troubleshooting

- The demo is a local unsigned macOS build. If macOS blocks first launch, use
  Finder's Open command for the app.
- Python and TensorBoard are not needed to play.
- If the analysis link does not open, run `scripts/open_stage8_analysis.sh`.
- Use Restart Set after changing focus or input settings if a held input feels
  stale.
