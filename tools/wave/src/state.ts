import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import type { ResolvedPlan, RunState, TaskState } from "./types.ts";

export function planHash(rawPlanText: string): string {
  return crypto.createHash("sha256").update(rawPlanText).digest("hex").slice(0, 16);
}

export function initialState(plan: ResolvedPlan, planPath: string, hash: string): RunState {
  const now = new Date().toISOString();
  return {
    wave: plan.wave,
    planPath,
    planHash: hash,
    baseBranch: plan.baseBranch,
    worktreeRoot: plan.worktreeRoot,
    startedAt: now,
    updatedAt: now,
    tasks: plan.tasks.map(
      (task): TaskState => ({
        id: task.id,
        status: "pending",
        attempts: 0,
        branch: task.branch,
      }),
    ),
  };
}

export async function loadRunState(statePath: string): Promise<RunState | null> {
  try {
    const text = await fs.promises.readFile(statePath, "utf8");
    return JSON.parse(text) as RunState;
  } catch {
    return null;
  }
}

export async function saveRunState(statePath: string, state: RunState): Promise<void> {
  state.updatedAt = new Date().toISOString();
  await fs.promises.mkdir(path.dirname(statePath), { recursive: true });
  const tempPath = `${statePath}.tmp`;
  await fs.promises.writeFile(tempPath, `${JSON.stringify(state, null, 2)}\n`, "utf8");
  await fs.promises.rename(tempPath, statePath);
}

export function taskStateById(state: RunState): Map<string, TaskState> {
  return new Map(state.tasks.map((task) => [task.id, task]));
}

export function appendTaskLog(runDir: string, taskId: string, line: string): void {
  const logPath = path.join(runDir, "logs", `${taskId}.log`);
  fs.mkdirSync(path.dirname(logPath), { recursive: true });
  fs.appendFileSync(logPath, `${new Date().toISOString()} ${line}\n`, "utf8");
}
