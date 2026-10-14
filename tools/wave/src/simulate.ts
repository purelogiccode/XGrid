import { selectNext, computeAutoBlocked, type SchedTask, type RunningTask } from "./scheduler.ts";
import type { ResolvedPlan } from "./types.ts";

export type SimEvent = {
  timeMs: number;
  kind: "start" | "end" | "blocked";
  taskId: string;
};

export function defaultDurations(plan: ResolvedPlan, stepMs = 60_000): Map<string, number> {
  const durations = new Map<string, number>();
  plan.tasks.forEach((task, index) => {
    durations.set(task.id, stepMs + index * stepMs * 0.5);
  });
  return durations;
}

export function simulate(plan: ResolvedPlan, durations: Map<string, number>): SimEvent[] {
  const tasks = new Map<string, SchedTask>(
    plan.tasks.map((task) => [
      task.id,
      { id: task.id, status: "pending", dependsOn: task.dependsOn, serial: task.serial },
    ]),
  );
  const events: SimEvent[] = [];
  const running = new Map<string, { endAt: number; serial: boolean }>();
  let now = 0;
  let started = 0;

  const blockedNow = new Set<string>();
  for (;;) {
    const blocked = computeAutoBlocked([...tasks.values()]);
    for (const id of blocked) {
      if (!blockedNow.has(id)) {
        blockedNow.add(id);
        tasks.get(id)!.status = "blocked";
        events.push({ timeMs: now, kind: "blocked", taskId: id });
      }
    }

    while (running.size < plan.maxParallel) {
      const runningList: RunningTask[] = [...running.entries()].map(([id, info]) => ({ id, serial: info.serial }));
      const next = selectNext([...tasks.values()], runningList);
      if (next === null) {
        break;
      }
      const task = tasks.get(next)!;
      task.status = "running";
      running.set(next, { endAt: now + (durations.get(next) ?? 60_000), serial: task.serial });
      started += 1;
      events.push({ timeMs: now, kind: "start", taskId: next });
    }

    if (running.size === 0) {
      break;
    }
    let nextEnd = Number.POSITIVE_INFINITY;
    for (const info of running.values()) {
      nextEnd = Math.min(nextEnd, info.endAt);
    }
    now = nextEnd;
    for (const [id, info] of [...running.entries()]) {
      if (info.endAt <= now) {
        running.delete(id);
        tasks.get(id)!.status = "done";
        events.push({ timeMs: now, kind: "end", taskId: id });
      }
    }
  }
  if (started < plan.tasks.length) {
    for (const task of tasks.values()) {
      if (task.status === "pending") {
        task.status = "blocked";
        events.push({ timeMs: now, kind: "blocked", taskId: task.id });
      }
    }
  }
  return events.sort((a, b) => a.timeMs - b.timeMs || a.taskId.localeCompare(b.taskId));
}
