# Completion treemap

A treemap of XGrid's code-completion, matching the ImplementationPlan
assessment (see `ImplementationPlan.md` -> "Project completion assessment").

## The default view: solid

**`completion-treemap.svg` / `.html` / `.png` (the solid heat-color style) is
the repo's canonical completion graph.** Tiles are fully colored by progress:

- **Tile area** = phase weight (share of remaining effort, of 108 points).
- **Tile color** = percent done (red 0% -> yellow 50% -> green 100%).
- Group bands: "LLE core (real console chain)" (phases 0-14) and
  "Performance & finish" (phases 15-23).

## Alternate styles (kept for reference)

- `squares` (unit-square mosaic): each tile holds a 10x10 grid of 100 unit
  squares; the bottom `done` squares are filled.
- `fill`: each tile is a dark slot; the bottom `done%` of the tile is filled.

## Files

| File | What |
|---|---|
| `completion-data.json` | Source of truth: phase id, name, weight, percent done. |
| `generate.ps1` | Renders the SVG + self-contained HTML (+ optional PNG) from the JSON. |
| `completion-treemap.svg` / `.html` / `.png` | **Default** (solid heat-color style). |
| `completion-treemap-fill.*` | Fill style (progress-slot look). |
| `completion-treemap-squares.*` | Unit-square mosaic style. |

## Regenerate

```powershell
# default: refresh the canonical solid graph
pwsh -File tools/completion-graph/generate.ps1

# include PNG screenshots (needs Edge; runs headless)
pwsh -File tools/completion-graph/generate.ps1 -Png

# alternates
pwsh -File tools/completion-graph/generate.ps1 -Style fill -Png
pwsh -File tools/completion-graph/generate.ps1 -Style squares -Png
pwsh -File tools/completion-graph/generate.ps1 -Style all -Png
```

## Updating the numbers

Edit `completion-data.json` (weights and percents must match the
`ImplementationPlan.md` assessment table), then re-run with `-Style all -Png`
so every committed variant stays current. Keep data and plan in sync in the
same commit.

Note for script authors: the generator pins InvariantCulture — pt-BR hosts
would otherwise write `389,6` into SVG coordinates and break every rect.
