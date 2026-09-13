# SWM-1 — Runtime-plane diagnosis (Aspire)

**Date:** 2026-07-14  
**Status:** Confirmed (static). Live SQL on `localhost:14333` was unreachable at diagnosis time (Aspire not running / login timeout).

## Effective databases / connection config

| Host | Authoritative SQL source under Aspire | Catalog | Host port |
|------|----------------------------------------|---------|-----------|
| Management.Host | `ConnectionStrings:DefaultConnection` via AppHost `WithReference(db, connectionName: "DefaultConnection")` | `Apperture` | **14333** (default `Aspire:SqlServer:HostPort`) |
| WorkerHost | Same `DefaultConnection` injection | `Apperture` | **14333** |

Both hosts resolve SQL through `IBootstrapConnectionProfile` / `BootstrapConnectionProfile`, which **prefers `ConnectionStrings:DefaultConnection` in Development**. Connection-string split-brain is **not** the Aspire failure mode.

Non-Aspire footgun (out of SWM-1 fix scope): Management may fall back to `SqlServer:Server=localhost,1433`; Worker without overlay may fall through to LocalDB.

## Ownership model

| Concern | Owner |
|---------|--------|
| Timer schedule dispatch | **WorkerHost** — `ScheduleDispatchBackgroundService` → `IScheduleDispatchRunner` |
| Manual schedule dispatch | **Management.Host** — `POST .../recovery/schedules/run-cycle` (plane default `local`) |
| Workflow/operation execution | **WorkerHost** — `StartRequestWorker` |
| Standalone `SchedulerHost` | Legacy stub; not in AppHost |

## Flags / identity

| Setting | Effective value (Aspire) | Role |
|---------|--------------------------|------|
| `APERTURE_RUNTIME_ID` | `worker-local` (AppHost + launchSettings) | Runtime / claim host identity |
| `ScheduleDispatch:RuntimePlaneId` (pre-fix) | Inherited from `APERTURE_RUNTIME_ID` → **`worker-local`** | Which assignments the timer evaluates |
| Assignment / UI defaults | **`local`** | Written by Schedules controllers |
| `ScheduleDispatch:Enabled` | *(none — loop always registered)* | N/A |
| `PlatformEvents:Enabled` | `true` (worker appsettings) | Unrelated to dispatch gate |

## Observed stage where execution stops

1. Operator creates schedule → assignment `RuntimePlaneId = local`.
2. Worker timer starts, builds plane `worker-local`.
3. `SqlScheduleRepository.GetAssignmentsAsync("worker-local")` returns **0 rows**.
4. No `ScheduleRuns` claims; no start requests; run history stays empty for timer path.
5. Manual run-cycle on Management with plane `local` **does** dispatch (masks the bug).

## Schedule visibility

Under Aspire, management and worker share one catalog. Schedules created in the UI **are** visible to the worker in SQL; the worker simply **filters them out** by plane.

## Logs during a scheduled-job test

Not captured live (SQL timeout / stack not up). Expected pre-fix WorkerHost behavior: dispatch loop runs without error every ≥15s with zero candidates. Expected post-fix: startup log shows `Plane=local`, `HostId=worker-local`, then cycles with non-zero due counts when an enabled schedule is due.

## Root cause

**Runtime-plane identity mismatch:** assignment plane `local` ≠ worker timer plane `worker-local` (incorrectly coupled to `APERTURE_RUNTIME_ID` in WorkerHost PostConfigure).

## Fix implemented (SWM-1)

- `ScheduleDispatchOptionsDefaults`: plane defaults to `local`; never inherits `APERTURE_RUNTIME_ID`.
- WorkerHost PostConfigure uses that helper; AppHost + launchSettings set `ScheduleDispatch__RuntimePlaneId=local` while keeping `APERTURE_RUNTIME_ID=worker-local`.
- `ScheduleDispatchBackgroundService` logs plane, host id, assignment/due/stale/candidate counts each cycle.

## Validation results (2026-07-14)

| Check | Result |
|-------|--------|
| Live SQL confirm (`localhost:14333`) | Not available (timeout; AppHost/SQL not reachable during this session) |
| Manual HelloWorld timer path | **Deferred** — requires Aspire restart with new WorkerHost binaries; operator checklist in Epic SWM-1 decision note |
| `ScheduleDispatchPlaneIdentityTests` | Passed (4) |
| Related scheduling/bootstrap/WorkerHost DI tests | Passed (27) |
| Full `Apperture.slnx` build | Succeeded |
| Full unit tests | Passed **1118** / 0 failed |
| Integration tests | Skipped (test SQL / Aspire SQL unavailable) |

## Live validation (2026-07-14, post-restart)

**Stack:** AppHost restarted; Management `/health` = Healthy; WorkerHost running; SQL reachable via DCP proxy `127.0.0.1:14333` (same catalog as container map `52712`).

**Aspire WorkerHost env (read from live process):**
- `APERTURE_RUNTIME_ID=worker-local`
- `ScheduleDispatch__RuntimePlaneId=local`
- `ConnectionStrings__DefaultConnection` → `127.0.0.1,14333` / `Apperture`

**Startup / cycle log (diagnostic worker with identical env; Aspire DCP `_out` empty / OTLP-only):**
```
Schedule dispatch loop starting. Plane=local HostId=worker-local PollIntervalSeconds=15
Schedule dispatch cycle begin. Plane=local HostId=worker-local Assignments=1 Due=0 Stale=0 Candidates=0
```

**Schedule:** `SWM1-Validate-HelloWorld` (`e3a76ff4dac24679b192305b`), cron `* * * * *`, plane `local`, HelloWorld workflow, integration Active.

**Automatic fire:** **No** — once a due minute was reached, cycles failed in `SqlScheduleRepository.RunExistsAsync` (`FireTimeUtc.UtcDateTime` not EF-translatable against SQL Server). Zero `ScheduleRuns` rows. This is **outside** SWM-1 plane-identity scope.

**SWM-1 close:** Yes for plane/host identity alignment. Not for full timer E2E until `RunExistsAsync` EF translation is fixed (next scheduling epic).

## SWM-2 follow-up (due-evaluation SQL translation)

**Status:** Fixed in code (`SqlScheduleRepository` normalizes `DateTimeOffset` outside EF queries). See [Epic SWM-2](../02-decisions/Epic-SWM2-Scheduled-Dispatch-E2E-Correctness.md).

| Item | Detail |
|------|--------|
| Root cause | `RunExistsAsync` / `CompleteRunAsync` used `FireTimeUtc.UtcDateTime` inside LINQ → SQL Server cannot translate |
| Entity/DB type | `DateTimeOffset` / `datetimeoffset` |
| Fix | Compare normalized `DateTimeOffset` (`offset=0`) to column; normalize on claim insert |
| Tests | `ScheduleRunExistsQueryTests` (unit); `ScheduleRunExistsSqlServerTests` (SQL when available) |
| Live E2E (2026-07-14) | Automatic fire OK — e.g. `RunId=e7f02827639a46bbbabbbc47`, host `worker-local`, outcome `Dispatched` |
