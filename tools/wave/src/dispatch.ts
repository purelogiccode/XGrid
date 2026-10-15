import fs from "node:fs";
import path from "node:path";
import type { OpenCodeClient } from "@opencode/client";
import { clientFor, type BasicAuth } from "./opencode.ts";
import { renderTemplate } from "./prompt.ts";
import { findWorkerResult } from "./result.ts";
import { computeAutoBlocked, selectNext, type RunningTask, type SchedTask } from "./scheduler.ts";
import { runSession, type RunSessionOutcome } from "./session-run.ts";
import { diffNames, gitChecked, logRange, revParse, runCommand } from "./git.ts";
import { ensureWorktree } from "./worktree.ts";
import {
  appendTaskLog,
  initialState,
  loadRunState,
  planHash,
  saveRunState,
  taskStateById,
} from "./state.ts";
import type { GateResult, ResolvedPlan, ResolvedTask, RunState, TaskState, WorkerResult } from "./types.ts";

export type DispatchOptions = {
  repoRoot: string;
  plan: ResolvedPlan;
  planPath: string;
  runDir: string;
  serverUrl: string;
  serverAuth?: BasicAuth | undefined;
  toolDir: string;
  maxParallel?: number;
  resume?: boolean;
  retryFailed?: boolean;
  log: (line: string) => void;
};

type ActiveSession = {
  client: OpenCodeClient;
  sessionId: string;
};

type Harvest =
  | { state: "ok"; value: WorkerResult; raw: string }
  | { state: "no-result" | "invalid"; errors: string[] };

function nowIso(): string {
  return new Date().toISOString();
}

function minutes(ms: number): string {
  return `${(ms / 60000).toFixed(1)}m`;
}

export async function dispatch(options: DispatchOptions): Promise<RunState> {
  const statePath = path.join(options.runDir, "state.json");
  const worktreeRoot = options.plan.worktreeRoot;
  const maxParallel = options.maxParallel ?? options.plan.maxParallel;
  const taskById = new Map(options.plan.tasks.map((task) => [task.id, task]));
  const workerTemplate = await fs.promises.readFile(path.join(options.toolDir, "prompts", "worker.md"), "utf8");
  const planText = await fs.promises.readFile(path.resolve(options.planPath), "utf8");
  const hash = planHash(planText);

  let state = options.resume ? await loadRunState(statePath) : null;
  if (state !== null && state.planHash !== hash) {
    throw new Error(`state at ${statePath} was created from a different plan revision; re-check the plan or start a new wave`);
  }
  if (state === null) {
    state = initialState(options.plan, path.resolve(options.planPath), hash);
    options.log(`[wave ${state.wave}] new run: ${state.tasks.length} tasks, max parallel ${maxParallel}`);
  } else {
    options.log(`[wave ${state.wave}] resuming run from ${statePath}`);
  }
  state.serverUrl = options.serverUrl;
  const locked = state;
  const taskStates = taskStateById(locked);

  for (const taskState of locked.tasks) {
    if (taskState.status === "running" || taskState.status === "verifying") {
      options.log(
        `[task ${taskState.id}] left running by a previous invocation; continuing from session ${taskState.sessionId ?? "<none>"}`,
      );
      taskState.status = "pending";
    }
    if (options.retryFailed && (taskState.status === "failed" || taskState.status === "blocked")) {
      taskState.status = "pending";
      taskState.error = undefined;
      taskState.note = "requeued by --retry-failed";
    }
  }
  await saveRunState(statePath, locked);

  const client = clientFor(options.serverUrl, options.serverAuth);
  const activeSessions = new Map<string, ActiveSession>();
  let interrupted = false;
  const onSigint = (): void => {
    if (interrupted) {
      process.exit(130);
    }
    interrupted = true;
    options.log("[wave] interrupt received; aborting active sessions and saving state");
    for (const active of activeSessions.values()) {
      void active.client.session.interrupt({ sessionID: active.sessionId }).catch(() => undefined);
    }
    void saveRunState(statePath, locked).finally(() => process.exit(130));
  };
  process.once("SIGINT", onSigint);

  const toSched = (): SchedTask[] =>
    locked.tasks.map((taskState) => {
      const task = taskById.get(taskState.id);
      return {
        id: taskState.id,
        status: taskState.status,
        dependsOn: task?.dependsOn ?? [],
        serial: task?.serial ?? false,
      };
    });

  const runTask = async (task: ResolvedTask, taskState: TaskState): Promise<void> => {
    const logLine = (line: string): void => {
      appendTaskLog(options.runDir, task.id, line);
      options.log(`[task ${task.id}] ${line}`);
    };
    const started = Date.now();
    taskState.status = "running";
    taskState.startedAt ??= nowIso();
    taskState.endedAt = undefined;
    await saveRunState(statePath, locked);

    const info = await ensureWorktree({
      repoRoot: options.repoRoot,
      worktreeRoot,
      taskId: task.id,
      branch: task.branch,
      baseBranch: options.plan.baseBranch,
      dataLink: options.plan.dataLink,
    });
    taskState.worktree = info.worktree;
    await saveRunState(statePath, locked);
    logLine(`worktree ${info.worktree}${info.reused ? " (reused)" : ""} branch ${task.branch}`);

    let lastReason = "unknown";
    let sessionId = taskState.sessionId;
    const basePrompt = renderTemplate(workerTemplate, {
      taskId: task.id,
      title: task.title,
      branch: task.branch,
      baseBranch: options.plan.baseBranch,
      acceptance: task.acceptance,
      prompt: task.prompt,
      budgetMinutes: String(Math.round(task.budgetMs / 60000)),
      wave: options.plan.wave,
    });

    for (let attempt = taskState.attempts + 1; attempt <= task.retries + 1; attempt += 1) {
      taskState.attempts = attempt;
      await saveRunState(statePath, locked);
      const continuation = await renderInventory(task, info.worktree, lastReason);
      const prompt = sessionId === undefined ? basePrompt : `${basePrompt}\n\n${continuation}`;
      const outcome: RunSessionOutcome = await runSession({
        client,
        directory: info.worktree,
        title: `wave ${locked.wave} ${task.id} (attempt ${attempt})`,
        prompt,
        agent: task.agent,
        model: task.model,
        budgetMs: task.budgetMs,
        autoApprove: task.autoApprove,
        sessionId,
        log: logLine,
      }).catch(
        (error: unknown): RunSessionOutcome => ({
          sessionId: sessionId ?? "",
          completed: false,
          aborted: false,
          error: String(error),
          assistantText: "",
          elapsedMs: Date.now() - started,
        }),
      );
      sessionId = outcome.sessionId.length > 0 ? outcome.sessionId : sessionId;
      taskState.sessionId = sessionId;
      await saveRunState(statePath, locked);
      if (sessionId !== undefined) {
        activeSessions.set(task.id, { client, sessionId });
      }

      if (!outcome.completed) {
        lastReason = outcome.aborted
          ? `budget of ${minutes(task.budgetMs)} exceeded`
          : `session error: ${outcome.error ?? "unknown"}`;
        logLine(`attempt ${attempt} ended without completion (${lastReason})`);
        continue;
      }

      let harvest = await harvestResult(task, info.worktree, outcome.assistantText, logLine);
      if (harvest.state === "no-result") {
        logLine("worker completed without a result file; sending one harvest nudge");
        const nudge = await runSession({
          client,
          directory: info.worktree,
          title: `wave ${locked.wave} ${task.id} (harvest)`,
          prompt:
            `You completed the task but did not write wt-data/wave/${task.id}.result.json.\n` +
            `Write that file NOW with the exact JSON contract from your instructions (taskId, status, branch, headCommit, filesChanged, testsRun, acceptance, notes, blockers). Do not do any other work.`,
          agent: task.agent,
          model: task.model,
          budgetMs: Math.min(10 * 60 * 1000, task.budgetMs),
          autoApprove: task.autoApprove,
          sessionId,
          log: logLine,
        }).catch(() => null);
        harvest = await harvestResult(task, info.worktree, nudge?.assistantText ?? "", logLine);
      }

      if (harvest.state !== "ok") {
        lastReason = `no valid result: ${harvest.errors.join("; ")}`;
        logLine(lastReason);
        continue;
      }

      const result = harvest.value;
      if (result.status !== "done") {
        taskState.status = result.status === "blocked" ? "blocked" : "failed";
        taskState.error = result.blockers !== undefined && result.blockers.length > 0 ? result.blockers : result.status;
        taskState.note = result.notes;
        taskState.endedAt = nowIso();
        await writeResultFile(options.runDir, task.id, harvest.raw);
        logLine(`${taskState.status.toUpperCase()}: ${result.blockers || result.status}`);
        await saveRunState(statePath, locked);
        activeSessions.delete(task.id);
        return;
      }

      const branchHead = await revParse(options.repoRoot, task.branch);
      if (branchHead === null || !branchHead.startsWith(result.headCommit.toLowerCase())) {
        lastReason = `headCommit mismatch: worker reported ${result.headCommit}, branch ${task.branch} is at ${branchHead ?? "<missing>"}`;
        logLine(lastReason);
        continue;
      }

      let gate: GateResult | undefined;
      if (task.gate !== undefined) {
        logLine(`running gate: ${task.gate}`);
        const gateResult = await runGate(task.gate, info.worktree);
        if (!gateResult.ok) {
          gate = gateResult;
          taskState.gate = gate;
          lastReason = `gate failed (exit ${gateResult.code}): ${gateResult.outputTail.slice(-400)}`;
          logLine(`gate FAILED (exit ${gateResult.code}); sending the failure back to the worker`);
          continue;
        }
        gate = gateResult;
        logLine("gate passed");
      }

      taskState.status = "done";
      taskState.headCommit = branchHead;
      taskState.note = result.notes;
      taskState.gate = gate;
      taskState.error = undefined;
      taskState.endedAt = nowIso();
      await writeResultFile(options.runDir, task.id, harvest.raw);
      const commits = await logRange(options.repoRoot, `${options.plan.baseBranch}..${task.branch}`);
      const commitCount = commits.length === 0 ? 0 : commits.split(/\r?\n/).length;
      logLine(`DONE in ${minutes(Date.now() - started)}; ${commitCount} commit(s) on ${task.branch} @ ${branchHead.slice(0, 8)}`);
      await saveRunState(statePath, locked);
      activeSessions.delete(task.id);
      return;
    }

    taskState.status = "failed";
    taskState.error = `attempts exhausted: ${lastReason}`;
    taskState.endedAt = nowIso();
    logLine(`FAILED: ${taskState.error}`);
    await saveRunState(statePath, locked);
    activeSessions.delete(task.id);
  };

  async function renderInventory(task: ResolvedTask, worktree: string, reason: string): Promise<string> {
    const commits = await logRange(worktree, `${options.plan.baseBranch}..HEAD`);
    const status = await gitChecked(worktree, ["status", "--short"]);
    const resultPath = path.join(worktree, "wt-data", "wave", `${task.id}.result.json`);
    const hasResult = fs.existsSync(resultPath);
    const changed = await diffNames(worktree, `${options.plan.baseBranch}...HEAD`);
    return [
      `## Continuation inventory (previous attempt: ${reason})`,
      ``,
      `Do not restart from scratch. Continue from the current state of this worktree:`,
      ``,
      `- Branch ${task.branch} commits since ${options.plan.baseBranch}:`,
      commits.length > 0 ? commits : "  (none)",
      `- Working tree status:`,
      status.length > 0 ? status : "  (clean)",
      `- Files changed vs ${options.plan.baseBranch}: ${changed.length > 0 ? changed.join(", ") : "none"}`,
      `- Result file present: ${hasResult ? "yes" : "no"}`,
      ``,
      `Finish the acceptance test and write wt-data/wave/${task.id}.result.json when done.`,
    ].join("\n");
  }

  async function harvestResult(
    task: ResolvedTask,
    worktree: string,
    assistantText: string,
    logLine: (line: string) => void,
  ): Promise<Harvest> {
    const resultPath = path.join(worktree, "wt-data", "wave", `${task.id}.result.json`);
    let rawFile: string | undefined;
    try {
      rawFile = await fs.promises.readFile(resultPath, "utf8");
    } catch {
      rawFile = undefined;
    }
    const parsed = findWorkerResult(rawFile, assistantText);
    if (!parsed.ok) {
      logLine(`result harvest failed: ${parsed.errors.join("; ")}`);
      const looksEmpty = rawFile === undefined && !assistantText.includes("taskId");
      return { state: looksEmpty ? "no-result" : "invalid", errors: parsed.errors };
    }
    if (parsed.value.taskId !== task.id) {
      const errors = [`result taskId "${parsed.value.taskId}" does not match "${task.id}"`];
      logLine(`result harvest failed: ${errors.join("; ")}`);
      return { state: "invalid", errors };
    }
    return { state: "ok", value: parsed.value, raw: rawFile ?? JSON.stringify(parsed.value, null, 2) };
  }

  try {
    const running = new Map<string, Promise<void>>();
    for (;;) {
      for (const id of computeAutoBlocked(toSched())) {
        const taskState = taskStates.get(id);
        if (taskState !== undefined && taskState.status === "pending") {
          taskState.status = "blocked";
          taskState.error = "a dependency failed";
          taskState.endedAt = nowIso();
          options.log(`[task ${id}] BLOCKED: a dependency failed`);
          await saveRunState(statePath, locked);
        }
      }

      while (running.size < maxParallel) {
        const runningList: RunningTask[] = [...running.keys()].map((id) => ({
          id,
          serial: taskById.get(id)?.serial ?? false,
        }));
        const nextId = selectNext(toSched(), runningList);
        if (nextId === null) {
          break;
        }
        const task = taskById.get(nextId);
        const taskState = taskStates.get(nextId);
        if (task === undefined || taskState === undefined) {
          break;
        }
        const promise = runTask(task, taskState)
          .catch(async (error: unknown) => {
            taskState.status = "failed";
            taskState.error = String(error);
            taskState.endedAt = nowIso();
            options.log(`[task ${task.id}] FAILED: ${String(error)}`);
            await saveRunState(statePath, locked);
          })
          .finally(() => {
            running.delete(nextId);
          });
        running.set(nextId, promise);
      }

      if (running.size === 0) {
        break;
      }
      await Promise.race([...running.values()]);
    }
  } finally {
    process.removeListener("SIGINT", onSigint);
  }

  const summary = locked.tasks.map((taskState) => `${taskState.id}=${taskState.status}`).join(" ");
  options.log(`[wave ${locked.wave}] all tasks settled: ${summary}`);
  await saveRunState(statePath, locked);
  return locked;
}

async function writeResultFile(runDir: string, taskId: string, raw: string): Promise<void> {
  const dir = path.join(runDir, "results");
  await fs.promises.mkdir(dir, { recursive: true });
  await fs.promises.writeFile(path.join(dir, `${taskId}.result.json`), `${raw.trimEnd()}\n`, "utf8");
}

export async function runGate(command: string, cwd: string, timeoutMs = 45 * 60 * 1000): Promise<GateResult> {
  const result = await runCommand(command, [], { cwd, shell: true, timeoutMs });
  const combined = `${result.stdout}${result.stderr}`;
  return {
    command,
    code: result.timedOut ? -999 : result.code,
    ok: !result.timedOut && result.code === 0,
    outputTail: combined.slice(-4000),
  };
}

export function abandonedWorktrees(state: RunState): string[] {
  const terminal = new Set(["done", "blocked", "failed", "conflict", "merged"]);
  return state.tasks
    .filter((task) => task.worktree !== undefined && terminal.has(task.status))
    .map((task) => task.worktree as string);
}
