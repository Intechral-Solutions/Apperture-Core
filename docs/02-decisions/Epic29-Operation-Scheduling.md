# Epic 29 — Operation Scheduling Support

## Status

Implemented

## Context

Epic 28 implemented real scheduler dispatch for workflows. Operation scheduling remained unimplemented. Epic 29 extends the same scheduler runtime to support operation targets.

## Decision

### 1. Target model extension

- **Create API**: `TargetJobType` added to `CreateScheduleRequestBody` (default 0 = WorkflowExecution).
- **Validation**: Only `WorkflowExecution` (0) and `ModuleOperationExecution` (1) allowed; 400 for others.

### 2. JobConfiguration formats

| Target type | JobConfiguration |
|-------------|------------------|
| WorkflowExecution | `{"workflowDefinitionId":"Sample.HelloWorld.HelloWorld"}` |
| ModuleOperationExecution | `{"operationDefinitionId":"Sample.HelloWorld.DoWork"}` |

### 3. Scheduler runtime extension

- **CronScheduleEvaluator**: Includes `ModuleOperationExecution` in allowed target types.
- **ScheduleDispatchRunner**: Branches on target type; workflow path unchanged; new operation path.
- **ISchedulerLifecycleGuard**: Added `CanDispatchOperationAsync(OperationId)`.
- **SchedulerLifecycleGuard**: Uses `IOperationRegistryService.GetOperationAsync` (lifecycle-filtered).

### 4. Operation dispatch flow

1. Parse `operationDefinitionId` from JobConfiguration.
2. Call `CanDispatchOperationAsync`; if false → BlockedByLifecycle, audit, return.
3. Build `StartOperationRequest` with idempotency and correlation.
4. Call `IOperationStartDispatchService.StartAsync`; if null → Failed, audit.
5. On success → Dispatched, audit, update state.

### 5. Durable outcomes

Same semantics as workflow: `Dispatched`, `BlockedByLifecycle`, `Failed`. `ScheduleRun.RunId` stores OperationRunId when dispatched.

### 6. Host/DI

- `AddRuntimeSchedulerDispatch` registers `IOperationStartDispatchService` for WorkerHost scheduler.

### 7. Remaining limitations

- Single-host scheduler (unchanged)
- First-run 5-minute lookback (unchanged)
- Other `ScheduledJobType` values (ConnectorSyncOrPolling, etc.) unsupported
- Scheduled operations dispatch with `Parameters: null`

## Related

- [Epic28-Real-Scheduler-Implementation.md](./Epic28-Real-Scheduler-Implementation.md)
- [Epic27-Real-Deferred-Execution-Path-Inventory.md](./Epic27-Real-Deferred-Execution-Path-Inventory.md)
