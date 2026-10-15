import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import { loadRunState } from "./state.ts";
import type { ResolvedPlan, RunState, WorkerResult } from "./types.ts";

export type DashboardTask = {
  id: string;
  title: string;
  status: string;
  attempts: number;
  branch: string;
  headCommit: string | null;
  error: string | null;
  gateOk: boolean | null;
  resultStatus: string | null;
  logTail: string[];
};

export type DashboardData = {
  wave: string;
  updatedAt: string;
  serverUrl: string | null;
  counts: Record<string, number>;
  aggregate: { merged: string[]; conflicts: string[]; skipped: string[] } | null;
  tasks: DashboardTask[];
  error?: string;
};

const LOG_TAIL_LINES = 25;

function readLogTail(runDir: string, taskId: string): string[] {
  try {
    const text = fs.readFileSync(path.join(runDir, "logs", `${taskId}.log`), "utf8");
    const lines = text.split("\n").filter((line) => line.length > 0);
    return lines.slice(-LOG_TAIL_LINES);
  } catch {
    return [];
  }
}

function readResultStatus(runDir: string, taskId: string): string | null {
  try {
    const text = fs.readFileSync(path.join(runDir, "results", `${taskId}.result.json`), "utf8");
    const parsed = JSON.parse(text) as Partial<WorkerResult>;
    return typeof parsed.status === "string" ? parsed.status : null;
  } catch {
    return null;
  }
}

export function collectDashboard(runDir: string, plan: ResolvedPlan, state: RunState): DashboardData {
  const byId = new Map(state.tasks.map((task) => [task.id, task]));
  const counts: Record<string, number> = {};
  const tasks: DashboardTask[] = plan.tasks.map((task) => {
    const live = byId.get(task.id);
    const status = live?.status ?? "pending";
    counts[status] = (counts[status] ?? 0) + 1;
    return {
      id: task.id,
      title: task.title,
      status,
      attempts: live?.attempts ?? 0,
      branch: live?.branch ?? task.branch,
      headCommit: live?.headCommit?.slice(0, 8) ?? null,
      error: live?.error ?? null,
      gateOk: live?.gate === undefined ? null : live.gate.ok,
      resultStatus: readResultStatus(runDir, task.id),
      logTail: readLogTail(runDir, task.id),
    };
  });
  return {
    wave: state.wave,
    updatedAt: state.updatedAt,
    serverUrl: state.serverUrl ?? null,
    counts,
    aggregate: state.aggregate
      ? {
          merged: state.aggregate.merged,
          conflicts: state.aggregate.conflicts,
          skipped: state.aggregate.skipped,
        }
      : null,
    tasks,
  };
}

export function dashboardHtml(): string {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>wave dashboard</title>
<style>
body { font-family: ui-monospace, Consolas, monospace; background: #111; color: #ddd; margin: 1em; }
h1 { font-size: 1.2em; }
table { border-collapse: collapse; width: 100%; }
th, td { border: 1px solid #444; padding: 4px 8px; text-align: left; vertical-align: top; }
th { background: #222; }
.running { color: #7fd4ff; } .done, .merged { color: #7fff9f; }
.failed, .conflict { color: #ff7f7f; } .blocked { color: #ffb27f; }
.pending, .verifying { color: #bbb; }
pre { background: #1a1a1a; padding: 8px; overflow-x: auto; max-height: 220px; overflow-y: auto; font-size: 0.85em; }
.err { color: #ff9f9f; }
#meta { color: #999; margin-bottom: 1em; }
</style>
</head>
<body>
<h1 id="title">wave dashboard</h1>
<div id="meta">loading…</div>
<table id="tasks"><thead><tr><th>task</th><th>status</th><th>att</th><th>branch/head</th><th>result</th><th>error</th></tr></thead><tbody></tbody></table>
<h2>logs (tail)</h2>
<div id="logs"></div>
<script>
const esc = (s) => String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
async function refresh() {
  try {
    const res = await fetch("/api/state");
    const d = await res.json();
    if (d.error) { document.getElementById("meta").textContent = d.error; return; }
    document.getElementById("title").textContent = "wave " + d.wave;
    const counts = Object.entries(d.counts).map(([k, v]) => k + "=" + v).join(" ");
    document.getElementById("meta").textContent =
      "updated " + d.updatedAt + "  " + counts + (d.serverUrl ? "  server " + d.serverUrl : "");
    document.querySelector("#tasks tbody").innerHTML = d.tasks.map((t) =>
      "<tr><td><b>" + esc(t.id) + "</b><br/>" + esc(t.title) + "</td>" +
      "<td class=\"" + esc(t.status) + "\">" + esc(t.status) +
      (t.gateOk === false ? " gate:FAILED" : "") + "</td>" +
      "<td>" + t.attempts + "</td>" +
      "<td>" + esc(t.branch) + "<br/>" + esc(t.headCommit ?? "-") + "</td>" +
      "<td>" + esc(t.resultStatus ?? "-") + "</td>" +
      "<td class=\"err\">" + esc(t.error ?? "") + "</td></tr>"
    ).join("");
    document.getElementById("logs").innerHTML = d.tasks.map((t) =>
      "<h3>" + esc(t.id) + "</h3><pre>" + esc(t.logTail.join("\\n")) + "</pre>"
    ).join("");
  } catch (e) {
    document.getElementById("meta").textContent = "fetch failed: " + e;
  }
}
refresh();
setInterval(refresh, 3000);
</script>
</body>
</html>`;
}

export function serveDashboard(options: {
  runDir: string;
  plan: ResolvedPlan;
  statePath: string;
  port: number;
  log?: ((line: string) => void) | undefined;
}): { url: string; close(): void } {
  const log = options.log ?? (() => {});
  const server = http.createServer((req, res) => {
    void (async () => {
      try {
        if (req.url === "/api/state") {
          const state = await loadRunState(options.statePath);
          const data: DashboardData =
            state === null
              ? {
                  wave: options.plan.wave,
                  updatedAt: "",
                  serverUrl: null,
                  counts: {},
                  aggregate: null,
                  tasks: [],
                  error: `no run state yet at ${options.statePath}`,
                }
              : collectDashboard(options.runDir, options.plan, state);
          res.writeHead(200, { "content-type": "application/json" });
          res.end(JSON.stringify(data));
          return;
        }
        res.writeHead(200, { "content-type": "text/html; charset=utf-8" });
        res.end(dashboardHtml());
      } catch (error) {
        res.writeHead(500, { "content-type": "text/plain" });
        res.end(`dashboard error: ${String(error)}`);
      }
    })();
  });
  server.listen(options.port, "127.0.0.1", () => {
    log(`[wave] dashboard at http://127.0.0.1:${options.port}/`);
  });
  return {
    url: `http://127.0.0.1:${options.port}/`,
    close() {
      server.close();
    },
  };
}
