# Epic 30 — Runtime Health, Diagnostics, and Degraded-State Visibility

## Status

Implemented

## Context

Epics 26–29 established lifecycle model, runtime composition, scheduler dispatch, and operation scheduling. Platform truth was distributed across composition snapshots, schedule state, run outcomes, and audit. Operators lacked a unified way to see health, degraded state, blocked work, and anomalies without reading logs or source code.

## Decision

### 1. Runtime health snapshot

- **Endpoint**: `GET /api/control/runtime-health`
- **Query parameters**: `runtimePlaneId` (default "local"), `blockedDeferredWindowHours` (default 24), `scheduleFailureWindowHours` (default 24)
- **Response**: `RuntimeHealthSnapshot` with overall status, degradation reasons, module diagnostics, schedule diagnostics, blocked deferred work, anomalies, and surface catalogs

### 2. Overall health status

| Status    | Meaning                                                                 |
|-----------|-------------------------------------------------------------------------|
| Healthy   | No degraded modules, no blocked schedules, no blocked deferred work     |
| Degraded  | One or more: degraded module, blocked schedule, failed schedule, blocked deferred work, invalid persisted state |
| Unhealthy | Reserved for severe conditions; not used in V1                          |

### 3. Diagnostic sources aggregated

- **Runtime composition**: `IRuntimeModuleCompositionQueryService` — module lifecycle, effectively executable, in-flight work, execution degraded reason
- **Schedule state**: `IScheduleManagementService`, `IScheduleRepository` — last outcome, last attempted, target module executable
- **Blocked deferred work**: `OperationRuns` where `Status=Failed` and `FailureSummary` starts with `[Blocked] ` (RunFailureCapture.DeferredWorkBlockedPrefix)
- **Run kind resolution**: `IRunKindResolver` for workflow vs operation attribution

### 4. Module diagnostics

Per-module fields: ModuleId, LifecycleState, PersistedEnabled, EffectivelyExecutable, AllowsNewStarts, HasInFlightWork, ValidationIssueCodes, ExecutionDegradedReason, BlockedDeferredWorkCount, BlockedScheduleCount.

### 5. Schedule diagnostics

Per-schedule: ScheduleId, ScheduleName, Enabled, TargetJobType, TargetJobConfiguration, NextDueUtc, LastAttemptedUtc, LastOutcome, LastOutcomeReason, ScheduleHealthStatus (Healthy/Degraded/Blocked/Unknown), DegradedReason, TargetModuleExecutable.

### 6. Blocked deferred work

Recent runs (configurable window) that failed with lifecycle-blocked semantics. Includes RunId, DefinitionId, DefinitionKind (workflow/operation), ModuleId, FailedAtUtc, Summary.

### 7. Execution anomalies

- `module_invalid_persisted_state`: Enabled in persistence but not effectively executable
- `schedule_blocked` / `schedule_degraded`: Schedule with BlockedByLifecycle or Failed outcome
- `deferred_work_blocked`: Blocked deferred run (recent window)

### 8. What this epic does not do

- Full alerting/notification system
- Dashboard UI (API/read model only)
- Remediation workflows
- Fabricated metrics

## Consequences

- Operators can query `GET /api/control/runtime-health` for a truthful snapshot
- Degraded modules, blocked schedules, and blocked deferred work are surfaced
- Each diagnostic item includes related entity references for drill-down
- No cosmetic health: status derived from actual platform state

## Remaining limitations

- Single-host; no distributed health aggregation
- Blocked deferred work query requires relational DB (InMemory test skipped)
- No minimal UI; API layer only
