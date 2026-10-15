import type { OpenCodeClient, SessionMessageAssistant, SessionMessageInfo } from "@opencode/client";
import { respondPermission, subscribeEvents } from "./opencode.ts";
import type { ModelRef } from "./types.ts";

export type RunSessionOptions = {
  client: OpenCodeClient;
  directory: string;
  title: string;
  prompt: string;
  agent?: string | undefined;
  model?: ModelRef | undefined;
  budgetMs: number;
  autoApprove: boolean;
  sessionId?: string | undefined;
  pollMs?: number;
  log?: ((line: string) => void) | undefined;
};

export type RunSessionOutcome = {
  sessionId: string;
  completed: boolean;
  aborted: boolean;
  error: string | null;
  assistantText: string;
  elapsedMs: number;
};

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function textOfContent(content: SessionMessageAssistant["content"]): string {
  const texts: string[] = [];
  for (const part of content) {
    if (part.type === "text") {
      texts.push(part.text);
    }
  }
  return texts.join("\n\n");
}

async function latestAssistant(
  client: OpenCodeClient,
  sessionId: string,
): Promise<SessionMessageAssistant | null> {
  let entries: SessionMessageInfo[];
  try {
    const result = await client.message.list({
      sessionID: sessionId,
      limit: 20,
      order: "desc",
      type: "assistant",
    });
    entries = result.data;
  } catch {
    return null;
  }
  let latest: SessionMessageAssistant | undefined;
  for (const entry of entries) {
    if (entry.type !== "assistant") {
      continue;
    }
    if (latest === undefined || entry.time.created >= latest.time.created) {
      latest = entry;
    }
  }
  return latest ?? null;
}

export async function runSession(options: RunSessionOptions): Promise<RunSessionOutcome> {
  const pollMs = options.pollMs ?? 5000;
  const log = options.log ?? (() => {});
  const startedAt = Date.now();
  let sessionId = options.sessionId ?? "";

  if (sessionId.length > 0) {
    try {
      await options.client.session.get({ sessionID: sessionId });
    } catch {
      log(`session ${sessionId} is gone; creating a new one`);
      sessionId = "";
    }
  }
  if (sessionId.length === 0) {
    try {
      const created = await options.client.session.create({
        title: options.title,
        ...(options.agent === undefined ? {} : { agent: options.agent }),
        ...(options.model === undefined
          ? {}
          : { model: { providerID: options.model.providerID, id: options.model.modelID } }),
        location: { directory: options.directory },
      });
      sessionId = created.id;
    } catch (error) {
      throw new Error(`failed to create session: ${String(error)}`);
    }
  }

  const before = await latestAssistant(options.client, sessionId);
  const previousAssistantId = before?.id;

  let lastStatus: "idle" | "busy" | "retry" = "idle";
  let permissionCount = 0;
  const subscription = subscribeEvents(options.client, (event) => {
    if (event.type === "permission.asked") {
      if (event.data.sessionID !== sessionId) {
        return;
      }
      permissionCount += 1;
      const decision = options.autoApprove ? "always" : "reject";
      const detail = event.data.message ?? event.data.resources.join(", ");
      log(`permission #${permissionCount} ${event.data.action} -> ${decision}: ${detail}`);
      void respondPermission(options.client, event.data.sessionID, event.data.id, decision).catch(
        (error: unknown) => log(`failed to answer permission: ${String(error)}`),
      );
      return;
    }
    if (event.type === "session.status" && event.data.sessionID === sessionId) {
      lastStatus = event.data.status.type;
      return;
    }
    if (event.type === "session.retry.scheduled" && event.data.sessionID === sessionId) {
      log(`model retry #${event.data.attempt}: ${event.data.error.message}`);
    }
  });

  try {
    try {
      await options.client.session.prompt({ sessionID: sessionId, text: options.prompt });
    } catch (error) {
      throw new Error(`failed to start prompt: ${String(error)}`);
    }

    let polls = 0;
    let abortSentAt: number | null = null;
    for (;;) {
      await sleep(pollMs);
      polls += 1;
      const elapsedMs = Date.now() - startedAt;
      const latest = await latestAssistant(options.client, sessionId);

      if (latest !== null && latest.time.completed !== undefined && latest.id !== previousAssistantId) {
        const error = latest.error;
        return {
          sessionId,
          completed: error === undefined,
          aborted: false,
          error: error === undefined ? null : JSON.stringify(error),
          assistantText: textOfContent(latest.content),
          elapsedMs,
        };
      }

      if (abortSentAt === null && elapsedMs > options.budgetMs) {
        log(`budget of ${Math.round(options.budgetMs / 60000)}m exceeded; interrupting session`);
        abortSentAt = Date.now();
        await options.client.session.interrupt({ sessionID: sessionId }).catch(() => undefined);
      }
      if (abortSentAt !== null && Date.now() - abortSentAt > 15000) {
        const final = await latestAssistant(options.client, sessionId);
        return {
          sessionId,
          completed: false,
          aborted: true,
          error: null,
          assistantText: final === null ? "" : textOfContent(final.content),
          elapsedMs,
        };
      }

      if (polls % 6 === 0) {
        log(`elapsed ${Math.round(elapsedMs / 1000)}s status=${lastStatus}`);
      }
    }
  } finally {
    subscription.close();
  }
}
