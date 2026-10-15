import assert from "node:assert/strict";
import test from "node:test";
import type { SessionMessageAssistant } from "@opencode/client";
import { isTurnComplete } from "./session-run.ts";

function fakeAssistant(
  id: string,
  finish: SessionMessageAssistant["finish"],
  completed: boolean,
): SessionMessageAssistant {
  return {
    id,
    time: completed ? { created: 1, completed: 2 } : { created: 1 },
    type: "assistant",
    agent: "build",
    model: { providerID: "p", id: "m" },
    content: [],
    finish,
  };
}

test("mid-turn tool-calls messages never complete, even when stamped completed", () => {
  assert.equal(isTurnComplete(fakeAssistant("new", "tool-calls", true), "old"), false);
  assert.equal(isTurnComplete(fakeAssistant("new", "tool-calls", false), "old"), false);
});

test("terminal finishes complete once and only once", () => {
  for (const finish of ["stop", "length", "error", "content-filter", "unknown", undefined] as const) {
    assert.equal(isTurnComplete(fakeAssistant("new", finish, true), "old"), true, `finish=${String(finish)}`);
  }
});

test("the pre-prompt message and incomplete messages never complete", () => {
  assert.equal(isTurnComplete(fakeAssistant("old", "stop", true), "old"), false);
  assert.equal(isTurnComplete(fakeAssistant("new", "stop", false), "old"), false);
  assert.equal(isTurnComplete(fakeAssistant("new", undefined, false), undefined), false);
});
