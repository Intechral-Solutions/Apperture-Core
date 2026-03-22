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
| Global scheduler run cycle | `POST /api/control/recovery/schedules/run-cycle` | Triggers **global** scheduler re-evaluation for the runtime plane. Processes all schedules due at cycle time. **Not** schedule-specific; blocked schedules re-evaluate at NextDueUtc. Response includes `action: "global_scheduler_run_cycle"` and `message` stating "not schedule-specific". |

**Schedule remediation semantics (explicit)**: The run-cycle endpoint is a **global** scheduler re-evaluation trigger, not a schedule-specific "force fire now" or "replay this schedule". Schedule-specific remediation is **not supported** in V1.

### 4. Refused remediation behavior

- Before retry: re-check eligibility via `IRecoveryQueryService.GetBlockedWorkRemediationForRunAsync`. If not eligible, return 409 without calling dispatch; audit `recovery.blocked_work.retry_refused`.
- **Operation remediation limitation (explicit)**: Operation blocked work is **inspectable only**. `RetryEligibleNow` is always false for `DefinitionKind=operation`; `RetryEligibleReason` is "Operation retry not yet supported." Retry returns **400 Bad Request** with:
  - `inspectionOnly: true` — operator-meaningful flag indicating platform does not support operation retry
  - `definitionKind: "operation"`
  - `message`: "Operation blocked work is inspectable via GET blocked-work; platform recovery does not yet support operation retry."
  - Root cause: `StartOperationRequest` lacks `RetryOfRunId`; no trusted execution path exists for operation retry in this epic.

### 5. Schedule and module remediation context

- **Schedule**: `RemediationAvailable` indicates whether run-cycle can dispatch (enabled and target module executable). The run-cycle endpoint triggers a **global** scheduler cycle; there is no schedule-specific "force fire now" in V1.
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

## Epic 31 corrective pass

- **Workflow remediation proof**: `RecoveryWorkflowRemediationSqlServerTests.BlockedWorkflow_BecomesRetryableAfterModuleEnabled_DispatchesThroughTrustedPath` proves: (1) blocked workflow exists while module disabled, (2) eligibility false when disabled, (3) module re-enabled, (4) retry succeeds, (5) trusted path used (WorkflowRetryService → WorkflowStartDispatchService → SQL persistence: OperationRun with RetryOfRunId, WorkflowStartRequest, WorkflowLifecycleEvents). Requires SQL Server. Trait: `Category=SqlServer`.
- **Schedule remediation semantics**: Run-cycle is explicitly a **global** scheduler re-evaluation; response includes `action: "global_scheduler_run_cycle"` and `message` stating "not schedule-specific". `RecoveryControllerTests.RunScheduleCycle_InvokesGlobalSchedulerCycle_Returns202` asserts both. Schedule-specific remediation not supported in V1.
- **Operation limitation**: Operation blocked work is inspection-only. `RetryEligibleNow` always false; retry returns 400 with `inspectionOnly: true`, `definitionKind: "operation"`, operator-meaningful message. `RecoveryControllerTests.RetryBlockedWork_WhenOperation_Returns400WithInspectionOnly` asserts response contract. Root cause documented: `StartOperationRequest` lacks RetryOfRunId.

## References

- Epic 30 Runtime Health Diagnostics
- Epic 27 Real Deferred Execution Path Inventory
- `docs/04-api/contracts.md` — Recovery API section
