# Epic SWM-2 — Scheduled Dispatch E2E Correctness

**Status:** Delivered  
**Track:** Scheduling & Worker Management  
**Depends on:** [Epic SWM-1](./Epic-SWM1-Runtime-Plane-Identity-Alignment.md) (plane/host identity)

## Context

SWM-1 fixed Aspire plane identity (`ScheduleDispatch:RuntimePlaneId=local`, `APERTURE_RUNTIME_ID=worker-local`). Live validation then hit a due-evaluation failure before claim/dispatch:

```
InvalidOperationException: The LINQ expression
  DbSet<ScheduleRunEntity>().Any(s => ... && s.FireTimeUtc.UtcDateTime == ...)
could not be translated
```

## Root cause

| Item | Value |
|------|--------|
| Column / entity | `ScheduleRuns.FireTimeUtc` — `datetimeoffset` / `DateTimeOffset` |
| Broken query | `RunExistsAsync` (also `CompleteRunAsync`) compared via `.UtcDateTime` **inside** the EF expression |
| Why | EF Core SQL Server cannot translate `DateTimeOffset.UtcDateTime` member access |

## Decision

Normalize to UTC `DateTimeOffset` **outside** the query (`new DateTimeOffset(fire.UtcDateTime, TimeSpan.Zero)`), then compare `FireTimeUtc == instant`. No schema migration. Unique index `(ScheduleId, FireTimeUtc)` and claim idempotency unchanged. Same normalization applied on claim insert and stale recover.

## Local validation

1. Start AppHost; WorkerHost logs `Plane=local HostId=worker-local`.
2. Enabled HelloWorld schedule with cron `* * * * *` on plane `local`.
3. Within one minute + poll interval: cycle shows due/candidates > 0; `ScheduleRuns` gains a row claimed by `worker-local`; run history shows the workflow — **without** manual run-cycle.
4. WorkerHost logs must not show EF translation failures on `FireTimeUtc.UtcDateTime`.

### Validated 2026-07-14

- Schedule `SWM1-Validate-HelloWorld` (`e3a76ff4dac24679b192305b`) fired automatically.
- Example run: `RunId=e7f02827639a46bbbabbbc47`, `ClaimedByHostId=worker-local`, `Outcome=Dispatched`, fire `2026-07-14T16:28:00Z`.
- Cycle logs showed `Due=1 Candidates=1` then completed with no EF translation errors.

## SWM-3 backlog (suggested)

- Scheduler / worker health diagnostics in Management UI
- Operator visibility for active plane, last evaluation, assignment plane
- Aspire console log capture / status card
- Non-Aspire connection-string footguns
- SchedulerHost stub retirement
