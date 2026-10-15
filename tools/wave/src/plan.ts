import { readFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import type {
  ModelRef,
  ResolvedPlan,
  ResolvedTask,
  WavePlan,
  WaveTask,
} from "./types.ts";

const DEFAULT_BUDGET_MS = 90 * 60 * 1000;
const DEFAULT_RETRIES = 1;
const DEFAULT_PARALLEL = 4;
const DEFAULT_SERVER_PORT = 4123;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isModelRef(value: unknown): value is ModelRef {
  return (
    isRecord(value) &&
    typeof value["providerID"] === "string" &&
    typeof value["modelID"] === "string"
  );
}

export function validatePlan(raw: unknown): string[] {
  const errors: string[] = [];
  if (!isRecord(raw)) {
    return ["plan must be a JSON object"];
  }
  if (typeof raw["wave"] !== "string" || raw["wave"].length === 0) {
    errors.push("plan.wave must be a non-empty string");
  }
  if (raw["dataLink"] !== undefined && !["copy", "junction", "none"].includes(raw["dataLink"] as string)) {
    errors.push('plan.dataLink must be "copy", "junction" or "none"');
  }
  if (!Array.isArray(raw["tasks"]) || raw["tasks"].length === 0) {
    errors.push("plan.tasks must be a non-empty array");
    return errors;
  }
  const seen = new Set<string>();
  const tasks = raw["tasks"] as unknown[];
  tasks.forEach((task, index) => {
    const at = `tasks[${index}]`;
    if (!isRecord(task)) {
      errors.push(`${at} must be an object`);
      return;
    }
    const id = task["id"];
    if (typeof id !== "string" || id.length === 0) {
      errors.push(`${at}.id must be a non-empty string`);
    } else if (seen.has(id)) {
      errors.push(`${at}.id "${id}" is duplicated`);
    } else {
      seen.add(id);
    }
    if (typeof task["title"] !== "string" || task["title"].length === 0) {
      errors.push(`${at}.title must be a non-empty string`);
    }
    const hasPrompt = typeof task["prompt"] === "string" && task["prompt"].length > 0;
    const hasPromptFile = typeof task["promptFile"] === "string" && task["promptFile"].length > 0;
    if (!hasPrompt && !hasPromptFile) {
      errors.push(`${at} must have either prompt or promptFile`);
    }
    if (typeof task["acceptance"] !== "string" || task["acceptance"].length === 0) {
      errors.push(`${at}.acceptance must be a non-empty string`);
    }
    if (task["dependsOn"] !== undefined) {
      if (!Array.isArray(task["dependsOn"]) || task["dependsOn"].some((d) => typeof d !== "string")) {
        errors.push(`${at}.dependsOn must be an array of task ids`);
      }
    }
    if (task["model"] !== undefined && !isModelRef(task["model"])) {
      errors.push(`${at}.model must be { providerID, modelID }`);
    }
  });
  for (const task of tasks) {
    if (!isRecord(task) || !Array.isArray(task["dependsOn"])) {
      continue;
    }
    for (const dep of task["dependsOn"]) {
      if (typeof dep === "string" && !seen.has(dep)) {
        errors.push(`tasks[id=${String(task["id"])}].dependsOn references unknown task "${dep}"`);
      }
      if (typeof dep === "string" && dep === task["id"]) {
        errors.push(`tasks[id=${String(task["id"])}] depends on itself`);
      }
    }
  }
  if (errors.length === 0) {
    const ordered = topoSortTasks(tasks as WaveTask[]);
    if (!ordered) {
      errors.push("tasks contain a dependency cycle");
    }
  }
  return errors;
}

function topoSortTasks(tasks: WaveTask[]): WaveTask[] | null {
  const byId = new Map(tasks.map((t) => [t.id, t]));
  const done = new Set<string>();
  const ordered: WaveTask[] = [];
  let progress = true;
  while (progress && ordered.length < tasks.length) {
    progress = false;
    for (const task of tasks) {
      if (done.has(task.id)) {
        continue;
      }
      const deps = task.dependsOn ?? [];
      if (deps.every((d) => done.has(d) || !byId.has(d))) {
        done.add(task.id);
        ordered.push(task);
        progress = true;
      }
    }
  }
  return ordered.length === tasks.length ? ordered : null;
}

export async function loadPlan(planPath: string, repoRoot: string): Promise<ResolvedPlan> {
  const absolute = path.resolve(planPath);
  const raw = JSON.parse(await readFile(absolute, "utf8")) as unknown;
  const errors = validatePlan(raw);
  if (errors.length > 0) {
    throw new Error(`invalid wave plan:\n  - ${errors.join("\n  - ")}`);
  }
  const plan = raw as WavePlan;
  const planDir = path.dirname(absolute);
  const defaults = plan.defaults ?? {};
  const tasks: ResolvedTask[] = [];
  for (const task of plan.tasks) {
    let prompt = task.prompt ?? "";
    if (!prompt && task.promptFile) {
      prompt = (await readFile(path.resolve(planDir, task.promptFile), "utf8")).trimEnd();
    }
    tasks.push({
      id: task.id,
      title: task.title,
      prompt,
      acceptance: task.acceptance,
      dependsOn: task.dependsOn ?? [],
      serial: task.serial ?? false,
      branch: task.branch ?? `wave/${task.id}`,
      merge: task.merge ?? true,
      agent: task.agent ?? defaults.agent,
      model: task.model ?? defaults.model,
      budgetMs: task.budgetMs ?? defaults.budgetMs ?? DEFAULT_BUDGET_MS,
      retries: task.retries ?? defaults.retries ?? DEFAULT_RETRIES,
      autoApprove: task.autoApprove ?? defaults.autoApprove ?? plan.autoApprove ?? true,
      gate: task.gate ?? defaults.gate,
    });
  }
  const rawWorktreeRoot = plan.worktreeRoot;
  const defaultWorktreeRoot = path.join(os.tmpdir(), "xgrid-wave", "worktrees");
  return {
    wave: plan.wave,
    baseBranch: plan.baseBranch ?? "master",
    maxParallel: Math.max(1, plan.maxParallel ?? DEFAULT_PARALLEL),
    worktreeRoot:
      rawWorktreeRoot === undefined
        ? defaultWorktreeRoot
        : path.isAbsolute(rawWorktreeRoot)
          ? rawWorktreeRoot
          : path.resolve(repoRoot, rawWorktreeRoot),
    dataLink: plan.dataLink ?? "copy",
    serverPort: plan.serverPort ?? DEFAULT_SERVER_PORT,
    autoAggregate: plan.autoAggregate ?? true,
    autoApprove: plan.autoApprove ?? true,
    finalGate: plan.finalGate ?? [],
    defaults: {
      agent: defaults.agent,
      model: defaults.model,
      budgetMs: defaults.budgetMs ?? DEFAULT_BUDGET_MS,
      retries: defaults.retries ?? DEFAULT_RETRIES,
      autoApprove: defaults.autoApprove ?? true,
      gate: defaults.gate,
    },
    tasks,
  };
}

export function topoOrder(plan: ResolvedPlan): ResolvedTask[] {
  const ordered = topoSortTasks(plan.tasks);
  if (!ordered) {
    throw new Error("plan tasks contain a dependency cycle");
  }
  const byId = new Map(ordered.map((t) => [t.id, t]));
  return plan.tasks.map((t) => byId.get(t.id)).filter((t): t is ResolvedTask => t !== undefined);
}
