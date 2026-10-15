# tools/wave — pull-scheduled agent waves

Turns the manual [BOSS protocol](../../AGENTS.md#wave-operations--boss-protocol) into a program:
a dispatcher keeps N worker agents busy, hands the next queued task to whichever worker
finishes first, verifies each result, and then merges finished branches sequentially.

```
wave.plan.json ──> dispatcher (N slots, pull scheduling)
                      │  idle slot => next ready task (deps satisfied, serial excluded)
                      │  git worktree + opencode v2 session (location.directory = worktree)
                      v
                   worker sessions ──> wt-data/wave/<id>.result.json (validated) + branch
                      │
                      v
                   aggregator: topo-order `git merge --no-ff`, optional resolver session, final gate
```

## Quickstart

```powershell
cd tools/wave
npm install
npm run wave -- run examples/wave.plan.json --dry-run    # validate + show the pull schedule
npm run wave -- run my-wave.plan.json                    # dispatch (spawns `opencode serve`)
npm run wave -- status my-wave.plan.json                 # task table (--json for machines)
npm run wave -- aggregate my-wave.plan.json              # merge leg only
npm run wave -- clean my-wave.plan.json                  # remove worktrees of settled tasks
```

Requires Node 24+ (native TypeScript execution) and `opencode` v2 on PATH. The run directory is
`runs/<wave>/` (gitignored): `state.json` (crash-safe, `--resume` continues it), `logs/<task>.log`,
`results/<task>.result.json`, `report.md`. Spawned servers keep their state registration under
`runs/<wave>/opencode-service/` (via `XDG_STATE_HOME`), so concurrent waves never share one server.

## How workers are driven

- One `opencode serve` process is spawned on the plan's `serverPort` (or `--server <url>` attaches to
  yours; a healthy server already on that port is reused). The v2 server requires basic auth, which the
  tool reads from the server's own service registration file (`service.json`).
- Each worker session is created with `location.directory` pointing at its worktree, so sessions edit
  files in that worktree only. `agent` and `model` (`{providerID, modelID}` in the plan) are bound at
  session creation.
- The worker prompt is `prompts/worker.md` rendered per task: `{{prompt}}`, `{{acceptance}}`,
  branch, budget. Workers are told to commit, then write `wt-data/wave/<taskId>.result.json`.
- Completion is detected by polling the session (completed assistant message), never a
  blocked HTTP request; status/retry progress comes from the event stream, and permissions are
  auto-answered there (`autoApprove`).
- Verification is deterministic: result JSON validated, reported `headCommit` must match the real
  branch head, optional per-task `gate` command must pass. Failures are recorded as diagnoses,
  never silently retried into a fake fix.
- Budget exceeded or session error → the task is continued in the same session with a *continuation
  inventory* (git log/status/diff + result-file presence), up to `retries`.

## Plan schema (`wave.plan.json`)

| field | meaning |
| --- | --- |
| `wave` | run id, names `runs/<wave>/` and session titles |
| `baseBranch` | branch worktrees fork from and the aggregator merges into (default `master`) |
| `maxParallel` | slot count (default 4) |
| `worktreeRoot` | where worktrees live (default `<tmp>/xgrid-wave/worktrees`, deliberately outside the repo/Dropbox) |
| `dataLink` | `copy` (default), `junction`, or `none` for `data/` in each worktree; `References/` is always junctioned |
| `autoAggregate` | merge when the wave settles (default true; `--no-aggregate` overrides) |
| `finalGate` | commands run in the repo root after all merges (format/build/test scripts) |
| `defaults` | per-task defaults: `agent`, `model` (`{providerID, modelID}`), `budgetMs`, `retries`, `gate`, `autoApprove` |
| `tasks[]` | `id`, `title`, `prompt` or `promptFile`, `acceptance`, `dependsOn[]`, `serial`, `branch`, `merge`, per-task overrides |

Scheduler rules: a task starts only when every dependency is `done`/`merged`; `serial: true` tasks
run alone (never parallel with anything); failed dependencies mark dependents `blocked`; branches
merge in dependency order with `--no-ff`.

## Merge conflicts

Conflicts are detected on a clean tree (`git merge --abort` first), then — unless `--no-resolver`
— a resolver session is spawned in the repo root with the conflict output and repo conventions
(§-union renumbering). Unresolved conflicts are recorded in `state.json` and `report.md` for a
human/next wave. Nothing is force-pushed, and a tree with uncommitted *tracked* changes is refused
(untracked files are fine; git still refuses any merge that would overwrite one).

## Tests

```powershell
npm test        # node --test on the pure core (scheduler, plan, result, simulate, state)
npm run typecheck
```
