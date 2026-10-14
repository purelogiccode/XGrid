import assert from "node:assert/strict";
import { test } from "node:test";
import { computeAutoBlocked, selectNext, type SchedTask } from "./scheduler.ts";

function task(id: string, status: SchedTask["status"] = "pending", dependsOn: string[] = [], serial = false): SchedTask {
  return { id, status, dependsOn, serial };
}

test("selects the first pending task with satisfied dependencies", () => {
  const tasks = [task("a"), task("b", "pending", ["a"]), task("c")];
  assert.equal(selectNext(tasks, []), "a");
});

test("does not select tasks with unmet dependencies", () => {
  const tasks = [task("a", "done"), task("b", "done"), task("c", "pending", ["a"])];
  tasks[1]!.status = "running";
  assert.equal(selectNext(tasks, [{ id: "b", serial: false }]), "c");
});

test("a running serial task blocks everything", () => {
  const tasks = [task("a", "running", [], true), task("b")];
  assert.equal(selectNext(tasks, [{ id: "a", serial: true }]), null);
});

test("serial candidates are excluded while others run", () => {
  const tasks = [task("hunt", "pending", [], true), task("work")];
  assert.equal(selectNext(tasks, [{ id: "work2", serial: false }]), "work");
  assert.equal(selectNext(tasks, []), "hunt");
});

test("failed dependencies mark dependents as blocked", () => {
  const tasks = [task("a", "failed"), task("b", "pending", ["a"]), task("c", "pending", ["b"])];
  assert.deepEqual(computeAutoBlocked(tasks), ["b"]);
  assert.equal(selectNext(tasks, []), null);
  tasks[1]!.status = "blocked";
  assert.deepEqual(computeAutoBlocked(tasks), ["c"]);
});

test("done dependencies satisfy", () => {
  const tasks = [task("a", "merged"), task("b", "pending", ["a"])];
  assert.deepEqual(computeAutoBlocked(tasks), []);
  assert.equal(selectNext(tasks, []), "b");
});

test("plan order is preserved among ready tasks", () => {
  const tasks = [task("a"), task("b"), task("c")];
  assert.equal(selectNext(tasks, [{ id: "x", serial: false }]), "a");
  tasks[0]!.status = "running";
  assert.equal(selectNext(tasks, [{ id: "a", serial: false }]), "b");
});
