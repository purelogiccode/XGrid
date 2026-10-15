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

const SAVE_ATTEMPTS = 8;
const SAVE_RETRY_BASE_MS = 250;

function isTransientSaveError(error: unknown): boolean {
  const code = (error as { code?: unknown }).code;
  // ENOENT on the tmp->state rename is the single-process concurrent-save race:
  // two tasks writing state.json in the same tick share one .tmp path, so the
  // second rename finds the file already moved. Retrying rewrites .tmp and
  // succeeds, so it is transient by the same argument as the lock codes.
  return code === "EPERM" || code === "EBUSY" || code === "EACCES" || code === "ENOENT";
}

export async function saveRunState(statePath: string, state: RunState): Promise<void> {
  state.updatedAt = new Date().toISOString();
  await fs.promises.mkdir(path.dirname(statePath), { recursive: true });
  const tempPath = `${statePath}.tmp`;
  const payload = `${JSON.stringify(state, null, 2)}\n`;
  for (let attempt = 1; ; attempt += 1) {
    try {
      await fs.promises.writeFile(tempPath, payload, "utf8");
      await fs.promises.rename(tempPath, statePath);
      return;
    } catch (error) {
      if (!isTransientSaveError(error) || attempt >= SAVE_ATTEMPTS) {
        throw error;
      }
      await new Promise((resolve) => setTimeout(resolve, SAVE_RETRY_BASE_MS * attempt));
    }
  }
}

export function taskStateById(state: RunState): Map<string, TaskState> {
  return new Map(state.tasks.map((task) => [task.id, task]));
}

export function appendTaskLog(runDir: string, taskId: string, line: string): void {
  const logPath = path.join(runDir, "logs", `${taskId}.log`);
  fs.mkdirSync(path.dirname(logPath), { recursive: true });
  fs.appendFileSync(logPath, `${new Date().toISOString()} ${line}\n`, "utf8");
}
