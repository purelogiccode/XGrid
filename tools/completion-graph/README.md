# Completion treemap (legacy)

The treemap files are a **historical snapshot** of an earlier product plan. They
are kept for reference and are not part of the current documentation, build or
release process.

The current roadmap is `ImplementationPlan.md`; the user documentation starts at
`docs/index.md`.

## Files

| File | What |
|---|---|
| `completion-data.json` | Frozen phase data used by the renderer. |
| `generate.ps1` | Renders the SVG + self-contained HTML (+ optional PNG) from the JSON. |
| `completion-treemap.svg` / `.html` / `.png` | The frozen graph. |

## Rendering

```powershell
pwsh -File tools/completion-graph/generate.ps1
pwsh -File tools/completion-graph/generate.ps1 -Png
```

The PNG preview needs Edge and runs headless. The renderer pins InvariantCulture
so a comma-decimal host cannot corrupt SVG coordinates.

Do not treat these numbers as current status.
