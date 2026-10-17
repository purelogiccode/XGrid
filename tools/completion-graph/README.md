# Completion treemap

A treemap of XGrid's code-completion, matching the ImplementationPlan
assessment (see `ImplementationPlan.md` -> "Project completion assessment").

- **Tile area** = phase weight (share of remaining effort, of 108 points).
- **Tile color** = percent done (red 0% -> yellow 50% -> green 100%).
- Group bands: "LLE core (real console chain)" (phases 0-14) and
  "Performance & finish" (phases 15-23).

## Files

| File | What |
|---|---|
| `completion-data.json` | Source of truth: phase id, name, weight, percent done. |
| `generate.ps1` | Renders the SVG and the self-contained HTML from the JSON. |
| `completion-treemap.svg` | Vector output (open in any browser / editor). |
| `completion-treemap.html` | Same graph + hover tooltips (self-contained). |
| `completion-treemap.png` | Raster preview (Edge headless screenshot of the SVG). |

## Regenerate

```powershell
pwsh -File tools/completion-graph/generate.ps1
```

PNG preview (Edge headless):

```powershell
& "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --headless=new --disable-gpu `
  --screenshot="tools/completion-graph/completion-treemap.png" `
  --window-size=1680,1080 --hide-scrollbars `
  "file:///<repo>/tools/completion-graph/completion-treemap.svg"
```

## Updating the numbers

Edit `completion-data.json` (weights and percents must match the
`ImplementationPlan.md` assessment table), then re-run the generator.
Keep the two in sync in the same commit.

Note for script authors: the generator pins InvariantCulture — pt-BR hosts
would otherwise write `389,6` into SVG coordinates and break every rect.
