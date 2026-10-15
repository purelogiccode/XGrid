import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { collectDashboard } from "./dashboard.ts";
import type { ResolvedPlan, RunState } from "./types.ts";

function fakePlan(): ResolvedPlan {
  return {
    wave: "demo",
    baseBranch: "master",
    maxParallel: 1,
    worktreeRoot: "",
    dataLink: "copy",
    serverPort: 1,
    autoAggregate: false,
    autoApprove: true,
    finalGate: [],
    defaults: { budgetMs: 1, retries: 0, autoApprove: true },
    tasks: [
      {
        id: "a",
        title: "task a",
        prompt: "p",
        acceptance: "q",
        dependsOn: [],
        serial: false,
        branch: "wave/a",
        merge: true,
        budgetMs: 1,
        retries: 0,
        autoApprove: true,
      },
      {
        id: "b",
        title: "task b",
        prompt: "p",
        acceptance: "q",
        dependsOn: [],
        serial: false,
        branch: "wave/b",
        merge: true,
        budgetMs: 1,
        retries: 0,
        autoApprove: true,
      },
    ],
  };
}

function fakeState(): RunState {
  return {
    wave: "demo",
    planPath: "p",
    planHash: "h",
    baseBranch: "master",
    worktreeRoot: "",
    startedAt: "t0",
    updatedAt: "t1",
    serverUrl: "http://127.0.0.1:1",
    tasks: [
      { id: "a", status: "running", attempts: 1, branch: "wave/a", error: "boom" },
      { id: "b", status: "done", attempts: 1, branch: "wave/b", headCommit: "abcdef123456" },
    ],
  };
}

test("collector maps state onto plan tasks with log tails and result status", () => {
  const runDir = fs.mkdtempSync(path.join(os.tmpdir(), "wave-dash-"));
  fs.mkdirSync(path.join(runDir, "logs"), { recursive: true });
  fs.mkdirSync(path.join(runDir, "results"), { recursive: true });
  fs.writeFileSync(path.join(runDir, "logs", "a.log"), "l1\nl2\nl3\n", "utf8");
  fs.writeFileSync(
    path.join(runDir, "results", "b.result.json"),
    JSON.stringify({ taskId: "b", status: "done", branch: "wave/b", headCommit: "x" }),
    "utf8",
  );
  const data = collectDashboard(runDir, fakePlan(), fakeState());
  assert.equal(data.wave, "demo");
  assert.deepEqual(data.counts, { running: 1, done: 1 });
  const [a, b] = data.tasks;
  assert.equal(a?.status, "running");
  assert.equal(a?.error, "boom");
  assert.deepEqual(a?.logTail, ["l1", "l2", "l3"]);
  assert.equal(a?.resultStatus, null);
  assert.equal(b?.headCommit, "abcdef12");
  assert.equal(b?.resultStatus, "done");
  assert.deepEqual(b?.logTail, []);
  fs.rmSync(runDir, { recursive: true, force: true });
});

test("collector tolerates a missing run directory", () => {
  const data = collectDashboard(path.join(os.tmpdir(), "wave-dash-missing"), fakePlan(), fakeState());
  assert.equal(data.tasks.length, 2);
  assert.deepEqual(data.tasks[0]?.logTail, []);
});
