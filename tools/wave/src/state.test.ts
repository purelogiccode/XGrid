import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { initialState, loadRunState, planHash, saveRunState } from "./state.ts";
import type { ResolvedPlan } from "./types.ts";

function plan(): ResolvedPlan {
  return {
    wave: "w1",
    baseBranch: "master",
    maxParallel: 4,
    worktreeRoot: "wt",
    dataLink: "none",
    serverPort: 4123,
    autoAggregate: true,
    autoApprove: true,
    finalGate: [],
    defaults: { budgetMs: 1000, retries: 1, autoApprove: true },
    tasks: [
      {
        id: "a",
        title: "A",
        prompt: "p",
        acceptance: "x",
        dependsOn: [],
        serial: false,
        branch: "wave/a",
        merge: true,
        budgetMs: 1000,
        retries: 1,
      },
    ],
  };
}

test("plan hash is stable and sensitive", () => {
  assert.equal(planHash("abc"), planHash("abc"));
  assert.notEqual(planHash("abc"), planHash("abd"));
});

test("state round-trips through disk", async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "wave-state-"));
  const statePath = path.join(dir, "nested", "state.json");
  const state = initialState(plan(), "plan.json", planHash("raw"));
  state.tasks[0]!.status = "done";
  await saveRunState(statePath, state);
  const loaded = await loadRunState(statePath);
  assert.equal(loaded?.wave, "w1");
  assert.equal(loaded?.tasks[0]!.status, "done");
  assert.equal(loaded?.planHash, planHash("raw"));
});

test("missing state loads as null", async () => {
  const loaded = await loadRunState(path.join(os.tmpdir(), "wave-missing", "state.json"));
  assert.equal(loaded, null);
});
