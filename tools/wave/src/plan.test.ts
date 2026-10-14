import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { loadPlan, validatePlan } from "./plan.ts";

function basePlan(): Record<string, unknown> {
  return {
    wave: "t",
    tasks: [
      {
        id: "a",
        title: "A",
        prompt: "do a",
        acceptance: "a is done",
      },
    ],
  };
}

test("accepts a minimal valid plan", () => {
  assert.deepEqual(validatePlan(basePlan()), []);
});

test("rejects missing wave, prompt, acceptance and bad model", () => {
  const plan = basePlan();
  plan["wave"] = "";
  (plan["tasks"] as Record<string, unknown>[])[0]!["prompt"] = undefined;
  (plan["tasks"] as Record<string, unknown>[])[0]!["acceptance"] = "";
  (plan["tasks"] as Record<string, unknown>[])[0]!["model"] = { nope: true };
  const errors = validatePlan(plan);
  assert.equal(errors.length, 4);
});

test("rejects duplicate ids and unknown dependencies", () => {
  const plan = {
    wave: "t",
    tasks: [
      { id: "a", title: "A", prompt: "p", acceptance: "x", dependsOn: ["b"] },
      { id: "a", title: "dupe", prompt: "p", acceptance: "x" },
    ],
  };
  const errors = validatePlan(plan);
  assert.ok(errors.some((e) => e.includes("duplicated")));
  assert.ok(errors.some((e) => e.includes('unknown task "b"')));
});

test("rejects dependency cycles", () => {
  const plan = {
    wave: "t",
    tasks: [
      { id: "a", title: "A", prompt: "p", acceptance: "x", dependsOn: ["b"] },
      { id: "b", title: "B", prompt: "p", acceptance: "x", dependsOn: ["a"] },
    ],
  };
  const errors = validatePlan(plan);
  assert.ok(errors.some((e) => e.includes("cycle")));
});

test("loadPlan resolves defaults, promptFile and branches", async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "wave-plan-"));
  fs.writeFileSync(path.join(root, "task.md"), "body from file\n", "utf8");
  const planPath = path.join(root, "wave.plan.json");
  fs.writeFileSync(
    planPath,
    JSON.stringify({
      wave: "w",
      worktreeRoot: "wave-worktrees",
      defaults: { budgetMs: 1000, retries: 2, agent: "build" },
      tasks: [
        { id: "a", title: "A", promptFile: "task.md", acceptance: "x" },
        { id: "b", title: "B", prompt: "inline", acceptance: "y", dependsOn: ["a"], serial: true },
      ],
    }),
    "utf8",
  );
  const plan = await loadPlan(planPath, root);
  assert.equal(plan.wave, "w");
  assert.equal(plan.baseBranch, "master");
  assert.equal(plan.maxParallel, 4);
  assert.equal(plan.dataLink, "copy");
  assert.equal(plan.tasks[0]!.prompt, "body from file");
  assert.equal(plan.tasks[0]!.budgetMs, 1000);
  assert.equal(plan.tasks[0]!.retries, 2);
  assert.equal(plan.tasks[0]!.agent, "build");
  assert.equal(plan.tasks[0]!.branch, "wave/a");
  assert.equal(plan.tasks[1]!.serial, true);
  assert.equal(plan.tasks[1]!.merge, true);
  assert.equal(plan.worktreeRoot, path.resolve(root, "wave-worktrees"));
});

test("default worktree root stays outside the repo", async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "wave-plan-"));
  const planPath = path.join(root, "wave.plan.json");
  fs.writeFileSync(
    planPath,
    JSON.stringify({ wave: "w", tasks: [{ id: "a", title: "A", prompt: "p", acceptance: "x" }] }),
    "utf8",
  );
  const plan = await loadPlan(planPath, root);
  assert.equal(plan.worktreeRoot, path.join(os.tmpdir(), "xgrid-wave", "worktrees"));
  assert.ok(!plan.worktreeRoot.startsWith(root));
});
