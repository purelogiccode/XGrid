import path from "node:path";
import { clientFor } from "./opencode.ts";
import { runSession } from "./session-run.ts";
import { currentBranch, git, gitChecked, isClean } from "./git.ts";
import { runGate } from "./dispatch.ts";
import { topoOrder } from "./plan.ts";
import { saveRunState, taskStateById } from "./state.ts";
import { renderReport } from "./report.ts";
import type { AggregateState, ResolvedPlan, ResolvedTask, RunState } from "./types.ts";

export type AggregateOptions = {
  repoRoot: string;
  plan: ResolvedPlan;
  state: RunState;
  statePath: string;
  runDir: string;
  serverUrl: string;
  resolver: boolean;
  log: (line: string) => void;
};

export async function aggregate(options: AggregateOptions): Promise<AggregateState> {
  const { state, plan } = options;
  const aggregateState: AggregateState = state.aggregate ?? {
    merged: [],
    conflicts: [],
    skipped: [],
    gateResults: [],
  };
  aggregateState.startedAt ??= new Date().toISOString();
  aggregateState.endedAt = undefined;
  state.aggregate = aggregateState;

  const branch = await currentBranch(options.repoRoot);
  if (branch !== plan.baseBranch) {
    if (!(await isClean(options.repoRoot))) {
      throw new Error(`repo is on "${branch}" with uncommitted changes; expected "${plan.baseBranch}"`);
    }
    await gitChecked(options.repoRoot, ["checkout", plan.baseBranch]);
    options.log(`[aggregate] checked out ${plan.baseBranch}`);
  }
  if (!(await isClean(options.repoRoot))) {
    throw new Error("main worktree is not clean; refusing to aggregate");
  }

  const taskStates = taskStateById(state);
  for (const task of topoOrder(plan)) {
    if (!task.merge) {
      aggregateState.skipped.push(task.id);
      continue;
    }
    const taskState = taskStates.get(task.id);
    if (taskState === undefined || taskState.status !== "done" || aggregateState.merged.includes(task.id)) {
      continue;
    }
    options.log(`[aggregate] merging ${task.branch} (${task.title})`);
    const merge = await git(options.repoRoot, [
      "merge",
      "--no-ff",
      task.branch,
      "-m",
      `Merge ${task.branch}: ${task.title}`,
    ]);
    let mergedOk = merge.code === 0;
    if (!mergedOk) {
      const conflictOutput = `${merge.stdout}\n${merge.stderr}`;
      await git(options.repoRoot, ["merge", "--abort"]);
      options.log(`[aggregate] conflict merging ${task.branch}`);
      if (options.resolver) {
        mergedOk = await resolveConflict(task, conflictOutput);
      }
      if (!mergedOk) {
        taskState.status = "conflict";
        taskState.error = "merge conflict";
        aggregateState.conflicts.push(task.id);
        options.log(`[aggregate] conflict left unresolved for ${task.id}; recorded for human follow-up`);
        await saveRunState(options.statePath, state);
        continue;
      }
    }
    taskState.status = "merged";
    taskState.error = undefined;
    aggregateState.merged.push(task.id);
    options.log(`[aggregate] ${task.id} merged`);
    await saveRunState(options.statePath, state);
  }

  for (const command of plan.finalGate) {
    options.log(`[aggregate] final gate: ${command}`);
    const result = await runGate(command, options.repoRoot);
    aggregateState.gateResults.push(result);
    options.log(
      result.ok
        ? `[aggregate] final gate passed: ${command}`
        : `[aggregate] final gate FAILED (exit ${result.code}): ${command}`,
    );
  }

  aggregateState.endedAt = new Date().toISOString();
  await saveRunState(options.statePath, state);
  const reportPath = path.join(options.runDir, "report.md");
  await renderReport(plan, state, options.runDir, reportPath);
  options.log(`[aggregate] report written to ${reportPath}`);
  return aggregateState;

  async function resolveConflict(task: ResolvedTask, conflictOutput: string): Promise<boolean> {
    options.log(`[aggregate] spawning resolver session for ${task.branch}`);
    const client = clientFor(options.serverUrl, options.repoRoot);
    const prompt = [
      `You are the wave aggregator resolver for wave "${plan.wave}".`,
      ``,
      `A sequential merge of branch \`${task.branch}\` into \`${plan.baseBranch}\` was aborted because of conflicts.`,
      `Repository root: ${options.repoRoot}. The working tree is clean; the merge must be redone by you.`,
      ``,
      `Conflict output from the aborted merge:`,
      "```",
      conflictOutput.slice(-3000),
      "```",
      ``,
      `Your job:`,
      `1. Redo \`git merge --no-ff ${task.branch}\` and resolve every conflict file by file.`,
      `2. Follow the repo's conventions (AGENTS.md): sequential merges, no unrelated edits, renumber research-note sections (section symbol conflicts) in the union style used in docs/research-notes/.`,
      `3. Stage and commit the merge (a normal merge commit).`,
      `4. Verify the merged result builds when a quick build is available; do not run the full suite.`,
      `5. Leave the working tree clean. Do not touch any other branch or worktree.`,
      ``,
      `When you are done, reply with a short summary of the resolution and the merge commit sha.`,
    ].join("\n");
    const outcome = await runSession({
      client,
      title: `wave ${plan.wave} resolver (${task.id})`,
      prompt,
      budgetMs: 30 * 60 * 1000,
      autoApprove: true,
      log: (line) => options.log(`[resolver ${task.id}] ${line}`),
    }).catch((error: unknown) => {
      options.log(`[aggregate] resolver failed for ${task.id}: ${String(error)}`);
      return null;
    });
    if (outcome === null || !outcome.completed) {
      return false;
    }
    const mergeHead = await git(options.repoRoot, ["rev-parse", "-q", "--verify", "MERGE_HEAD"]);
    if (mergeHead.code === 0) {
      await git(options.repoRoot, ["merge", "--abort"]);
      options.log(`[aggregate] resolver left an unfinished merge for ${task.id}; aborted`);
      return false;
    }
    return isClean(options.repoRoot);
  }
}
