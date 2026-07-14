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

## Completion report

1. **Root cause:** Plane mismatch (`local` assignments vs `worker-local` timer).
2. **Management.Host DB:** Aspire `DefaultConnection` → catalog `Apperture` @ host port 14333.
3. **WorkerHost DB:** Same under Aspire.
4. **Ownership:** Timer dispatch + execution = WorkerHost; manual run-cycle = Management; plane `local`, claim host `worker-local`.
5. **Fix:** Decouple plane from `APERTURE_RUNTIME_ID` (see above).
6. **Manual validation:** Deferred — restart AppHost and follow Epic SWM-1 verify steps.
7. **Tests:** `ScheduleDispatchPlaneIdentityTests` + existing suite green.
8. **Docs:** diagnosis, Epic SWM-1, Epic106, deploy/README, audit header.
9. **Build/test:** Build OK; 1118 unit tests passed.
10. **Next epic:** Scheduler health/diagnostics surface; assignment-plane UI; multi-host plane management; non-Aspire connection alignment; SchedulerHost stub retirement.
