# RepoFleet worker contract — XGrid

This repository is developed by RepoFleet workers. A worker runs `opencode`
inside this checkout with a task command written by the admin. The repository,
not the task command, owns the rules.

## The short instruction (paste this into RepoFleet)

Generate the task title and command with:

```powershell
pwsh -File scripts/status.ps1 -Prompt
```

Paste the text after `Title:` into the RepoFleet task title, and everything from
`Work only...` to the end into the command. The `Repository:` line is
informational. The command is a thin pointer to the active task; do not restate
the rules in RepoFleet.

## What the worker must do

1. Start with `pwsh -File scripts/status.ps1 -Check -Show`; if the check fails,
   stop and report.
2. Read `ImplementationPlan.md` -> "Execution plan (current)" and the section
   named by the task (`E2` for D1, `E3` for D2, and so on).
3. Work only on the active task. No second task, no docs refresh, no "while I
   am here".
4. The first unchecked box is the price measurement. If the ceiling is below
   10% wall / 20% engine, record a negative and stop.
5. If the ceiling clears the bar, follow the checkboxes in order: acceptance
   test first, smallest implementation, exact 1B anchors, 3 interleaved pairs,
   the definition of done.
6. Close with `pwsh -File scripts/status.ps1 -Check -Sync`.
7. Commit code + tests + status + graph in one commit:
   `area: imperative summary (task id)`.
8. Finish with: task id, changed files, evidence (numbers/anchors), stop
   condition.

## What the worker must not do

- Do not edit `tools/completion-graph/completion-data.json` or the generated
  status blocks by hand; run `status.ps1 -Sync`.
- Do not retry a measured negative; the `negatives` table in `docs/status.json`
  is binding.
- Do not run the full 22-row corpus after a slice; re-verify only the affected
  rows.
- Do not create or switch branches; RepoFleet stays on main/master.
- Do not commit `.repofleet-task.md`.
- Do not leave uncommitted work; the harness's fallback message is generic
  (`<title> (RepoFleet task #N)`), so the task-id protocol is lost.
- Do not rewrite the status narrative in `next-session.md`, `AGENTS.md`, the
  decisions file or the plan; the generated block is the status.

## RepoFleet mechanics

- One active task per repository is enforced by the server
  (`IX_AgentTasks_ActiveRepo`). That is the WIP limit; keep it.
- The worker writes the command to `.repofleet-task.md` and deletes it after the
  run.
- The harness commits any remaining changes as `<title> (RepoFleet task #N)` and
  pushes. If the agent commits itself, the harness pushes that commit. The task
  command asks the agent to commit so the message follows the repo protocol.
- The harness refuses to start on a dirty or unpushed checkout.
- `ExtraInstructions` is machine-wide; do not put XGrid rules there.

## Why this shape

The task command is generated from `docs/status.json`, so it cannot name the
wrong task. The rules live in the repo, so they are versioned and every worker
sees them. The per-repo queue is the WIP limit. The `status.ps1 -Check` gate
makes status/plan/graph drift a hard failure, not a review comment.

## The generated command

The output has this shape (the current values come from `docs/status.json`):

```text
Title: XGrid <task id> - <task title>
Repository: CSharp_XboxEmulator

Work only on the active task in docs/status.json (<task id>). Read
ImplementationPlan.md -> Execution plan (current) -> <plan section> (<task id>)
and follow its checkboxes in order.
1. Start with `pwsh -File scripts/status.ps1 -Check -Show`; if the check fails,
   stop and report. Do not start any other task or a docs refresh.
2. Price first: the first unchecked box is the price measurement. If the
   ceiling is <10% wall or <20% engine, record a new negative in
   docs/status.json, run `pwsh -File scripts/status.ps1 -Check -Sync`, commit
   only the status change, and stop.
3. If the ceiling clears the bar, follow the remaining checkboxes exactly:
   acceptance test first, smallest implementation, exact 1B anchors, 3
   interleaved pairs, the definition of done.
4. Do not retry a measured negative; cite a new measurement or stop.
5. Close with `pwsh -File scripts/status.ps1 -Check -Sync`. Commit code + tests
   + status + graph in one commit: `<area>: <summary> (<task id>)`. Commit
   yourself so the message follows this protocol; if you leave changes
   uncommitted, the harness commits them as `<title> (RepoFleet task #N)`.
6. Finish with: task id, what changed, evidence (numbers/anchors), and any stop
   condition.

Acceptance: <the task's acceptance test>
Price/ceiling: <the task's price>
```
