import type { TaskStatus } from "./types.ts";

export type SchedTask = {
  id: string;
  status: TaskStatus;
  dependsOn: string[];
  serial: boolean;
};

export type RunningTask = {
  id: string;
  serial: boolean;
};

const SATISFIED = new Set<TaskStatus>(["done", "merged"]);
const DEAD = new Set<TaskStatus>(["failed", "blocked", "conflict"]);

export function unmetDeps(task: SchedTask, byId: Map<string, SchedTask>): string[] {
  return task.dependsOn.filter((dep) => {
    const target = byId.get(dep);
    return target !== undefined && !SATISFIED.has(target.status);
  });
}

export function blockedByFailure(task: SchedTask, byId: Map<string, SchedTask>): boolean {
  return task.dependsOn.some((dep) => {
    const target = byId.get(dep);
    return target !== undefined && DEAD.has(target.status);
  });
}

export function computeAutoBlocked(tasks: SchedTask[]): string[] {
  const byId = new Map(tasks.map((t) => [t.id, t]));
  return tasks.filter((t) => t.status === "pending" && blockedByFailure(t, byId)).map((t) => t.id);
}

export function selectNext(tasks: SchedTask[], running: RunningTask[]): string | null {
  if (running.some((r) => r.serial)) {
    return null;
  }
  const byId = new Map(tasks.map((t) => [t.id, t]));
  let candidates = tasks.filter(
    (t) => t.status === "pending" && unmetDeps(t, byId).length === 0 && !blockedByFailure(t, byId),
  );
  if (running.length > 0) {
    candidates = candidates.filter((t) => !t.serial);
  }
  return candidates[0]?.id ?? null;
}
