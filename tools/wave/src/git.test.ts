import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { gitChecked, isClean } from "./git.ts";

async function makeRepo(): Promise<string> {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), "wave-git-"));
  await gitChecked(dir, ["init", "-b", "master"]);
  await gitChecked(dir, ["config", "user.email", "wave@test.local"]);
  await gitChecked(dir, ["config", "user.name", "wave test"]);
  await fs.writeFile(path.join(dir, "tracked.txt"), "one\n", "utf8");
  await gitChecked(dir, ["add", "tracked.txt"]);
  await gitChecked(dir, ["commit", "-m", "init"]);
  return dir;
}

test("untracked files do not make the worktree dirty", async (t) => {
  const dir = await makeRepo();
  t.after(() => fs.rm(dir, { recursive: true, force: true }));
  assert.equal(await isClean(dir), true);
  await fs.writeFile(path.join(dir, "wave.plan.json"), "{}\n", "utf8");
  assert.equal(await isClean(dir), true, "an untracked plan file must not block the aggregator");
});

test("tracked modifications and staged changes make the worktree dirty", async (t) => {
  const dir = await makeRepo();
  t.after(() => fs.rm(dir, { recursive: true, force: true }));
  await fs.writeFile(path.join(dir, "tracked.txt"), "two\n", "utf8");
  assert.equal(await isClean(dir), false);
  await gitChecked(dir, ["checkout", "--", "tracked.txt"]);
  assert.equal(await isClean(dir), true);
  await fs.writeFile(path.join(dir, "tracked.txt"), "three\n", "utf8");
  await gitChecked(dir, ["add", "tracked.txt"]);
  assert.equal(await isClean(dir), false, "staged changes count as dirty");
});

test("ignored files do not make the worktree dirty", async (t) => {
  const dir = await makeRepo();
  t.after(() => fs.rm(dir, { recursive: true, force: true }));
  await fs.writeFile(path.join(dir, ".gitignore"), "scratch/\n", "utf8");
  await gitChecked(dir, ["add", ".gitignore"]);
  await gitChecked(dir, ["commit", "-m", "ignore rule"]);
  await fs.mkdir(path.join(dir, "scratch"));
  await fs.writeFile(path.join(dir, "scratch", "dump.bin"), "junk\n", "utf8");
  assert.equal(await isClean(dir), true);
});
