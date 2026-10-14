import assert from "node:assert/strict";
import { test } from "node:test";
import { simulate } from "./simulate.ts";
import type { ResolvedPlan, ResolvedTask } from "./types.ts";

function task(overrides: Partial<ResolvedTask> & { id: string }): ResolvedTask {
  return {
    id: overrides.id,
    title: overrides.id,
    prompt: "p",
    acceptance: "a",
    dependsOn: overrides.dependsOn ?? [],
    serial: overrides.serial ?? false,
    branch: `wave/${overrides.id}`,
    merge: true,
    budgetMs: 1000,
    retries: 0,
  };
}

function plan(tasks: ResolvedTask[], maxParallel: number): ResolvedPlan {
  return {
    wave: "sim",
    baseBranch: "master",
    maxParallel,
    worktreeRoot: "wt",
    dataLink: "none",
    serverPort: 4123,
    autoAggregate: false,
    autoApprove: true,
    finalGate: [],
    defaults: { budgetMs: 1000, retries: 0, autoApprove: true },
    tasks,
  };
}

test("an idle slot pulls the next task as soon as one finishes", () => {
  const durations = new Map([
    ["a", 60_000],
    ["b", 30_000],
    ["c", 10_000],
  ]);
  const events = simulate(plan([task({ id: "a" }), task({ id: "b" }), task({ id: "c" })], 2), durations);
  const startC = events.find((event) => event.kind === "start" && event.taskId === "c");
  assert.equal(startC?.timeMs, 30_000);
  const endA = events.find((event) => event.kind === "end" && event.taskId === "a");
  assert.equal(endA?.timeMs, 60_000);
});

test("dependencies gate the start time", () => {
  const durations = new Map([
    ["a", 20_000],
    ["b", 15_000],
  ]);
  const events = simulate(plan([task({ id: "a" }), task({ id: "b", dependsOn: ["a"] })], 4), durations);
  const startB = events.find((event) => event.kind === "start" && event.taskId === "b");
  assert.equal(startB?.timeMs, 20_000);
});

test("a serial task never overlaps another", () => {
  const durations = new Map([
    ["a", 30_000],
    ["hunt", 20_000],
  ]);
  const events = simulate(plan([task({ id: "a" }), task({ id: "hunt", serial: true })], 4), durations);
  const startHunt = events.find((event) => event.kind === "start" && event.taskId === "hunt");
  assert.equal(startHunt?.timeMs, 30_000);
});

test("simulation reaches every task in a dependency chain", () => {
  const events = simulate(
    plan([task({ id: "a" }), task({ id: "b", dependsOn: ["a"] }), task({ id: "c", dependsOn: ["b"] })], 2),
    new Map([
      ["a", 10_000],
      ["b", 10_000],
      ["c", 10_000],
    ]),
  );
  const ends = new Set(events.filter((event) => event.kind === "end").map((event) => event.taskId));
  assert.deepEqual([...ends].sort(), ["a", "b", "c"]);
});
