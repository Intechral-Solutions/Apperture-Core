# Apperture Scheduling Infrastructure & Runtime Audit

- **Date:** 2026-07-13
- **Type:** Read-only architecture & runtime investigation (planning pass, not implementation)
- **Dev mode at time of audit:** Aspire (`scripts/.runtime/dev/mode.json` → `"mode": "aspire"`)
- **Method:** Repository-wide search, dependency inspection, configuration tracing, migration review, composition-root tracing. Runtime smoke test (§H) and test-suite execution (§12) are **not** run in this pass and are flagged as required follow-up.

> Evidence convention: file paths and line numbers refer to the state of the repository at audit time. Conclusions are marked **Confirmed** (pure code/config trace), **Strongly-supported inference**, or **Unverified** (needs runtime).

---

## A. Executive summary

**Is scheduling operational?** The scheduling *machinery* is implemented and structurally sound, but **automatic (timer-driven) scheduled execution is broken in the Aspire development environment** — the environment this repository is currently running. Manual/triggered execution works; the background timer path does not.

**Most likely failure point (Confirmed statically; High confidence):** a **runtime-plane identity mismatch**.

- Schedules are assigned to plane **`"local"`** (controller default).
- The WorkerHost that runs the dispatch timer polls plane **`"worker-local"`** because Aspire injects `APERTURE_RUNTIME_ID=worker-local`.
- The evaluator filters assignments by plane, finds **zero** rows, and never creates runs.

Evidence:

- Assignment default: `IntegrationSchedulesController.cs:105` (`RuntimePlaneId ?? "local"`), `SchedulesController.cs:111` (same).
- Worker plane: `src/Apperture.AppHost/AppHost.cs:30` `.WithEnvironment("APERTURE_RUNTIME_ID", "worker-local")` → `WorkerHost/Program.cs:88` `opts.RuntimePlaneId ??= APERTURE_RUNTIME_ID` → `ScheduleDispatchBackgroundService.cs:30` `new RuntimePlaneId(_options.Value.RuntimePlaneId ?? "local")`.
- Filter: `CronScheduleEvaluator.GetDueAsync` → `_repo.GetAssignmentsAsync(runtimePlaneId)` → `SqlScheduleRepository.cs:70-72` `Where(x => x.RuntimePlaneId == planeId.Value)`.

**Why manual works but scheduled doesn't:** the manual "run cycle" (`POST /api/control/recovery/schedules/run-cycle`, `RecoveryController.cs:181-191`) runs `IScheduleDispatchRunner.RunCycleAsync` **inside the Management API process** with plane default `"local"` — matching the assignment plane — so it dispatches. The background timer runs only in WorkerHost on plane `"worker-local"`.

**Current architecture:** custom SQL-claim scheduler (Cronos for cron parsing only). Control plane owns definitions/assignments; WorkerHost owns timer-driven execution. Matches intended model — but disconnected by the plane defect.

**Current host ownership:** timer runs in **WorkerHost only** (`WorkerHost/Program.cs:150`). The standalone `SchedulerHost` project is a legacy no-op stub, not orchestrated by AppHost.

**Severity:** High. **Confidence:** High that the mismatch prevents timer dispatch in Aspire; Medium-High that it is *the* user-observed cause (final confirmation = two DB reads in §H).

**Data loss / duplicate execution:** No duplicate-execution risk from this bug (nothing runs). Duplicate protection is otherwise sound (unique index `IX_ScheduleRuns_ScheduleId_FireTimeUtc` + insert-based claim). No data loss; missed occurrences are simply never created, and first-run lookback is bounded to 5 minutes so re-enabling will not burst.

---

## B. Current-state architecture diagram (as implemented)

```mermaid
flowchart TB
  subgraph MUI[Management UI :9001 - Aspire: management-ui]
    RunsPage[Schedules / Runs pages]
    RunCycleBtn[Manual run-cycle action]
  end

  subgraph MAPI[Management API :9000 - Aspire: management-api]
    SchedCtl[SchedulesController / IntegrationSchedulesController<br/>assign plane = 'local']
    RecCtl[RecoveryController.RunScheduleCycle<br/>plane = 'local']
    RunnerA[IScheduleDispatchRunner registered here]
    Obs[AttentionIncidentObservationHostedService<br/>expected-run monitoring, plane-independent]
    Mig[Migrations on startup + /health]
  end

  subgraph WH[WorkerHost - Aspire: worker, APERTURE_RUNTIME_ID=worker-local]
    BG[ScheduleDispatchBackgroundService<br/>poll every >=15s, plane = 'worker-local']
    RunnerW[ScheduleDispatchRunner]
    SRW[StartRequestWorker executes enqueued starts]
  end

  subgraph DB[(SQL 'Apperture' - shared under Aspire via DefaultConnection)]
    SD[ScheduleDefinitions]
    SA["ScheduleAssignments (RuntimePlaneId)"]
    SR["ScheduleRuns (unique ScheduleId+FireTimeUtc = claim)"]
    SS[ScheduleStates]
    WSR[Workflow/Operation StartRequests]
    PE[PlatformEvents]
  end

  SchedCtl -->|write plane=local| SA
  SchedCtl --> SD
  RecCtl --> RunnerA -->|plane=local matches| SA
  RunnerA -->|claim + enqueue| SR & WSR

  BG --> RunnerW -->|GetAssignments plane=worker-local| SA
  RunnerW -. "returns 0 rows -> no runs" .-> SR
  SRW --> WSR

  Obs -->|reads defs + runs| SD & SR
  Obs --> PE

  RunCycleBtn --> RecCtl

  classDef broken fill:#fdd,stroke:#c00;
  class BG,RunnerW broken;
```

The red path (WorkerHost timer) is the intended production execution path and is the one that returns zero candidates.

---

## C. Component inventory

| Component | Project / path | Responsibility | Host | Persistence | Runtime status |
|---|---|---|---|---|---|
| `ScheduleDispatchBackgroundService` | RuntimeHost.Windows.WorkerHost | Timer loop (>=15s) driving dispatch | **WorkerHost only** | - | Active but **effectively no-op in Aspire** (plane mismatch) |
| `ScheduleDispatchRunner : IScheduleDispatchRunner` | RuntimePlane.Dispatch.SqlServer | One dispatch cycle: due+stale -> claim -> dispatch | WorkerHost + Management API | ScheduleRuns/States | Active |
| `CronScheduleEvaluator : IScheduleEvaluator` | Persistence.SqlServer | Cronos-based due detection | Both | reads defs/assignments/state | Active |
| `SqlScheduleRepository : IScheduleRepository` | Persistence.SqlServer | CRUD, claim, stale recovery, state | Both | all schedule tables | Active |
| `SqlScheduleManagementService : IScheduleManagementService` | ControlPlane.Security | Definition/assignment mgmt, execution-state projection | Management API | schedule tables | Active |
| `SchedulerLifecycleGuard : ISchedulerLifecycleGuard` | RuntimePlane.Dispatch.SqlServer | Block dispatch for missing/disabled defs | Both | - | Active (existence-only, see I-M2) |
| `SchedulesController` / `IntegrationSchedulesController` | Management.Api | Definition CRUD + assignment | Management API | - | Active; **assigns plane `"local"`** |
| `RecoveryController.RunScheduleCycle` | Management.Api | Manual run-cycle | Management API | - | Active (works on plane `"local"`) |
| `ScheduleExpectationEvaluator` + `AttentionIncidentObservationHostedService` | Core.Application / ControlPlane.Security | expected-run-missing detection | Management API | reads defs/runs, writes PlatformEvents/incidents | Active, **plane-independent** |
| `SchedulerHostStub` | RuntimeHost.Windows.SchedulerHost | Standalone scheduler host | (own exe) | - | **Legacy no-op stub, not in AppHost** |
| `IScheduleTriggerBackend` | RuntimeHost.Windows.SchedulerHost | Abstraction | - | - | **Unused / orphaned** |
| `IWorkflowScheduler` | RuntimePlane.Abstractions | Abstraction | - | - | Appears vestigial (verify) |

**Duplicate/competing paths:** two invokers of the same runner — (1) WorkerHost timer (plane `worker-local`), (2) Management API manual endpoint (plane `local`). They target **different planes**, which is the defect, not intentional redundancy. The `SchedulerHost` project is a legacy standalone-scheduler stub superseded by the WorkerHost implementation (its own XML doc states this).

---

## D. Library inventory

| Package | Version | Referencing projects | Responsibility | Runtime usage | Risks / observations |
|---|---|---|---|---|---|
| **Cronos** | 0.13.0 (`Directory.Packages.props:60`) | Persistence.SqlServer, Core.Application, ControlPlane.Security | Cron parse + TZ-aware occurrence enumeration | **Yes** (`CronScheduleEvaluator.cs:59,69`; `ScheduleExpectedFireCollector`) | Sound; wrapped behind `IScheduleEvaluator`. Cronos requires `fromUtc < toUtc` (handled `CronScheduleEvaluator.cs:71-74`). |
| Microsoft.Extensions.Hosting | managed | WorkerHost, SchedulerHost, Management.Host | `BackgroundService` / generic host | Yes | Standard. |
| `TimeProvider` (BCL) | net10.0 | ControlPlane.Security mgmt svc, tests | Time abstraction | Partial | Runner/evaluator use `DateTimeOffset.UtcNow` directly (see G/I-M1). |

**No Quartz / Hangfire / Coravel / NCrontab.** Scheduling is a **custom SQL-claim scheduler**; only cron parsing is delegated (Cronos). Apperture implements evaluation, claiming/leasing (row-insert + stale recovery), dispatch, run persistence, and state itself — matching the intended "Apperture owns schedules/runs/execution truth" model. No third-party in-memory/single-process scheduler assumptions are introduced.

---

## E. Execution trace (intended timer path)

| # | Transition | Class.method | Notes / state |
|---|---|---|---|
| 1 | Timer tick | `ScheduleDispatchBackgroundService.ExecuteAsync` | plane = `RuntimePlaneId(opts.RuntimePlaneId ?? "local")` -> **`worker-local`** in Aspire |
| 2 | Cycle | `ScheduleDispatchRunner.RunCycleAsync` | stale cutoff = now - ClaimLeaseMinutes(5) |
| 3 | Due detection | `CronScheduleEvaluator.GetDueAsync` | `GetAssignmentsAsync(plane)` -> **0 rows** (mismatch) -> empty **<- execution stops here** |
| 4 | Stale recovery | `SqlScheduleRepository.GetRecoverableStaleCandidatesAsync` | also plane-filtered -> 0 |
| 5 | Claim | `SqlScheduleRepository.TryClaimFireAsync` | insert `ScheduleRuns` (Outcome=InProgress); unique index = claim token |
| 6 | Guard | `SchedulerLifecycleGuard.CanDispatch*` | existence check |
| 7 | Integration gate | `ScheduleDispatchRunner.EnsureIntegrationOperationalAsync` | requires Integration `Active` |
| 8 | Dispatch | `IWorkflowStartDispatchService`/`IOperationStartDispatchService.StartAsync` | idempotency key `schedule:{id}:{fireUtc:O}` |
| 9 | Complete | `CompleteRunAsync` + `UpdateStateAsync` | Outcome=Dispatched, LastSuccessfulDispatchUtc |
| 10 | Execute | `StartRequestWorker` (WorkerHost) -> `DefaultWorkflowExecutor` | actual work |

In Aspire the chain terminates at **step 3** (zero candidates). The manual endpoint enters the same runner at step 2 but with plane `"local"`, so it proceeds through 5-10.

---

## F. Configuration matrix

| Key | Default | Source / precedence | Consumer | Reaches executor? | Missing -> |
|---|---|---|---|---|---|
| `APERTURE_RUNTIME_ID` | unset -> `"local"`; Aspire sets `"worker-local"` | env (AppHost) > config | WorkerHost plane id + `RuntimePlaneId` | Yes | plane `"local"` (which *would* match assignments) |
| `ScheduleDispatch:PollIntervalSeconds` | 30 (min clamp 15) | appsettings (`ScheduleDispatchOptions`) | Background service | Yes | 30s |
| `ScheduleDispatch:ClaimLeaseMinutes` | 5 | appsettings | Runner stale cutoff | Yes | 5 |
| `ScheduleDispatch:RuntimePlaneId` | null -> APERTURE_RUNTIME_ID | appsettings/env | Background service plane | Yes | falls back to env/`local` |
| `ScheduleDispatch:SchedulerHostId` | null -> RuntimeId | appsettings | claim ownership id | Yes | uses RuntimeId/`worker` |
| assignment `RuntimePlaneId` (request body) | `"local"` | API request body default | stored on `ScheduleAssignments` | - | **`"local"`, mismatches Aspire worker** |
| `ConnectionStrings:DefaultConnection` | Aspire-injected | Aspire > file bootstrap > `SqlServer:*` > LocalDB | Both hosts | Yes | see G split-brain |
| `PlatformEvents:Enabled` | true (worker appsettings) | appsettings | platform events | Yes | - |

**Notable:** WorkerHost `appsettings.json` contains **no `ScheduleDispatch` section and no `SqlServer` section** — every scheduler value is defaulted or Aspire/env-supplied. There is **no operator-facing "scheduler enabled" flag**; the timer is always registered (`WorkerHost/Program.cs:150`). The runtime plane is invisible in the operator UI, so the mismatch is undiscoverable from the product.

---

## G. Persistence assessment

Tables (migration `AddSchedules` / conditional `EnsureScheduleTablesConditional`):

- `ScheduleDefinitions` (PK Id)
- `ScheduleAssignments` (PK Id; index `IX_ScheduleAssignments_RuntimePlaneId`)
- `ScheduleRuns` (PK Id identity; **unique** `IX_ScheduleRuns_ScheduleId_FireTimeUtc`; index `AssignmentId`)
- `ScheduleStates` (PK AssignmentId)

Assessment:

- **Claim/dedup integrity: good.** Unique `(ScheduleId, FireTimeUtc)` + insert-based claim + `IsUniqueViolation` handling (`SqlScheduleRepository.cs:340-345`) prevents duplicate runs across hosts.
- **Plane index exists** -> assignment queries are efficient; the bug is the *value*, not query performance.
- **UTC consistency:** runs normalize fire instants to UTC (`ScheduleDispatchRunner.NormalizeFireUtc`; repo compares `.UtcDateTime`). Consistent.
- **Historical defect:** `EnsureScheduleTablesConditional` + `scripts/dev/maintenance/Fix-ScheduleTables.ps1` exist because `AddSchedules` was *sometimes applied with an empty `Up()`*, leaving DBs without schedule tables. On an affected DB the scheduler fails differently (missing tables). Confirm the active DB has the tables.
- **No stale-lease block risk:** stale InProgress runs are recoverable after `ClaimLeaseMinutes` (`GetRecoverableStaleCandidatesAsync`, `TryRecoverStaleClaimAsync`). A crash between claim and dispatch leaves an InProgress row another host recovers.
- **Split-brain risk (non-Aspire):** under Aspire both hosts share `DefaultConnection`. Outside Aspire, WorkerHost resolves via `SqlServer:*`/bootstrap-file/LocalDB (`BootstrapConnectionProfile.BuildConnectionString`) independently of Management API — misconfiguration could point them at different databases.

**Operator SQL (read-only):**

```sql
-- 1. Distinct planes assignments were created under (expect 'local')
SELECT RuntimePlaneId, COUNT(*) FROM dbo.ScheduleAssignments GROUP BY RuntimePlaneId;

-- 2. Enabled schedules and whether they have an assignment on the worker's plane
SELECT d.Id, d.Name, d.Enabled, d.RecurrenceExpression, d.IntegrationId, a.RuntimePlaneId
FROM dbo.ScheduleDefinitions d
LEFT JOIN dbo.ScheduleAssignments a ON a.ScheduleId = d.Id
WHERE d.Enabled = 1;

-- 3. Any runs ever created? (empty => nothing ever dispatched)
SELECT TOP 50 ScheduleId, FireTimeUtc, Outcome, ClaimedByHostId, StartedAtUtc, CompletedAtUtc, RunId
FROM dbo.ScheduleRuns ORDER BY StartedAtUtc DESC;

-- 4. Stale InProgress claims
SELECT * FROM dbo.ScheduleRuns
WHERE Outcome = 'InProgress' AND StartedAtUtc < DATEADD(MINUTE, -5, SYSUTCDATETIME());
```

Confirming outcome for the diagnosis: query 1 returns `local`, query 3 is empty (or only rows whose `ClaimedByHostId` came from a manual cycle), confirming the worker (`worker-local`) never claimed.

---

## H. Runtime test results

**Not executed in this pass.** Per the audit constraints, scheduling must not be declared working from builds/unit tests alone. To reach the completion standard, run in a live environment:

1. **Read runtime facts:** run the §G queries (via SSMS or `docker exec` into the Aspire SQL container) to capture plane values and run history.
2. **Confirm worker plane at runtime:** inspect WorkerHost logs for the effective `RuntimeId`/plane and the `Schedule dispatch cycle` behavior.
3. **Deterministic near-term test:** create an every-minute schedule via the UI (-> plane `local`), wait 2 cycles, observe **no** worker-created `ScheduleRuns` row; then call `POST /api/control/recovery/schedules/run-cycle?runtimePlaneId=local` and observe a row **does** appear — isolating plane as the divergence.
4. **Confirmatory fix test:** create a schedule with `RuntimePlaneId=worker-local` (or set worker `ScheduleDispatch:RuntimePlaneId=local`) and observe timer dispatch succeed.

Static composition facts already confirmed: dev mode = aspire (`mode.json`), worker plane = `worker-local` (AppHost), assignment default = `local` (controllers), evaluator filters by plane (repo).

---

## I. Findings

### CRITICAL

**C1 — Runtime-plane mismatch prevents all timer-driven dispatch in Aspire.**
- Evidence: `AppHost.cs:30`; `WorkerHost/Program.cs:88`; `ScheduleDispatchBackgroundService.cs:30`; `IntegrationSchedulesController.cs:105`; `SchedulesController.cs:111`; `SqlScheduleRepository.cs:70-72`.
- User-visible impact: scheduled jobs never run automatically in dev; operators see enabled schedules that never fire.
- Root cause: assignment plane (`local`) != worker poll plane (`worker-local`).
- Confidence: High (static); runtime confirm via §H.
- Remediation: align identities — either drop `APERTURE_RUNTIME_ID=worker-local` from AppHost (revert to `local`), or make the assignment plane a first-class, operator-visible value defaulted to the actual worker plane; add a startup log + diagnostic exposing the worker's active plane and the set of assignment planes.

### HIGH

**H1 — No scheduler heartbeat / liveness signal; health is misleadingly green.**
- Evidence: only `MigrationsCompleteHealthCheck` and start-request lease processors expose health; `/health` (AppHost `WithHttpHealthCheck`) covers migrations/host, not scheduler evaluation. The background service swallows per-cycle exceptions (`ScheduleDispatchBackgroundService.cs:44-47`) and exposes nothing queryable.
- Impact: a dead/no-op scheduler reports healthy; operators cannot distinguish "not started" / "no assignments on my plane" / "nothing due".
- Confidence: High.

**H2 — Operator UI reads a different plane than the executor, so it cannot reveal the mismatch.**
- Evidence: `SchedulesController.Get` and diagnostics default `runtimePlaneId ?? "local"` (`SchedulesController.cs:60`; `RecoveryController.cs:162,182`; `IRuntimeHealthQueryService` default `"local"`). Execution state is read from plane `local`, which coincidentally matches the assignment, so the UI shows "assigned, never dispatched" without explaining that no worker serves that plane.
- Impact: UI implies configuration is fine; the empty execution state looks like "nothing due yet."
- Confidence: High.

### MEDIUM

**M1 — `TimeProvider` not used on the hot path.**
- Evidence: `ScheduleDispatchBackgroundService.cs:38` and `ScheduleDispatchRunner` use `DateTimeOffset.UtcNow`; only `SqlScheduleManagementService` takes `TimeProvider`. Reduces testability of timer behavior and clock-skew handling.
- Confidence: High.

**M2 — Lifecycle guard is existence-only.**
- Evidence: `SchedulerLifecycleGuard.cs:25-36` returns true if the definition/operation resolves; the XML comment claims "disabled modules return false," relying on the injected resolver being a filtered variant. Verify WorkerHost injects the filtered `IWorkflowDefinitionResolver`/`IOperationRegistryService`.
- Confidence: Medium (DI wiring not fully traced).

**M3 — Non-Aspire split-brain DB possibility.**
- Evidence: `BootstrapConnectionProfile.BuildConnectionString` fallback chain differs per host; only Aspire guarantees shared `DefaultConnection`.
- Confidence: Medium.

### LOW

**L1 — Legacy `SchedulerHost` stub + `IScheduleTriggerBackend` are orphaned.**
- Evidence: `SchedulerHostStub.cs` no-op; not referenced by `AppHost.cs`. Naming/design drift from an abandoned standalone-scheduler design.
- Confidence: High. Do not delete until runtime usage is disproven (none found).

### INFORMATIONAL

**I1 — Historical empty-`AddSchedules` migration.** `EnsureScheduleTablesConditional` + `Fix-ScheduleTables.ps1`. Confirm the active DB has the tables.

**I2 — Expected-run monitoring is NOT circularly dependent on the worker.** `ScheduleExpectationEvaluator` + `AttentionIncidentObservationHostedService` run in Management API and evaluate cron expectation vs `LatestScheduleOriginAuthorizedRun` from definitions directly (not assignments/plane). So it can detect missing scheduled runs even while the worker is dead — the detector does not depend on the thing it monitors. Caveat: notification *delivery* is disabled by default (separate subsystem), so the signal may not reach operators. Do not conflate this with the scheduling defect.

---

## J. Current-state verdict

| Capability | Verdict |
|---|---|
| Definition management | Operational |
| Persistence | Operational (confirm tables in active DB) |
| Assignment | **Present but disconnected** (plane value mismatches executor) |
| Recurrence evaluation | Operational (Cronos) |
| Claiming / leasing | Operational (unique-index claim + stale recovery) |
| Dispatch (timer) | **Present but disconnected** (never reached in Aspire) |
| Dispatch (manual) | Operational |
| Execution | Operational (via StartRequestWorker) |
| Retry | Partially implemented (stale recovery yes; no per-attempt dispatch retry/backoff — terminal `Failed`) |
| Recovery after restart | Operational (stale claim recovery) |
| Observability / heartbeat | **Missing** |
| UI management | Operational for CRUD; **misleading for health/plane** |
| Expected-run monitoring | Operational, plane-independent |
| Notification integration | Present but delivery gated off (out of scope) |

---

## K. Remediation plan (phased)

### 1. Immediate restoration (focused fix)
- **Align plane identity.** Option A (smallest): remove `APERTURE_RUNTIME_ID=worker-local` from `AppHost.cs:30` so the worker polls `local`, matching the assignment default. Option B (more correct): make the assignment plane explicit and equal to the deployed worker's runtime id, and stop defaulting to a literal `"local"` in two controllers.
  - Rationale: restores timer dispatch. Risk: low. Validation: §H tests 3-4. Type: focused fix.
- **Add active-plane + candidate-count startup/cycle logging** in `ScheduleDispatchBackgroundService` (narrow diagnostic, documented). Type: focused fix.

### 2. Reliability & concurrency corrections (stabilization epic)
- Switch runner/background service to `TimeProvider` (M1). Confirm/repair lifecycle-guard filtering (M2). Add explicit non-Aspire connection-resolution assertion or shared-DB check (M3). Consider a dispatch-failure retry policy vs terminal `Failed`.

### 3. Observability & operator diagnostics
- Minimal scheduler-health contract: last evaluation UTC, active plane, due count, unclaimed count, stale-claim count, last successful dispatch — surfaced via a diagnostics endpoint + a health check that is not green when no evaluation has occurred within N intervals. Fixes H1/H2.

### 4. Architecture cleanup
- Retire `SchedulerHost` stub and `IScheduleTriggerBackend` after confirming no runtime usage (L1). Reconcile plane-id defaults so the value is defined once. Keep hosts independently runnable (no Aspire coupling).

### 5. Test coverage
- Host-composition test proving WorkerHost's effective plane equals the plane schedules are assigned to. Real-DB end-to-end test: create schedule -> worker timer -> run row -> dispatch. (Current `ScheduleDispatchTests` all call `RunCycleAsync(new RuntimePlaneId("local"))` directly, **bypassing host composition and plane resolution** — which is why this defect passed CI.)

### 6. Documentation
- Update Epic 28/36 scheduler docs and platform configuration docs to document runtime-plane identity, the assignment-plane requirement, and Aspire's `APERTURE_RUNTIME_ID`.

### 7. Optional future
- Operator-managed plane assignment UI; scheduler metrics/OTEL; multi-worker plane routing.

**Do not** replace the custom scheduler — the architecture is sound and matches intent; it is disconnected by a configuration/identity defect, not structurally broken.

---

## L. Recommended next epic

**Title:** Restore and harden runtime-plane-aware scheduled execution.

**Problem statement:** Timer-driven scheduled execution does not run in the Aspire dev environment because schedules are assigned to plane `local` while the worker polls `worker-local`; the mismatch is invisible in the UI and health, and existing tests bypass host composition so it went undetected.

**Scope:**
- Single source of truth for runtime-plane identity across AppHost, WorkerHost, and schedule assignment.
- Startup + diagnostic visibility of the active plane and per-cycle candidate counts.
- Host-composition and end-to-end tests that exercise the real plane-resolution path.

**Explicit non-goals:** no scheduler engine replacement; no Aspire in production; no change to claim/lease semantics; no notification redesign; no deletion of the legacy stub until proven unused; no schedule-semantics changes.

**Implementation sequence:**
1. Align plane identity (fix AppHost/worker default or make assignment plane explicit and matched).
2. Add active-plane startup log + candidate-count logging.
3. Add scheduler heartbeat/diagnostic surface + non-green health when no evaluation.
4. Add host-composition + real-DB e2e tests.
5. Docs; then retire legacy `SchedulerHost` after confirming non-use.

**Acceptance criteria:**
- A newly created enabled schedule produces a `ScheduleRuns` row via the WorkerHost timer (no manual trigger) within one poll interval in Aspire.
- Operator UI/diagnostics show the worker's active plane and last evaluation time; health is not green when the scheduler has not evaluated within N intervals.
- Tests fail if the assignment plane and worker plane diverge.

**Test plan:** unit (plane resolution); host-composition (worker effective plane == assignment plane); persistence/e2e (real DB dispatch); concurrency (two workers, single claim); failure (integration inactive -> BlockedByLifecycle).

**Manual verification checklist:** §H steps 1-4; confirm §G queries show runs created by the worker host id (not only manual cycles).

**Rollback considerations:** the immediate fix is a config/identity change with no schema impact; revert by restoring the prior env value. No destructive DB changes. Duplicate-run protection (unique index) remains intact throughout.

---

## Required-findings quick reference

1. **What process evaluates schedules today?** `ScheduleDispatchBackgroundService` in WorkerHost, driving `ScheduleDispatchRunner`.
2. **Does it start in normal dev?** Yes (registered unconditionally), but it evaluates the wrong plane in Aspire.
3. **Does it start outside Aspire?** Yes; outside Aspire its plane defaults to `local` (which *matches* assignments).
4. **Which host?** WorkerHost. Management API only hosts the manual run-cycle + the runner registration.
5. **Which package parses cron?** Cronos 0.13.0.
6. **Where is next occurrence calculated?** `CronScheduleEvaluator.GetDueAsync` (+ `ScheduleExpectedFireCollector` for monitoring).
7. **Where are definitions stored?** `ScheduleDefinitions`.
8. **Where are assignments stored?** `ScheduleAssignments` (keyed by `RuntimePlaneId`).
9. **How does an executor discover work?** `GetAssignmentsAsync(planeId)` filtered by its runtime plane.
10. **How is an occurrence claimed?** Insert into `ScheduleRuns` guarded by unique `(ScheduleId, FireTimeUtc)`.
11. **What prevents duplicate runs?** The unique index + `RunExistsAsync` pre-check + unique-violation handling.
12. **How is instance identity determined?** `ISchedulerHostIdentity` from `ScheduleDispatch:SchedulerHostId ?? RuntimeId ?? "worker"`.
13. **After a restart?** Stale InProgress claims recovered after `ClaimLeaseMinutes`.
14. **Missed occurrences?** Not created while broken; first-run lookback bounded to 5 min (no burst on recovery).
15. **Can manual operations run through the same path?** Yes — manual run-cycle uses the same runner (on plane `local`) and works.
16. **Valid enabled schedules in the active DB?** Unverified — run §G query 2.
17. **Any due-but-unexecuted?** Unverified — run §G queries 2/3.
18. **What is blocking execution?** Plane mismatch (assignment `local` vs worker `worker-local`) — Confirmed statically.
19. **Heartbeat / health?** None specific to the scheduler (misleading green).
20. **Most likely explanation for the observed failure?** The runtime-plane mismatch (finding C1).
