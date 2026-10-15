import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { aggregate } from "./aggregate.ts";
import { git, gitChecked } from "./git.ts";
import { initialState } from "./state.ts";
import type { AggregateOptions } from "./aggregate.ts";
import type { ResolvedPlan, RunState } from "./types.ts";

const SERVER_URL = "http://127.0.0.1:1";

async function makeRepo(): Promise<string> {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), "wave-agg-"));
  await gitChecked(dir, ["init", "-b", "master"]);
  await gitChecked(dir, ["config", "user.email", "wave@test.local"]);
  await gitChecked(dir, ["config", "user.name", "wave test"]);
  await fs.writeFile(path.join(dir, "base.txt"), "base\n", "utf8");
  await gitChecked(dir, ["add", "base.txt"]);
  await gitChecked(dir, ["commit", "-m", "base"]);
  await gitChecked(dir, ["checkout", "-b", "wave/a"]);
  await fs.writeFile(path.join(dir, "a.txt"), "a\n", "utf8");
  await gitChecked(dir, ["add", "a.txt"]);
  await gitChecked(dir, ["commit", "-m", "task a"]);
  await gitChecked(dir, ["checkout", "master"]);
  return dir;
}

function plan(): ResolvedPlan {
  return {
    wave: "agg",
    baseBranch: "master",
    maxParallel: 1,
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
        autoApprove: true,
      },
    ],
  };
}

function doneState(resolved: ResolvedPlan): RunState {
  const state = initialState(resolved, "plan.json", "hash");
  const task = state.tasks[0]!;
  task.status = "done";
  task.headCommit = "deadbeef";
  return state;
}

async function optionsFor(
  dir: string,
  resolved: ResolvedPlan,
  state: RunState,
  runDir: string,
): Promise<AggregateOptions> {
  return {
    repoRoot: dir,
    plan: resolved,
    state,
    statePath: path.join(runDir, "state.json"),
    runDir,
    serverUrl: SERVER_URL,
    resolver: false,
    log: () => {},
  };
}

test("aggregate merges a done task even with untracked files present", async (t) => {
  const dir = await makeRepo();
  const runDir = await fs.mkdtemp(path.join(os.tmpdir(), "wave-agg-run-"));
  t.after(() => fs.rm(dir, { recursive: true, force: true }));
  t.after(() => fs.rm(runDir, { recursive: true, force: true }));
  await fs.writeFile(path.join(dir, "wave.plan.json"), "{}\n", "utf8");

  const resolved = plan();
  const state = doneState(resolved);
  const aggregateState = await aggregate(await optionsFor(dir, resolved, state, runDir));

  assert.deepEqual(aggregateState.merged, ["a"]);
  assert.deepEqual(aggregateState.conflicts, []);
  assert.deepEqual(state.aggregate?.merged, ["a"]);
  const merged = await fs.readFile(path.join(dir, "a.txt"), "utf8");
  assert.equal(merged.replace(/\r\n/g, "\n"), "a\n");
  const untracked = await fs.readFile(path.join(dir, "wave.plan.json"), "utf8");
  assert.equal(untracked.replace(/\r\n/g, "\n"), "{}\n");
  const parents = await gitChecked(dir, ["rev-list", "--parents", "-n", "1", "HEAD"]);
  assert.equal(parents.split(/\s+/).length, 3, "merge commit must have two parents");
  await fs.access(path.join(runDir, "report.md"));
});

test("aggregate refuses tracked changes and does not merge", async (t) => {
  const dir = await makeRepo();
  const runDir = await fs.mkdtemp(path.join(os.tmpdir(), "wave-agg-run-"));
  t.after(() => fs.rm(dir, { recursive: true, force: true }));
  t.after(() => fs.rm(runDir, { recursive: true, force: true }));
  await fs.writeFile(path.join(dir, "base.txt"), "dirty\n", "utf8");

  const resolved = plan();
  const state = doneState(resolved);
  await assert.rejects(
    aggregate(await optionsFor(dir, resolved, state, runDir)),
    /uncommitted tracked changes/,
  );
  const merged = await git(dir, ["merge-base", "--is-ancestor", "wave/a", "master"]);
  assert.notEqual(merged.code, 0, "wave/a must not be an ancestor of master");
});
