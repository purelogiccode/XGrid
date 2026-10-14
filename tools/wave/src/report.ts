import fs from "node:fs";
import path from "node:path";
import type { ResolvedPlan, RunState, WorkerResult } from "./types.ts";

function formatDuration(startedAt?: string, endedAt?: string): string {
  if (startedAt === undefined || endedAt === undefined) {
    return "-";
  }
  const ms = new Date(endedAt).getTime() - new Date(startedAt).getTime();
  return `${(ms / 60000).toFixed(1)}m`;
}

function readResult(runDir: string, taskId: string): WorkerResult | null {
  try {
    const text = fs.readFileSync(path.join(runDir, "results", `${taskId}.result.json`), "utf8");
    return JSON.parse(text) as WorkerResult;
  } catch {
    return null;
  }
}

export async function renderReport(
  plan: ResolvedPlan,
  state: RunState,
  runDir: string,
  reportPath: string,
): Promise<void> {
  const lines: string[] = [];
  lines.push(`# Wave ${plan.wave} — run report`);
  lines.push("");
  lines.push(`- Started: ${state.startedAt}`);
  lines.push(`- Updated: ${state.updatedAt}`);
  lines.push(`- Base branch: \`${plan.baseBranch}\``);
  lines.push(`- Worktrees: \`${plan.worktreeRoot}\``);
  lines.push("");
  lines.push("## Tasks");
  lines.push("");
  lines.push("| task | status | attempts | commits | duration | branch |");
  lines.push("| --- | --- | --- | --- | --- | --- |");
  for (const task of state.tasks) {
    lines.push(
      `| ${task.id} | ${task.status} | ${task.attempts} | ${task.headCommit?.slice(0, 8) ?? "-"} | ${formatDuration(
        task.startedAt,
        task.endedAt,
      )} | \`${task.branch}\` |`,
    );
  }
  lines.push("");

  const failing = state.tasks.filter((task) => ["failed", "blocked", "conflict"].includes(task.status));
  if (failing.length > 0) {
    lines.push("## Diagnoses (no fake fixes)");
    lines.push("");
    for (const task of failing) {
      lines.push(`### ${task.id} — ${task.status}`);
      lines.push("");
      lines.push(task.error ?? "(no diagnosis recorded)");
      if (task.gate !== undefined && !task.gate.ok) {
        lines.push("");
        lines.push("Gate output tail:");
        lines.push("```");
        lines.push(task.gate.outputTail.slice(-2000));
        lines.push("```");
      }
      lines.push("");
    }
  }

  lines.push("## Evidence per task");
  lines.push("");
  const titleById = new Map(plan.tasks.map((task) => [task.id, task.title]));
  for (const task of state.tasks) {
    const result = readResult(runDir, task.id);
    if (result === null) {
      continue;
    }
    lines.push(`### ${task.id} — ${titleById.get(task.id) ?? ""}`);
    lines.push("");
    if (result.acceptance !== undefined && result.acceptance.length > 0) {
      lines.push(`- Acceptance evidence: ${result.acceptance}`);
    }
    if (result.testsRun !== undefined && result.testsRun.length > 0) {
      lines.push("- Tests run:");
      for (const run of result.testsRun) {
        lines.push(`  - \`${run.command}\` — ${run.result} (${run.summary})`);
      }
    }
    if (result.filesChanged !== undefined && result.filesChanged.length > 0) {
      lines.push(`- Files changed: ${result.filesChanged.map((file) => `\`${file}\``).join(", ")}`);
    }
    if (result.notes !== undefined && result.notes.length > 0) {
      lines.push("");
      lines.push("Research notes to merge:");
      lines.push("");
      lines.push(result.notes);
    }
    lines.push("");
  }

  if (state.aggregate !== undefined) {
    lines.push("## Aggregation");
    lines.push("");
    lines.push(`- Merged: ${state.aggregate.merged.join(", ") || "none"}`);
    lines.push(`- Conflicts: ${state.aggregate.conflicts.join(", ") || "none"}`);
    lines.push(`- Skipped (merge=false): ${state.aggregate.skipped.join(", ") || "none"}`);
    for (const gate of state.aggregate.gateResults) {
      lines.push(`- Gate \`${gate.command}\`: ${gate.ok ? "passed" : `FAILED (exit ${gate.code})`}`);
    }
    lines.push("");
  }

  await fs.promises.mkdir(path.dirname(reportPath), { recursive: true });
  await fs.promises.writeFile(reportPath, `${lines.join("\n")}\n`, "utf8");
}
