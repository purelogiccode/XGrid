import fs from "node:fs";
import path from "node:path";
import { branchExists, git, gitChecked, registeredWorktrees } from "./git.ts";

export type WorktreeOptions = {
  repoRoot: string;
  worktreeRoot: string;
  taskId: string;
  branch: string;
  baseBranch: string;
  dataLink: "copy" | "junction" | "none";
};

export type WorktreeInfo = {
  worktree: string;
  branch: string;
  reused: boolean;
};

function samePath(a: string, b: string): boolean {
  const normalize = (p: string) => path.resolve(p).replace(/[\\/]+$/, "").toLowerCase();
  return normalize(a) === normalize(b);
}

export async function ensureWorktree(options: WorktreeOptions): Promise<WorktreeInfo> {
  const worktree = path.join(options.worktreeRoot, options.taskId);
  await fs.promises.mkdir(options.worktreeRoot, { recursive: true });
  const registered = await registeredWorktrees(options.repoRoot);
  const isRegistered = registered.some((entry) => samePath(entry, worktree));
  if (isRegistered) {
    await linkSharedAssets(options.repoRoot, worktree, options.dataLink);
    return { worktree, branch: options.branch, reused: true };
  }
  if (fs.existsSync(worktree)) {
    throw new Error(
      `worktree path ${worktree} already exists but is not a registered git worktree; remove it or pick another task id`,
    );
  }
  if (await branchExists(options.repoRoot, options.branch)) {
    await gitChecked(options.repoRoot, ["worktree", "add", worktree, options.branch]);
  } else {
    await gitChecked(options.repoRoot, ["worktree", "add", "-b", options.branch, worktree, options.baseBranch]);
  }
  await linkSharedAssets(options.repoRoot, worktree, options.dataLink);
  return { worktree, branch: options.branch, reused: false };
}

async function linkSharedAssets(
  repoRoot: string,
  worktree: string,
  dataLink: "copy" | "junction" | "none",
): Promise<void> {
  const referencesSource = path.join(repoRoot, "References");
  const referencesTarget = path.join(worktree, "References");
  if (fs.existsSync(referencesSource) && !fs.existsSync(referencesTarget)) {
    fs.symlinkSync(referencesSource, referencesTarget, "junction");
  }
  const dataSource = path.join(repoRoot, "data");
  const dataTarget = path.join(worktree, "data");
  if (!fs.existsSync(dataSource) || fs.existsSync(dataTarget)) {
    return;
  }
  if (dataLink === "junction") {
    fs.symlinkSync(dataSource, dataTarget, "junction");
  } else if (dataLink === "copy") {
    await fs.promises.cp(dataSource, dataTarget, { recursive: true });
  }
}

export async function removeWorktree(repoRoot: string, worktree: string): Promise<void> {
  const result = await git(repoRoot, ["worktree", "remove", "--force", worktree]);
  if (result.code !== 0) {
    if (fs.existsSync(worktree)) {
      await fs.promises.rm(worktree, { recursive: true, force: true, maxRetries: 3 });
    }
    await git(repoRoot, ["worktree", "prune"]);
  }
}

export async function pruneWorktrees(repoRoot: string): Promise<void> {
  await git(repoRoot, ["worktree", "prune"]);
}
