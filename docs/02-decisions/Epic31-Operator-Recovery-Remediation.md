# Epic 31 — Operator Recovery and Remediation Workflows

## Status

Implemented

## Context

Epic 30 delivered runtime health diagnostics including blocked deferred work, schedule diagnostics, and module diagnostics. Operators could inspect degraded state but lacked explicit recovery actions and eligibility projection. This epic adds safe, explicit operator recovery and remediation workflows.

## Decision

### 1. Remediation read model

- **BlockedWorkRemediationEntry**: RunId, DefinitionId, DefinitionKind, ModuleId, FailedAtUtc, BlockedReason, **RetryEligibleNow**, **RetryEligibleReason**
- **ScheduleRemediationEntry**: ScheduleId, ScheduleName, Enabled, LastOutcome, LastOutcomeReason, TargetModuleExecutable, **RemediationAvailable**, NextDueUtc, NextCycleProcessesAt
- **ModuleRemediationEntry**: ModuleId, LifecycleState, EffectivelyExecutable, ExecutionDegradedReason, BlockedDeferredWorkCount, BlockedScheduleCount, **PrerequisiteHint**

### 2. Eligibility rules (explicit in code)

- Blocked workflow run: `RetryEligibleNow` = (definition's module is in `composition.EffectivelyExecutableModuleIds`)
- Blocked operation run: Always `RetryEligibleNow = false`, `RetryEligibleReason = "Operation retry not yet supported."` (Option A)
- Schedule: `RemediationAvailable` = (enabled && TargetModuleExecutable)

### 3. Supported recovery actions

| Action | Endpoint | Behavior |
|--------|----------|----------|
| Retry blocked workflow | `POST /api/control/recovery/blocked-work/{runId}/retry` | Delegates to `IWorkflowRetryService.RequestRetryAsync`; returns 409 with `blocked: true` when ineligible or when retry returns blocked |
| Run schedule cycle | `POST /api/control/recovery/schedules/run-cycle` | Calls `IScheduleDispatchRunner.RunCycleAsync(planeId, UtcNow)`; processes schedules due at current time |

### 4. Refused remediation behavior

- Before retry: re-check eligibility via `IRecoveryQueryService.GetBlockedWorkRemediationForRunAsync`. If not eligible, return 409 without calling dispatch; audit `recovery.blocked_work.retry_refused`.
- Operation retry: returns 400 with error "Operation retry not yet supported."

### 5. Schedule and module remediation context

- **Schedule**: `RemediationAvailable` indicates whether run-cycle can dispatch (enabled and target module executable). No "force fire this schedule now" in V1.
- **Module**: `PrerequisiteHint` guides operators (e.g. "Enable module to allow new starts"). No new lifecycle actions; link to existing `POST /modules/{id}/enable`.

### 6. Audit records

| Action | Outcome |
|--------|---------|
| `recovery.blocked_work.retry_requested` | Actor requested retry (before dispatch) |
| `recovery.blocked_work.retry_dispatched` | Retry succeeded, new run created |
| `recovery.blocked_work.retry_refused` | Ineligible; no dispatch |
| `recovery.schedules.run_cycle_requested` | Run-cycle completed |

### 7. Not in scope (V1)

- Operation retry (StartOperationRequest lacks RetryOfRunId; requires follow-up epic)
- Force fire single schedule
- Auto-healing / bulk remediation
- Minimal UI: Blocked work / Recovery page (API layer complete; UI should follow in next epic)

## Consequences

- Operators can list blocked work with eligibility, retry eligible workflows, and trigger schedule cycles
- Eligibility is projected from runtime composition truth; no fabrication
- Refused retries are audited without invoking dispatch
- Schedule and module remediation context supports operator triage

## References

- Epic 30 Runtime Health Diagnostics
- Epic 27 Real Deferred Execution Path Inventory
- `docs/04-api/contracts.md` — Recovery API section
