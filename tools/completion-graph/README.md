# Completion treemap

A treemap of XGrid's code-completion, matching the ImplementationPlan
assessment (see `ImplementationPlan.md` -> "Project completion assessment").
Last refreshed **2026-10-22**: weighted **77.7%** (83.89 of 108 weight-points,
24 phases).

## The canonical view: solid

**`completion-treemap.svg` / `.html` / `.png` (the solid heat-color style) is
the repo's canonical completion graph.** Tiles are fully colored by progress:

- **Tile area** = phase weight (share of remaining effort, of 108 points).
- **Tile color** = percent done (red 0% -> yellow 50% -> green 100%).
- Group bands: "LLE core (real console chain)" (phases 0-14) and
  "Performance & finish" (phases 15-23).

## Alternate styles (on demand only)

The `fill` and `squares` variants are no longer committed (removed
2026-10-20); the solid set is the only versioned graph. The generator still
supports them ad hoc (`-Style fill`, `-Style squares`) if a one-off rendering
is useful.

## Files

| File | What |
|---|---|
| `completion-data.json` | Source of truth: phase id, name, weight, percent done. |
| `generate.ps1` | Renders the SVG + self-contained HTML (+ optional PNG) from the JSON. |
| `completion-treemap.svg` / `.html` / `.png` | **The canonical graph** (solid heat-color style); the only committed variant. |

## Regenerate

```powershell
# canonical graph (SVG + HTML)
pwsh -File tools/completion-graph/generate.ps1

# include the PNG preview (needs Edge; runs headless)
pwsh -File tools/completion-graph/generate.ps1 -Png
```

## Updating the numbers

Edit `completion-data.json` (weights and percents must match the
`ImplementationPlan.md` assessment table), then re-run with `-Png` so the
committed SVG/HTML/PNG stay current. Keep data and plan in sync in the same
commit.

Note for script authors: the generator pins InvariantCulture — pt-BR hosts
would otherwise write `389,6` into SVG coordinates and break every rect.
