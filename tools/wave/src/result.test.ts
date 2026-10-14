import assert from "node:assert/strict";
import { test } from "node:test";
import { extractJsonCandidates, findWorkerResult, parseWorkerResult } from "./result.ts";

const valid = {
  taskId: "alpha",
  status: "done",
  branch: "wave/alpha",
  headCommit: "0123456789abcdef0123456789abcdef01234567",
  filesChanged: ["src/a.cs"],
  testsRun: [{ command: "dotnet test", result: "pass", summary: "all green" }],
  acceptance: "counter went up",
  notes: "§ note",
  blockers: "",
};

test("accepts a valid result", () => {
  const parsed = parseWorkerResult(JSON.stringify(valid));
  assert.equal(parsed.ok, true);
  if (parsed.ok) {
    assert.equal(parsed.value.taskId, "alpha");
    assert.equal(parsed.value.testsRun?.[0]?.result, "pass");
  }
});

test("rejects bad status and bad sha", () => {
  const parsed = parseWorkerResult(JSON.stringify({ ...valid, status: "mostly-done", headCommit: "zzz" }));
  assert.equal(parsed.ok, false);
  if (!parsed.ok) {
    assert.equal(parsed.errors.length, 2);
  }
});

test("extracts fenced json candidates", () => {
  const text = "summary\n```json\n{\"taskId\": \"x\"}\n```\ntail";
  const candidates = extractJsonCandidates(text);
  assert.ok(candidates.some((candidate) => candidate.includes("taskId")));
});

test("falls back to the assistant message when the result file is missing", () => {
  const text = `Done.\n\`\`\`json\n${JSON.stringify(valid)}\n\`\`\``;
  const parsed = findWorkerResult(undefined, text);
  assert.equal(parsed.ok, true);
});

test("reports no-result when nothing is available", () => {
  const parsed = findWorkerResult(undefined, "I finished everything, trust me.");
  assert.equal(parsed.ok, false);
  if (!parsed.ok) {
    assert.ok(parsed.errors[0]!.includes("no result file"));
  }
});

test("prefers a valid file over a broken assistant message", () => {
  const parsed = findWorkerResult(JSON.stringify(valid), "no json here");
  assert.equal(parsed.ok, true);
});
