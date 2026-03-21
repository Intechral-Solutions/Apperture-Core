# Epic 28 — Real Scheduler Implementation

## Status

Implemented

## Context

Epic 27 Hard Follow-Up closed lifecycle enforcement for persisted worker paths and workflow retry, but left `SchedulerDispatch` as a named not-yet-implemented execution surface. Epic 28 closes that gap by implementing a real scheduler execution path.

## Decision

### 1. Scheduler location

The scheduler runs in **WorkerHost** as a `BackgroundService` (`ScheduleDispatchBackgroundService`), alongside `StartRequestWorker`. This aligns with ADR-0004 instance-owned scheduling: the instance (WorkerHost) evaluates and dispatches.

### 2. Durable schedule model

- **ScheduleDefinition**: Id, Name, RecurrenceExpression (cron), TimeZone, Enabled, Target (ScheduledTarget), Version
- **ScheduleAssignment**: Binds schedule to RuntimePlaneId
- **ScheduleRun**: Per-firing record (ScheduleId, FireTimeUtc, Outcome, OutcomeReason, RunId)
- **ScheduleState**: Per-assignment state (NextDueUtc, LastAttemptedUtc, LastOutcome, etc.)

### 3. Supported targets (V1)

- **WorkflowExecution** only. `JobConfiguration` = `{"workflowDefinitionId":"..."}`.
- Operation scheduling not implemented; surface as limitation if needed.

### 4. Cron evaluation

- **Cronos** for parsing `RecurrenceExpression`.
- **CronScheduleEvaluator** implements `IScheduleEvaluator` and returns due firings for a runtime plane.

### 5. Lifecycle gating

Before dispatching, the scheduler calls `ISchedulerLifecycleGuard.CanDispatchWorkflowAsync`. If false:
- No new work is created
- ScheduleRun outcome = `BlockedByLifecycle`
- Audit action `scheduler.schedule.blocked_by_lifecycle`

### 6. Outcome semantics

| Outcome | Meaning |
|---------|---------|
| Dispatched | Workflow start request created |
| BlockedByLifecycle | Module disabled or definition not executable |
| Failed | Invalid target, dispatch error, or resolution failure |

### 7. Poll interval and configuration

- Poll interval: configurable via `ScheduleDispatch:PollIntervalSeconds` (default 30)
- RuntimePlaneId: `ScheduleDispatch:RuntimePlaneId` or `APERTURE_RUNTIME_ID` or `"local"`

### 8. Single-host assumption

V1 assumes a single scheduler host. Idempotency key `ScheduleId:FireTimeUtc` and unique index prevent duplicate dispatch. Multi-host claiming not implemented.

## Consequences

- Scheduler dispatch is a real, test-proven execution path
- `SchedulerDispatch` removed from `NotYetImplementedExecutionSurfaces`; added to `ImmediatelyLifecycleGoverned`
- Disabled modules cannot create new work via scheduled dispatch
- Blocked scheduled work leaves durable evidence in ScheduleRuns and audit

## Related

- [Epic27-Real-Deferred-Execution-Path-Inventory.md](./Epic27-Real-Deferred-Execution-Path-Inventory.md)
- [instance-owned-scheduling.md](../01-architecture/instance-owned-scheduling.md)
