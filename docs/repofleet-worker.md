# RepoFleet worker contract — XGrid

This repository is developed by RepoFleet workers. A worker runs `opencode`
inside this checkout with a task command written by the admin. The repository,
not the task command, owns the rules and the task sequence.

## The static command (paste this for every sequence slot)

```text
Run `pwsh -File scripts/status.ps1 -Next` and follow the printed task exactly.
Do not choose a task yourself; do not do more than one task.
When the task is done, run the close command printed by `-Next` (`-Complete` or
`-Negative`), then commit code + tests + status + graph in one commit.
If `-Next` prints STOP or SEQUENCE COMPLETE, stop and report; do not start
anything else.
```

Queue this same command once per task in the sequence. RepoFleet already allows
one active task per repository, so the slots run one at a time. `-Next` reads
`docs/status.json`, claims the active task and prints the full task (id, plan
section, price, acceptance and the exact close commands). `-Complete` and
`-Negative` remove the task and promote the next eligible one.

The admin does not need to know the task ids and does not need to run anything
locally. The sequence lives in `docs/status.json`.

## The sequence

- The queue in `docs/status.json` is the sequence, in order, with dependencies.
- `-Next` increments the attempt counter and prints `Attempt N of 2`. A task
  that is still active after a failed slot is retried by the next slot; after
  two attempts `-Next` prints STOP, and the worker records the negative.
- `-Complete -Evidence "<numbers/anchors>" -CommitMessage "<area>: <summary> (<id>)"`
  closes the task as done.
- `-Negative -Evidence "<measurement>" -Reopen "<condition>"` records a
  measured negative.
- `-Negative -Task <id> -Evidence "<why>" -Reopen "<condition>"` closes a
  parked/queued conditional task (e.g. D4 after D2 chose the device path)
  without changing the active task.
- A completed or negative task is removed from the queue, so no later worker
  can pick it again. That is the duplicate-work guard.
- If no eligible task remains, `-Next` prints SEQUENCE COMPLETE; the remaining
  RepoFleet slots are no-ops.

## Closing a parked conditional task

A conditional task that never opened (its trigger did not happen) is not a
failed attempt. Close it without making it active:

```text
pwsh -File scripts/status.ps1 -Negative -Task D4 -Evidence "<why it did not open>" -Reopen "<what would open it>"
```

It leaves the queue, lands in the `negatives` table, and the active task is
unchanged. Do not leave it `blocked` forever; the queue must be able to empty.

## What the worker must do

1. Run `pwsh -File scripts/status.ps1 -Next`; it prints the task. Do not choose
   a task yourself.
2. Follow the printed steps. Start with
   `pwsh -File scripts/status.ps1 -Check -Show`; if the check fails, stop and
   report.
3. The first unchecked box is the price measurement. If the ceiling is below
   10% wall / 20% engine, run the printed negative close and stop.
4. If the ceiling clears the bar, follow the checkboxes in order: acceptance
   test first, smallest implementation, exact 1B anchors, 3 interleaved pairs,
   the definition of done.
5. Close with the matching command printed by `-Next` (`-Complete` or
   `-Negative`). That advances the queue.
6. Commit code + tests + status + graph in one commit:
   `area: imperative summary (task id)`.
7. Finish with: task id, changed files, evidence (numbers/anchors), stop
   condition.

## What the worker must not do

- Do not choose a task or skip the queue; `-Next` is the only selector.
- Do not edit `tools/completion-graph/completion-data.json` or the generated
  status blocks by hand; `-Complete`/`-Negative`/`-Sync` regenerate them.
- Do not retry a measured negative; the `negatives` table in `docs/status.json`
  is binding.
- Do not run the full 22-row corpus after a slice; re-verify only the affected
  rows.
- Do not create or switch branches; RepoFleet stays on main/master.
- Do not commit `.repofleet-task.md`.
- Do not leave a dirty tree; `-Next` refuses one (any tracked or untracked
  change except `.repofleet-task.md`).
- Do not leave uncommitted work; the harness's fallback message is generic
  (`<title> (RepoFleet task #N)`), so the task-id protocol is lost.
- Do not rewrite the status narrative in `next-session.md`, `AGENTS.md`, the
  decisions file or the plan; the generated block is the status.
- If you cannot complete the task: revert uncommitted code changes
  (`git checkout -- .` and `git clean -fd` for untracked files), run the
  negative close, commit the status change, and stop. The next slot must start
  from a clean tree.

## RepoFleet mechanics

- One active task per repository is enforced by the server
  (`IX_AgentTasks_ActiveRepo`). That is the WIP limit; keep it.
- The worker writes the command to `.repofleet-task.md` and deletes it after the
  run.
- The harness commits any remaining changes as `<title> (RepoFleet task #N)` and
  pushes. If the agent commits itself, the harness pushes that commit. The task
  command asks the agent to commit so the message follows the repo protocol.
- The harness refuses to start on a dirty or unpushed checkout. On a failed
  (non-zero) opencode run it resets the workspace to the start commit and
  preserves any agent commit on `repofleet/failed-<taskId>`, so the next slot
  starts clean.
- `ExtraInstructions` is machine-wide; do not put XGrid rules there.

## Why this shape

The admin starts workers from a remote computer and cannot run repo scripts to
generate a prompt. The static `-Next` command moves the sequence into the repo:
the worker asks the repo what to do, and `-Complete`/`-Negative` advance it. A
completed task is removed from the queue, so the next worker cannot repeat it.
The rules live in `AGENTS.md` and `ImplementationPlan.md` (E0-E11), so the task
command stays short and cannot drift. The `status.ps1 -Check` gate makes
status/plan/graph drift a hard failure, not a review comment.

## The `-Next` output

```text
Title: XGrid <task id> - <task title>
Attempt: 1 of 2
Repository: CSharp_XboxEmulator

Work only on the active task in docs/status.json (<task id>). Read
ImplementationPlan.md -> Execution plan (current) -> <plan section> (<task id>)
and follow its checkboxes in order.
1. Start with `pwsh -File scripts/status.ps1 -Check -Show`; if the check fails,
   stop and report. Do not start any other task or a docs refresh.
2. Price first: the first unchecked box is the price measurement. If the
   ceiling is <10% wall or <20% engine, run the negative close below and stop.
3. If the ceiling clears the bar, follow the remaining checkboxes exactly:
   acceptance test first, smallest implementation, exact 1B anchors, 3
   interleaved pairs, the definition of done.
4. Do not retry a measured negative (docs/status.json negatives); cite a new
   measurement or stop.
5. Close with the matching command below. Commit code + tests + status + graph
   in one commit. Commit yourself so the message follows this protocol; if you
   leave changes uncommitted, the harness commits them as
   `<title> (RepoFleet task #N)`.
6. Finish with: task id, what changed, evidence (numbers/anchors), and any stop
   condition.

Close (done): pwsh -File scripts/status.ps1 -Complete -Evidence "<numbers/anchors>" -CommitMessage "<area>: <summary> (<task id>)"
Close (negative): pwsh -File scripts/status.ps1 -Negative -Evidence "<measurement>" -Reopen "<condition>"

Acceptance: <the task's acceptance test>
Price/ceiling: <the task's price>
```

`-Next` first refuses a dirty tree (any tracked or untracked change except
`.repofleet-task.md`), so a failed or crashed slot cannot leak files into the
next task.
