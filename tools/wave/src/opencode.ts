import { createOpencodeServer, createOpencodeClient } from "@opencode-ai/sdk";
import type { Event, OpencodeClient, SessionStatus } from "@opencode-ai/sdk";
import net from "node:net";

export type ServerHandle = {
  url: string;
  close(): void;
};

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function canBind(port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const tester = net.createServer();
    tester.once("error", () => resolve(false));
    tester.once("listening", () => tester.close(() => resolve(true)));
    tester.listen(port, "127.0.0.1");
  });
}

export async function pickPort(preferred: number, attempts = 200): Promise<number> {
  for (let port = preferred; port < preferred + attempts && port < 65535; port += 1) {
    if (await canBind(port)) {
      return port;
    }
  }
  throw new Error(`no bindable port found from ${preferred} upward (Windows excluded port ranges?)`);
}

export async function checkHealth(url: string): Promise<boolean> {
  try {
    const response = await fetch(`${url.replace(/\/$/, "")}/global/health`, {
      signal: AbortSignal.timeout(3000),
    });
    return response.ok;
  } catch {
    return false;
  }
}

export async function ensureServer(options: {
  port: number;
  attachUrl?: string;
  timeoutMs?: number;
  log?: (line: string) => void;
}): Promise<ServerHandle> {
  if (options.attachUrl !== undefined) {
    const url = options.attachUrl.replace(/\/$/, "");
    if (!(await checkHealth(url))) {
      throw new Error(`no healthy opencode server at ${url}`);
    }
    return { url, close: () => {} };
  }
  const preferredUrl = `http://127.0.0.1:${options.port}`;
  if (await checkHealth(preferredUrl)) {
    return { url: preferredUrl, close: () => {} };
  }
  const port = await pickPort(options.port);
  if (port !== options.port) {
    options.log?.(`[wave] port ${options.port} is not bindable (excluded range?); using ${port}`);
  }
  const server = await createOpencodeServer({
    hostname: "127.0.0.1",
    port,
    timeout: options.timeoutMs ?? 20000,
  });
  return { url: server.url.replace(/\/$/, ""), close: () => server.close() };
}

export function clientFor(serverUrl: string, directory: string): OpencodeClient {
  return createOpencodeClient({ baseUrl: serverUrl, directory });
}

export type EventSubscription = {
  close(): void;
};

export function subscribeEvents(
  client: OpencodeClient,
  handler: (event: Event) => void,
  onConnectionChange?: (connected: boolean) => void,
): EventSubscription {
  let closed = false;
  const run = async (): Promise<void> => {
    while (!closed) {
      try {
        const subscription = await client.event.subscribe();
        onConnectionChange?.(true);
        for await (const event of subscription.stream) {
          if (closed) {
            break;
          }
          handler(event);
        }
      } catch {
        onConnectionChange?.(false);
      }
      if (!closed) {
        await sleep(2000);
      }
    }
  };
  void run();
  return {
    close() {
      closed = true;
    },
  };
}

export async function sessionStatus(
  client: OpencodeClient,
  sessionId: string,
): Promise<SessionStatus | null> {
  const result = await client.session.status();
  const data = result.data as Record<string, SessionStatus> | undefined;
  return data?.[sessionId] ?? null;
}

export async function respondPermission(
  client: OpencodeClient,
  sessionId: string,
  permissionId: string,
  response: "once" | "always" | "reject",
): Promise<void> {
  await client.postSessionIdPermissionsPermissionId({
    path: { id: sessionId, permissionID: permissionId },
    body: { response },
  });
}
