# Epic 28 Corrective Pass — Scheduler Audit Inventory

This document records the factual state of Epic 28 after the corrective pass, per the audit requirements.

## 1. Actual Scheduler Implementation Inventory

### Runtime service

| Component | Status | Location |
|-----------|--------|----------|
| **ScheduleDispatchBackgroundService** | Implemented | `WorkerHost/ScheduleDispatchBackgroundService.cs` |
| **Host** | WorkerHost | Registered in `Program.cs`; polls every 15–30s |
| **IScheduleDispatchRunner** | Implemented | `ScheduleDispatchRunner` in ControlPlane.Security |
| **CronScheduleEvaluator** | Implemented | Persistence.SqlServer |

### Persistence

| Entity | Purpose |
|--------|---------|
| **ScheduleDefinitionEntity** | Schedule metadata, target, cron, enabled |
| **ScheduleAssignmentEntity** | Binds schedule to RuntimePlaneId |
| **ScheduleRunEntity** | Per-firing record (FireTimeUtc, Outcome, OutcomeReason, RunId) |
| **ScheduleStateEntity** | Per-assignment state (LastAttemptedUtc, LastOutcome, LastOutcomeReason, etc.) |

### Dispatch path

- Scheduled dispatch calls `IWorkflowStartDispatchService.StartAsync` (normal platform path)
- Lifecycle gating: `ISchedulerLifecycleGuard.CanDispatchWorkflowAsync` before dispatch
- Blocked outcome: `ScheduleRun` outcome = `BlockedByLifecycle`, audit `scheduler.schedule.blocked_by_lifecycle`
- Operation scheduling: **Not implemented** (V1 workflow-only)

### Multi-host

- Single-host V1; idempotency via `ScheduleId:FireTimeUtc` unique constraint
- Multi-host duplicate prevention: **Not implemented**

## 2. First-run / No-prior-state Semantics (Corrective change)

| Before | After |
|--------|-------|
| `fromUtc = asOfUtc.AddDays(-1)` when no prior state | `fromUtc = asOfUtc.AddMinutes(-5)` |
| Risk: 24 hourly runs on first evaluation for `0 * * * *` | Bounded: at most 5 minutes of catch-up |
| Undocumented | `CronScheduleEvaluator.FirstRunLookbackMinutes = 5`; documented in Epic28-Real-Scheduler-Implementation.md |

## 3. Durable Outcome and Operator Visibility

| Capability | Status |
|------------|--------|
| Last attempted time | ScheduleStateEntity.LastAttemptedUtc |
| Last outcome | ScheduleStateEntity.LastOutcome |
| Last outcome reason | ScheduleStateEntity.LastOutcomeReason |
| Last successful dispatch | ScheduleStateEntity.LastSuccessfulDispatchUtc |
| API exposure | `GET api/schedules/{id}` returns `ScheduleDetailDto` with execution state (`?runtimePlaneId=local`) |
| List with state | List returns definition-only; detail endpoint has state |

## 4. Runtime Composition

- `SchedulerDispatch` in `ImmediatelyLifecycleGoverned` (ExecutionSurfaceCatalog)
- `NotYetImplementedSurfaceIds` is empty
- `RuntimeModuleCompositionQueryServiceTests` asserts SchedulerDispatch is governed

## 5. Tests

| Test | Coverage |
|------|----------|
| `RunCycleAsync_WhenDueScheduleAndModuleEnabled_DispatchesWorkflow` | Enabled module + due schedule → 1 WorkflowStartRequest, outcome Dispatched |
| `RunCycleAsync_WhenModuleDisabled_BlocksDispatch_LeavesBlockedEvidence` | Disabled module → 0 requests, outcome BlockedByLifecycle |
| `RunCycleAsync_WhenTargetInvalid_FailsWithReason` | Invalid target → outcome Failed |
| `RunCycleAsync_FirstRun_WithNoPriorState_UsesBoundedLookback_NoBurstDispatch` | Hourly cron, no prior state → ≤1 run (not 24) |

## 6. Remaining Limitations

- **Operation scheduling**: Not implemented; V1 workflow-only
- **Multi-host**: Single-host assumption; no distributed claiming
- **First-run lookback**: Fixed 5 minutes; not configurable
- **List API**: Does not include execution state (use detail endpoint per schedule)
