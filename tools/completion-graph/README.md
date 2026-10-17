# Completion treemap

A treemap of XGrid's code-completion, matching the ImplementationPlan
assessment (see `ImplementationPlan.md` -> "Project completion assessment").

- **Tile area** = phase weight (share of remaining effort, of 108 points).
- **Completion encoding** — two styles, same layout:
  - `fill` (default look): each tile is a dark slot; the bottom `done%` of
    the tile is filled with color (100% = fully filled, 50% = half, 0% = empty).
  - `solid`: the whole tile is colored by `done%`
    (red 0% -> yellow 50% -> green 100%).
- Group bands: "LLE core (real console chain)" (phases 0-14) and
  "Performance & finish" (phases 15-23).

## Files

| File | What |
|---|---|
| `completion-data.json` | Source of truth: phase id, name, weight, percent done. |
| `generate.ps1` | Renders both styles (SVG + self-contained HTML) from the JSON. |
| `completion-treemap-fill.svg` / `.html` / `.png` | Fill style (progress-slot look). |
| `completion-treemap.svg` / `.html` / `.png` | Solid style (heat-color look). |

## Regenerate

```powershell
pwsh -File tools/completion-graph/generate.ps1
```

PNG previews (Edge headless), one per style:

```powershell
& "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --headless=new --disable-gpu `
  --screenshot="tools/completion-graph/completion-treemap-fill.png" `
  --window-size=1680,1080 --hide-scrollbars `
  "file:///<repo>/tools/completion-graph/completion-treemap-fill.svg"
```

## Updating the numbers

Edit `completion-data.json` (weights and percents must match the
`ImplementationPlan.md` assessment table), then re-run the generator.
Keep the two in sync in the same commit.

Note for script authors: the generator pins InvariantCulture — pt-BR hosts
would otherwise write `389,6` into SVG coordinates and break every rect.
