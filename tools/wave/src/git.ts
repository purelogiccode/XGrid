import { spawn } from "node:child_process";

export type CommandResult = {
  code: number;
  stdout: string;
  stderr: string;
  timedOut: boolean;
};

export type SpawnOptions = {
  cwd: string;
  timeoutMs?: number;
  shell?: boolean;
  env?: NodeJS.ProcessEnv;
};

export function runCommand(command: string, args: string[], options: SpawnOptions): Promise<CommandResult> {
  return new Promise((resolve) => {
    const child = spawn(command, args, {
      cwd: options.cwd,
      shell: options.shell ?? false,
      env: options.env ?? process.env,
      windowsHide: true,
    });
    let stdout = "";
    let stderr = "";
    let timedOut = false;
    const timer =
      options.timeoutMs !== undefined
        ? setTimeout(() => {
            timedOut = true;
            child.kill();
          }, options.timeoutMs)
        : undefined;
    child.stdout?.on("data", (chunk: Buffer) => {
      stdout += chunk.toString();
    });
    child.stderr?.on("data", (chunk: Buffer) => {
      stderr += chunk.toString();
    });
    child.on("error", (error) => {
      if (timer) {
        clearTimeout(timer);
      }
      resolve({ code: -1, stdout, stderr: stderr + String(error), timedOut });
    });
    child.on("close", (code) => {
      if (timer) {
        clearTimeout(timer);
      }
      resolve({ code: code ?? -1, stdout, stderr, timedOut });
    });
  });
}

export function git(cwd: string, args: string[], timeoutMs?: number): Promise<CommandResult> {
  return runCommand("git", args, { cwd, ...(timeoutMs !== undefined ? { timeoutMs } : {}) });
}

export async function gitChecked(cwd: string, args: string[], timeoutMs?: number): Promise<string> {
  const result = await git(cwd, args, timeoutMs);
  if (result.code !== 0) {
    throw new Error(`git ${args.join(" ")} failed (${result.code}): ${result.stderr.trim() || result.stdout.trim()}`);
  }
  return result.stdout.trim();
}

export async function repoRoot(cwd: string): Promise<string> {
  return gitChecked(cwd, ["rev-parse", "--show-toplevel"]);
}

export async function currentBranch(cwd: string): Promise<string> {
  return gitChecked(cwd, ["rev-parse", "--abbrev-ref", "HEAD"]);
}

export async function revParse(cwd: string, ref: string): Promise<string | null> {
  const result = await git(cwd, ["rev-parse", "--verify", "--quiet", `${ref}^{commit}`]);
  return result.code === 0 ? result.stdout.trim() : null;
}

export async function branchExists(cwd: string, branch: string): Promise<boolean> {
  return (await revParse(cwd, `refs/heads/${branch}`)) !== null;
}

export async function isClean(cwd: string): Promise<boolean> {
  const status = await gitChecked(cwd, ["status", "--porcelain"]);
  return status.length === 0;
}

export async function logRange(cwd: string, range: string): Promise<string> {
  const result = await git(cwd, ["log", "--oneline", "--no-decorate", range]);
  return result.code === 0 ? result.stdout.trim() : "";
}

export async function diffNames(cwd: string, range: string): Promise<string[]> {
  const result = await git(cwd, ["diff", "--name-only", range]);
  if (result.code !== 0) {
    return [];
  }
  return result.stdout
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

export async function registeredWorktrees(cwd: string): Promise<string[]> {
  const result = await git(cwd, ["worktree", "list", "--porcelain"]);
  if (result.code !== 0) {
    return [];
  }
  return result.stdout
    .split(/\r?\n/)
    .filter((line) => line.startsWith("worktree "))
    .map((line) => line.slice("worktree ".length).trim());
}
