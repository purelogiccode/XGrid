import type {
  AssistantMessage,
  Message,
  OpencodeClient,
  Part,
  SessionStatus,
} from "@opencode-ai/sdk";
import { respondPermission, sessionStatus, subscribeEvents } from "./opencode.ts";
import type { ModelRef } from "./types.ts";

export type RunSessionOptions = {
  client: OpencodeClient;
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

function textOfParts(parts: Part[]): string {
  return parts
    .filter((part) => part.type === "text")
    .map((part) => (part.type === "text" ? part.text : ""))
    .join("\n\n");
}

function isCompletedAssistant(message: Message): message is AssistantMessage {
  return message.role === "assistant" && message.time.completed !== undefined;
}

async function latestAssistant(
  client: OpencodeClient,
  sessionId: string,
): Promise<{ message: Message; text: string } | null> {
  const result = await client.session.messages({ path: { id: sessionId }, query: { limit: 20 } });
  const data = result.data;
  if (!Array.isArray(data)) {
    return null;
  }
  for (let index = data.length - 1; index >= 0; index -= 1) {
    const entry = data[index];
    if (entry?.info.role === "assistant") {
      return { message: entry.info, text: textOfParts(entry.parts) };
    }
  }
  return null;
}

export async function runSession(options: RunSessionOptions): Promise<RunSessionOutcome> {
  const pollMs = options.pollMs ?? 5000;
  const log = options.log ?? (() => {});
  const startedAt = Date.now();
  let sessionId = options.sessionId ?? "";

  if (sessionId.length > 0) {
    const existing = await options.client.session.get({ path: { id: sessionId } });
    if (existing.error !== undefined || existing.data === undefined) {
      log(`session ${sessionId} is gone; creating a new one`);
      sessionId = "";
    }
  }
  if (sessionId.length === 0) {
    const created = await options.client.session.create({ body: { title: options.title } });
    if (created.error !== undefined || created.data === undefined) {
      throw new Error(`failed to create session: ${JSON.stringify(created.error ?? "empty response")}`);
    }
    sessionId = created.data.id;
  }

  const before = await latestAssistant(options.client, sessionId);
  const previousAssistantId = before?.message.id;

  let lastStatus: SessionStatus["type"] = "idle";
  let permissionCount = 0;
  const subscription = subscribeEvents(options.client, (event) => {
    if (event.type === "permission.updated") {
      const permission = event.properties;
      if (permission.sessionID !== sessionId) {
        return;
      }
      permissionCount += 1;
      const response = options.autoApprove ? "always" : "reject";
      log(`permission #${permissionCount} ${permission.type} -> ${response}: ${permission.title}`);
      void respondPermission(options.client, permission.sessionID, permission.id, response).catch(
        (error: unknown) => log(`failed to answer permission: ${String(error)}`),
      );
    }
  });

  try {
    const body = {
      parts: [{ type: "text" as const, text: options.prompt }],
      ...(options.agent !== undefined ? { agent: options.agent } : {}),
      ...(options.model !== undefined ? { model: options.model } : {}),
    };
    const sent = await options.client.session.promptAsync({ path: { id: sessionId }, body });
    if (sent.error !== undefined) {
      throw new Error(`failed to start prompt: ${JSON.stringify(sent.error)}`);
    }

    let polls = 0;
    let abortSentAt: number | null = null;
    for (;;) {
      await sleep(pollMs);
      polls += 1;
      const elapsedMs = Date.now() - startedAt;
      const status = await sessionStatus(options.client, sessionId);
      lastStatus = status?.type ?? lastStatus;
      const latest = await latestAssistant(options.client, sessionId);

      if (status?.type === "retry") {
        log(`model retry #${status.attempt}: ${status.message}`);
      }

      if (latest !== null && isCompletedAssistant(latest.message) && latest.message.id !== previousAssistantId) {
        const error = latest.message.error;
        return {
          sessionId,
          completed: error === undefined,
          aborted: false,
          error: error === undefined ? null : JSON.stringify(error),
          assistantText: latest.text,
          elapsedMs,
        };
      }

      if (abortSentAt === null && elapsedMs > options.budgetMs) {
        log(`budget of ${Math.round(options.budgetMs / 60000)}m exceeded; aborting session`);
        abortSentAt = Date.now();
        await options.client.session.abort({ path: { id: sessionId } }).catch(() => undefined);
      }
      if (abortSentAt !== null && Date.now() - abortSentAt > 15000) {
        const final = await latestAssistant(options.client, sessionId);
        return {
          sessionId,
          completed: false,
          aborted: true,
          error: null,
          assistantText: final?.text ?? "",
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
