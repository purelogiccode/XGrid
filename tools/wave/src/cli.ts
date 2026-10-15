#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { parseArgs } from "node:util";
import { repoRoot } from "./git.ts";
import { loadPlan } from "./plan.ts";
import { ensureServer } from "./opencode.ts";
import { abandonedWorktrees, dispatch } from "./dispatch.ts";
import { aggregate } from "./aggregate.ts";
import { serveDashboard } from "./dashboard.ts";
import { loadRunState } from "./state.ts";
import { removeWorktree } from "./worktree.ts";
import { defaultDurations, simulate } from "./simulate.ts";
import type { ResolvedPlan, RunState } from "./types.ts";

const TOOL_DIR = path.resolve(import.meta.dirname, "..");

const USAGE = `xgrid wave - pull-scheduled agent waves on the opencode v2 client

usage:
  wave run <plan.json> [options]     dispatch a wave (workers take the next task as soon as one finishes)
  wave status <plan.json>            print run state from runs/<wave>/state.json
  wave dashboard <plan.json>         serve a live dashboard (task states + log tails, auto-refresh)
  wave aggregate <plan.json>         merge finished branches sequentially (--no-ff) and run the final gate
  wave clean <plan.json>             remove worktrees of settled tasks

options:
  --plan <file>        plan path (default: wave.plan.json in cwd)
  --resume             continue an existing run (reuses worktrees, sessions, state)
  --retry-failed       requeue failed/blocked tasks
  --max-parallel <n>   override plan.maxParallel
  --port <n>           opencode server port for run/aggregate, dashboard port for dashboard (default 4181)
  --server <url>       attach to an existing opencode server instead of spawning one
  --no-aggregate       skip the automatic merge leg after dispatch
  --no-resolver        do not spawn a resolver session on merge conflicts
  --dry-run            validate the plan and print a pull-schedule simulation
  --json               machine-readable output for status
  -h, --help           show this help
`;

async function main(): Promise<number> {
  const { values, positionals } = parseArgs({
    options: {
      plan: { type: "string" },
      resume: { type: "boolean", default: false },
      "retry-failed": { type: "boolean", default: false },
      "max-parallel": { type: "string" },
      port: { type: "string" },
      server: { type: "string" },
      "no-aggregate": { type: "boolean", default: false },
      "no-resolver": { type: "boolean", default: false },
      "dry-run": { type: "boolean", default: false },
      json: { type: "boolean", default: false },
      help: { type: "boolean", short: "h", default: false },
    },
    allowPositionals: true,
  });

  const command = positionals[0] ?? "run";
  if (values.help) {
    console.log(USAGE);
    return 0;
  }
  const root = await repoRoot(process.cwd());
  const planPath = path.resolve(values.plan ?? positionals[1] ?? "wave.plan.json");
  const plan = await loadPlan(planPath, root);
  const runDir = path.join(root, "runs", plan.wave);
  const statePath = path.join(runDir, "state.json");

  switch (command) {
    case "run":
      return await runCommand(plan, planPath, root, runDir, statePath);
    case "status":
      return await statusCommand(plan, statePath);
    case "dashboard":
      return await dashboardCommand(plan, runDir, statePath);
    case "aggregate":
      return await aggregateCommand(plan, root, runDir, statePath);
    case "clean":
      return await cleanCommand(root, statePath);
    default:
      console.error(`unknown command "${command}"\n\n${USAGE}`);
      return 2;
  }

  async function runCommand(
    resolved: ResolvedPlan,
    resolvedPlanPath: string,
    repo: string,
    runDirectory: string,
    stateFile: string,
  ): Promise<number> {
    if (values["dry-run"]) {
      console.log(`plan ${resolved.wave}: ${resolved.tasks.length} tasks, max parallel ${resolved.maxParallel}`);
      const events = simulate(resolved, defaultDurations(resolved));
      for (const event of events) {
        console.log(`  t+${(event.timeMs / 60000).toFixed(1)}m ${event.kind.padEnd(7)} ${event.taskId}`);
      }
      return 0;
    }
    if (!values.resume && fs.existsSync(stateFile)) {
      console.error(`run state already exists at ${stateFile}; use --resume to continue it`);
      return 2;
    }
    process.chdir(repo);
    const server = await ensureServer({
      port: values.port !== undefined ? Number(values.port) : resolved.serverPort,
      stateDir: path.join(runDirectory, "opencode-service"),
      ...(values.server !== undefined ? { attachUrl: values.server } : {}),
      log: (line) => console.log(line),
    });
    console.log(`[wave] opencode server at ${server.url}`);
    try {
      const state = await dispatch({
        repoRoot: repo,
        plan: resolved,
        planPath: resolvedPlanPath,
        runDir: runDirectory,
        serverUrl: server.url,
        serverAuth: server.auth,
        toolDir: TOOL_DIR,
        ...(values["max-parallel"] !== undefined ? { maxParallel: Number(values["max-parallel"]) } : {}),
        resume: values.resume,
        retryFailed: values["retry-failed"],
        log: (line) => console.log(line),
      });
      let exitCode = exitCodeFor(state);
      if (resolved.autoAggregate && !values["no-aggregate"]) {
        await aggregate({
          repoRoot: repo,
          plan: resolved,
          state,
          statePath: stateFile,
          runDir: runDirectory,
          serverUrl: server.url,
          serverAuth: server.auth,
          resolver: !values["no-resolver"],
          log: (line) => console.log(line),
        });
        exitCode = exitCodeFor(state);
      }
      printSummary(state);
      console.log(`run state: ${stateFile}`);
      return exitCode;
    } finally {
      server.close();
    }
  }

  async function statusCommand(resolved: ResolvedPlan, stateFile: string): Promise<number> {
    const state = await loadRunState(stateFile);
    if (state === null) {
      console.error(`no run state at ${stateFile}`);
      return 2;
    }
    if (values.json) {
      console.log(JSON.stringify(state, null, 2));
      return 0;
    }
    console.log(`wave ${state.wave} (updated ${state.updatedAt})`);
    const width = Math.max(...state.tasks.map((task) => task.id.length), 4);
    for (const task of state.tasks) {
      const gate = task.gate !== undefined && !task.gate.ok ? " gate:FAILED" : "";
      console.log(
        `  ${task.id.padEnd(width)}  ${task.status.padEnd(9)} attempts=${task.attempts} head=${task.headCommit?.slice(0, 8) ?? "-"}${gate} ${task.error ?? ""}`,
      );
    }
    if (state.aggregate !== undefined) {
      console.log(
        `  aggregate: merged=[${state.aggregate.merged.join(",")}] conflicts=[${state.aggregate.conflicts.join(",")}]`,
      );
    }
    return 0;
  }

  async function dashboardCommand(
    resolved: ResolvedPlan,
    runDirectory: string,
    stateFile: string,
  ): Promise<number> {
    const port = values.port !== undefined ? Number(values.port) : 4181;
    const handle = serveDashboard({
      runDir: runDirectory,
      plan: resolved,
      statePath: stateFile,
      port,
      log: (line) => console.log(line),
    });
    console.log(`[wave] dashboard at ${handle.url} (Ctrl-C to stop; bound to 127.0.0.1 only)`);
    await new Promise<void>((resolve) => {
      const stop = (): void => {
        handle.close();
        resolve();
      };
      process.once("SIGINT", stop);
    });
    return 0;
  }

  async function aggregateCommand(    resolved: ResolvedPlan,
    repo: string,
    runDirectory: string,
    stateFile: string,
  ): Promise<number> {
    const state = await loadRunState(stateFile);
    if (state === null) {
      console.error(`no run state at ${stateFile}`);
      return 2;
    }
    process.chdir(repo);
    const server = await ensureServer({
      port: values.port !== undefined ? Number(values.port) : resolved.serverPort,
      stateDir: path.join(runDirectory, "opencode-service"),
      ...(values.server !== undefined ? { attachUrl: values.server } : {}),
      log: (line) => console.log(line),
    });
    try {
      await aggregate({
        repoRoot: repo,
        plan: resolved,
        state,
        statePath: stateFile,
        runDir: runDirectory,
        serverUrl: server.url,
        serverAuth: server.auth,
        resolver: !values["no-resolver"],
        log: (line) => console.log(line),
      });
      const conflicts = state.aggregate?.conflicts.length ?? 0;
      return conflicts > 0 ? 1 : 0;
    } finally {
      server.close();
    }
  }

  async function cleanCommand(repo: string, stateFile: string): Promise<number> {
    const state = await loadRunState(stateFile);
    if (state === null) {
      console.error(`no run state at ${stateFile}`);
      return 2;
    }
    for (const worktree of abandonedWorktrees(state)) {
      await removeWorktree(repo, worktree);
      console.log(`[clean] removed ${worktree}`);
    }
    return 0;
  }
}

function exitCodeFor(state: RunState): number {
  const ok = state.tasks.every((task) => task.status === "done" || task.status === "merged");
  return ok ? 0 : 1;
}

function printSummary(state: RunState): void {
  const counts = new Map<string, number>();
  for (const task of state.tasks) {
    counts.set(task.status, (counts.get(task.status) ?? 0) + 1);
  }
  const summary = [...counts.entries()].map(([status, count]) => `${status}=${count}`).join(" ");
  console.log(`[wave ${state.wave}] summary: ${summary}`);
}

main()
  .then((code) => process.exit(code))
  .catch((error: unknown) => {
    console.error(`wave: ${error instanceof Error ? error.message : String(error)}`);
    process.exit(2);
  });
