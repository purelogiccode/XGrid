import type { TestRun, WorkerResult } from "./types.ts";

export type ParseResult =
  | { ok: true; value: WorkerResult }
  | { ok: false; errors: string[] };

const STATUSES = new Set(["done", "blocked", "failed"]);

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

export function parseWorkerResult(text: string): ParseResult {
  let raw: unknown;
  try {
    raw = JSON.parse(text);
  } catch (error) {
    return { ok: false, errors: [`not valid JSON: ${(error as Error).message}`] };
  }
  if (!isRecord(raw)) {
    return { ok: false, errors: ["result must be a JSON object"] };
  }
  const errors: string[] = [];
  if (typeof raw["taskId"] !== "string" || raw["taskId"].length === 0) {
    errors.push("taskId must be a non-empty string");
  }
  if (typeof raw["status"] !== "string" || !STATUSES.has(raw["status"])) {
    errors.push('status must be "done", "blocked" or "failed"');
  }
  if (typeof raw["branch"] !== "string" || raw["branch"].length === 0) {
    errors.push("branch must be a non-empty string");
  }
  if (typeof raw["headCommit"] !== "string" || !/^[0-9a-f]{7,40}$/i.test(raw["headCommit"])) {
    errors.push("headCommit must be a hex commit sha");
  }
  if (raw["filesChanged"] !== undefined) {
    if (!Array.isArray(raw["filesChanged"]) || raw["filesChanged"].some((f) => typeof f !== "string")) {
      errors.push("filesChanged must be an array of strings");
    }
  }
  if (raw["testsRun"] !== undefined) {
    if (!Array.isArray(raw["testsRun"])) {
      errors.push("testsRun must be an array");
    } else {
      raw["testsRun"].forEach((run, index) => {
        if (
          !isRecord(run) ||
          typeof run["command"] !== "string" ||
          typeof run["result"] !== "string" ||
          !["pass", "fail"].includes(run["result"]) ||
          typeof run["summary"] !== "string"
        ) {
          errors.push(`testsRun[${index}] must be { command, result: "pass"|"fail", summary }`);
        }
      });
    }
  }
  for (const key of ["acceptance", "notes", "blockers"]) {
    if (raw[key] !== undefined && typeof raw[key] !== "string") {
      errors.push(`${key} must be a string`);
    }
  }
  if (errors.length > 0) {
    return { ok: false, errors };
  }
  return {
    ok: true,
    value: {
      taskId: raw["taskId"] as string,
      status: raw["status"] as WorkerResult["status"],
      branch: raw["branch"] as string,
      headCommit: raw["headCommit"] as string,
      filesChanged: (raw["filesChanged"] as string[] | undefined) ?? [],
      testsRun: (raw["testsRun"] as TestRun[] | undefined) ?? [],
      acceptance: (raw["acceptance"] as string | undefined) ?? "",
      notes: (raw["notes"] as string | undefined) ?? "",
      blockers: (raw["blockers"] as string | undefined) ?? "",
    },
  };
}

export function extractJsonCandidates(text: string): string[] {
  const candidates: string[] = [];
  const fenced = text.matchAll(/```(?:json)?\s*\n([\s\S]*?)```/g);
  for (const match of fenced) {
    const body = match[1];
    if (body !== undefined) {
      candidates.push(body.trim());
    }
  }
  const first = text.indexOf("{");
  const last = text.lastIndexOf("}");
  if (first >= 0 && last > first) {
    candidates.push(text.slice(first, last + 1));
  }
  return candidates;
}

export function findWorkerResult(rawFile: string | undefined, assistantText: string): ParseResult {
  const errors: string[] = [];
  if (rawFile !== undefined && rawFile.trim().length > 0) {
    const parsed = parseWorkerResult(rawFile);
    if (parsed.ok) {
      return parsed;
    }
    errors.push(`result file: ${parsed.errors.join("; ")}`);
  }
  for (const candidate of extractJsonCandidates(assistantText)) {
    if (!candidate.includes("taskId")) {
      continue;
    }
    const parsed = parseWorkerResult(candidate);
    if (parsed.ok) {
      return parsed;
    }
    errors.push(`assistant message: ${parsed.errors.join("; ")}`);
  }
  if (errors.length === 0) {
    errors.push("no result file and no JSON report found in the final assistant message");
  }
  return { ok: false, errors };
}
