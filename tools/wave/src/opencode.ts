import { OpenCode } from "@opencode/client";
import type { OpenCodeClient, V2Event } from "@opencode/client";
import { spawn, spawnSync, type ChildProcess } from "node:child_process";
import fs from "node:fs/promises";
import net from "node:net";
import os from "node:os";
import path from "node:path";

export type BasicAuth = {
  username: string;
  password: string;
};

export type ServerHandle = {
  url: string;
  auth: BasicAuth | undefined;
  close(): void;
};

export type EventSubscription = {
  close(): void;
};

const AUTH_USERNAME = "opencode";
const INFO_TIMEOUT_MS = 3000;
const START_POLL_MS = 250;

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export function basicAuthHeader(auth: BasicAuth): string {
  return `Basic ${Buffer.from(`${auth.username}:${auth.password}`).toString("base64")}`;
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

type ServerInfo = {
  version: string;
  pid: number;
};

type Registration = {
  url: string;
  pid: number;
  password?: string;
};

async function readRegistration(file: string): Promise<Registration | undefined> {
  try {
    const parsed = JSON.parse(await fs.readFile(file, "utf8")) as Record<string, unknown>;
    if (typeof parsed["url"] !== "string" || typeof parsed["pid"] !== "number") {
      return undefined;
    }
    const registration: Registration = { url: parsed["url"], pid: parsed["pid"] };
    if (typeof parsed["password"] === "string") {
      registration.password = parsed["password"];
    }
    return registration;
  } catch {
    return undefined;
  }
}

function registrationCandidates(stateDir: string | undefined): string[] {
  const files: string[] = [];
  if (stateDir !== undefined) {
    files.push(path.join(stateDir, "opencode", "service.json"));
  }
  for (const root of [process.env["XDG_STATE_HOME"], path.join(os.homedir(), ".local", "state")]) {
    if (root !== undefined) {
      files.push(path.join(root, "opencode", "service.json"));
    }
  }
  return [...new Set(files)];
}

async function authCandidates(stateDir: string | undefined): Promise<BasicAuth[]> {
  const candidates: BasicAuth[] = [];
  for (const file of registrationCandidates(stateDir)) {
    const registration = await readRegistration(file);
    const password = registration?.password;
    if (password !== undefined && !candidates.some((candidate) => candidate.password === password)) {
      candidates.push({ username: AUTH_USERNAME, password });
    }
  }
  return candidates;
}

type Probe = {
  ok: boolean;
  status: number | undefined;
  info: ServerInfo | undefined;
};

async function probeInfo(url: string, auth: BasicAuth | undefined): Promise<Probe> {
  try {
    const response = await fetch(`${url.replace(/\/$/, "")}/api/info`, {
      headers: auth === undefined ? undefined : { authorization: basicAuthHeader(auth) },
      signal: AbortSignal.timeout(INFO_TIMEOUT_MS),
    });
    const body = (await response.json().catch(() => undefined)) as Record<string, unknown> | undefined;
    const info =
      body !== undefined && typeof body["version"] === "string" && typeof body["pid"] === "number"
        ? { version: body["version"], pid: body["pid"] }
        : undefined;
    return { ok: response.ok && info !== undefined, status: response.status, info };
  } catch {
    return { ok: false, status: undefined, info: undefined };
  }
}

async function attach(url: string, stateDir: string | undefined): Promise<ServerHandle | undefined> {
  let probe = await probeInfo(url, undefined);
  let auth: BasicAuth | undefined;
  if (!probe.ok && probe.status === 401) {
    for (const candidate of await authCandidates(stateDir)) {
      probe = await probeInfo(url, candidate);
      if (probe.ok) {
        auth = candidate;
        break;
      }
    }
  }
  if (!probe.ok || probe.info === undefined) {
    return undefined;
  }
  if (!probe.info.version.startsWith("2.")) {
    throw new Error(`opencode server at ${url} reports version ${probe.info.version}; this tool needs the v2 API`);
  }
  return { url, auth, close: () => {} };
}

export async function ensureServer(options: {
  port: number;
  stateDir?: string | undefined;
  attachUrl?: string | undefined;
  timeoutMs?: number | undefined;
  log?: ((line: string) => void) | undefined;
}): Promise<ServerHandle> {
  if (options.attachUrl !== undefined) {
    const url = options.attachUrl.replace(/\/$/, "");
    const handle = await attach(url, options.stateDir);
    if (handle === undefined) {
      throw new Error(`no healthy opencode v2 server at ${url} (start one with \`opencode serve\`)`);
    }
    return handle;
  }

  const preferredUrl = `http://127.0.0.1:${options.port}`;
  const existing = await attach(preferredUrl, options.stateDir).catch(() => undefined);
  if (existing !== undefined) {
    options.log?.(`[wave] reusing healthy opencode server on port ${options.port}`);
    return existing;
  }

  const port = await pickPort(options.port);
  if (port !== options.port) {
    options.log?.(`[wave] port ${options.port} is not bindable (excluded range?); using ${port}`);
  }
  const stateDir = options.stateDir ?? path.join(os.tmpdir(), "xgrid-wave", "service", String(port));
  await fs.mkdir(path.join(stateDir, "opencode"), { recursive: true });

  const child = spawn("opencode", ["serve", "--service", "--hostname", "127.0.0.1", "--port", String(port)], {
    env: { ...process.env, XDG_STATE_HOME: stateDir },
    windowsHide: true,
    stdio: ["ignore", "ignore", "pipe"],
  });
  let stderrTail = "";
  child.stderr?.on("data", (chunk: Buffer) => {
    stderrTail = `${stderrTail}${chunk.toString()}`.slice(-8192);
  });
  child.on("error", () => undefined);

  const registrationFile = path.join(stateDir, "opencode", "service.json");
  const timeoutMs = options.timeoutMs ?? 20000;
  const deadline = Date.now() + timeoutMs;
  const url = `http://127.0.0.1:${port}`;
  for (;;) {
    const registration = await readRegistration(registrationFile);
    const auth =
      registration?.password === undefined
        ? undefined
        : { username: AUTH_USERNAME, password: registration.password };
    const probe = await probeInfo(url, auth);
    if (
      probe.ok &&
      probe.info !== undefined &&
      probe.info.version.startsWith("2.") &&
      (registration === undefined || probe.info.pid === registration.pid)
    ) {
      return {
        url,
        auth,
        close() {
          stopServerProcess(child);
        },
      };
    }
    if (child.exitCode !== null) {
      throw new Error(
        `opencode server exited with code ${child.exitCode} before becoming healthy on port ${port}${stderrSuffix(stderrTail)}`,
      );
    }
    if (Date.now() >= deadline) {
      stopServerProcess(child);
      throw new Error(`opencode server did not become healthy on port ${port} within ${timeoutMs}ms${stderrSuffix(stderrTail)}`);
    }
    await sleep(START_POLL_MS);
  }
}

function stderrSuffix(stderr: string): string {
  const tail = stderr.trim().slice(-400);
  return tail.length > 0 ? `\nserver stderr: ${tail}` : "";
}

function stopServerProcess(child: ChildProcess): void {
  if (child.exitCode !== null || child.signalCode !== null) {
    return;
  }
  if (process.platform === "win32" && child.pid !== undefined) {
    const result = spawnSync("taskkill", ["/pid", String(child.pid), "/T", "/F"], { windowsHide: true });
    if (!result.error && result.status === 0) {
      return;
    }
  }
  child.kill();
}

export function clientFor(serverUrl: string, auth?: BasicAuth | undefined): OpenCodeClient {
  return OpenCode.make({
    baseUrl: serverUrl.replace(/\/$/, ""),
    ...(auth === undefined ? {} : { headers: { authorization: basicAuthHeader(auth) } }),
  });
}

export function subscribeEvents(
  client: OpenCodeClient,
  handler: (event: V2Event) => void,
  onConnectionChange?: (connected: boolean) => void,
): EventSubscription {
  let closed = false;
  const run = async (): Promise<void> => {
    while (!closed) {
      try {
        onConnectionChange?.(true);
        for await (const event of client.event.subscribe()) {
          if (closed) {
            break;
          }
          handler(event);
        }
        onConnectionChange?.(false);
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

export async function respondPermission(
  client: OpenCodeClient,
  sessionId: string,
  requestId: string,
  decision: "once" | "always" | "reject",
): Promise<void> {
  await client.permission.reply({ sessionID: sessionId, requestID: requestId, decision });
}
