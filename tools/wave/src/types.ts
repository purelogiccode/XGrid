export type ModelRef = {
  providerID: string;
  modelID: string;
};

export type TaskDefaults = {
  agent?: string;
  model?: ModelRef;
  budgetMs?: number;
  retries?: number;
  autoApprove?: boolean;
  gate?: string;
};

export type WaveTask = {
  id: string;
  title: string;
  prompt?: string;
  promptFile?: string;
  acceptance: string;
  dependsOn?: string[];
  serial?: boolean;
  branch?: string;
  merge?: boolean;
  agent?: string;
  model?: ModelRef;
  budgetMs?: number;
  retries?: number;
  autoApprove?: boolean;
  gate?: string;
};

export type ResolvedTask = {
  id: string;
  title: string;
  prompt: string;
  acceptance: string;
  dependsOn: string[];
  serial: boolean;
  branch: string;
  merge: boolean;
  agent?: string;
  model?: ModelRef;
  budgetMs: number;
  retries: number;
  autoApprove: boolean;
  gate?: string;
};

export type WavePlan = {
  wave: string;
  baseBranch?: string;
  maxParallel?: number;
  worktreeRoot?: string;
  dataLink?: "copy" | "junction" | "none";
  serverPort?: number;
  autoAggregate?: boolean;
  autoApprove?: boolean;
  finalGate?: string[];
  defaults?: TaskDefaults;
  tasks: WaveTask[];
};

export type ResolvedPlan = {
  wave: string;
  baseBranch: string;
  maxParallel: number;
  worktreeRoot: string;
  dataLink: "copy" | "junction" | "none";
  serverPort: number;
  autoAggregate: boolean;
  autoApprove: boolean;
  finalGate: string[];
  defaults: Required<Omit<TaskDefaults, "agent" | "model" | "gate">> &
    Pick<TaskDefaults, "agent" | "model" | "gate">;
  tasks: ResolvedTask[];
};

export type TaskStatus =
  | "pending"
  | "running"
  | "verifying"
  | "done"
  | "blocked"
  | "failed"
  | "conflict"
  | "merged";

export type GateResult = {
  command: string;
  code: number;
  ok: boolean;
  outputTail: string;
};

export type TestRun = {
  command: string;
  result: "pass" | "fail";
  summary: string;
};

export type WorkerResult = {
  taskId: string;
  status: "done" | "blocked" | "failed";
  branch: string;
  headCommit: string;
  filesChanged?: string[];
  testsRun?: TestRun[];
  acceptance?: string;
  notes?: string;
  blockers?: string;
};

export type TaskState = {
  id: string;
  status: TaskStatus;
  attempts: number;
  branch: string;
  worktree?: string;
  sessionId?: string;
  startedAt?: string;
  endedAt?: string;
  headCommit?: string;
  resultFile?: string;
  gate?: GateResult;
  note?: string;
  error?: string;
};

export type AggregateState = {
  startedAt?: string;
  endedAt?: string;
  merged: string[];
  conflicts: string[];
  skipped: string[];
  gateResults: GateResult[];
};

export type RunState = {
  wave: string;
  planPath: string;
  planHash: string;
  baseBranch: string;
  worktreeRoot: string;
  startedAt: string;
  updatedAt: string;
  serverUrl?: string;
  tasks: TaskState[];
  aggregate?: AggregateState;
};
