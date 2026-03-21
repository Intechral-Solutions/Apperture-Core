# Epic 27 Hard Follow-Up — Real deferred execution path inventory

This inventory documents the **actual implemented** deferred execution paths that exist today, not hypothetical or scaffolded ones.

## Implemented paths (real code, real execution)

| Path | Location | Lifecycle gating | Test coverage |
|------|----------|------------------|---------------|
| **Persisted workflow start** | `StartRequestWorker` → `WorkflowStartRequestProcessor.ProcessClaimedAsync` | `ResolveDefinitionOrThrowAsync` at process boundary; throws `DefinitionNotExecutableException` when module disabled; run marked Failed with `[Blocked]` | `ProcessClaimedAsync_WhenModuleDisabled_BlocksExecution_LeavesBlockedEvidence` |
| **Persisted operation start** | `StartRequestWorker` → `OperationStartRequestProcessor.ProcessClaimedAsync` | Same pattern as workflow | Same pattern |
| **Workflow retry** | `WorkflowRetryService.RequestRetryAsync` → `IWorkflowStartDispatchService.StartAsync` | Dispatch returns null when definition unavailable → returns blocked result, no new request created | `RequestRetryAsync_DefinitionNotExecutable_ReturnsBlockedResult_NoNewRun` |
| **API workflow start** | `WorkflowsController` → `WorkflowStartDispatchService` | Dispatch returns null; no persisted request created | Via dispatch service |
| **API operation start** | `OperationsController` → `OperationStartDispatchService` | Same | Via dispatch service |

## Not implemented (no execution path exists)

| Path | Status | Notes |
|------|--------|-------|
| **Scheduled dispatch** | **No real scheduler exists** | `SchedulerHostStub` runs `Task.CompletedTask`; no code path dispatches scheduled work. `ISchedulerLifecycleGuard` is a future extension point, not runtime enforcement. |

## Restart-bound (governed at resolution, not unregistration)

| Surface | Notes |
|---------|-------|
| **StepHandlerRegistry** | Handlers remain registered when module disabled; invocation still requires resolved definition at processor boundary. |
| **ModulePermissionRegistration** | Permission descriptors seeded at host start; updates require restart. |

## Summary

- **Enforced today**: Persisted workflow/operation start processing, workflow retry, API dispatch.
- **Not enforced (no path)**: Scheduler — no scheduler dispatch path exists.
- **Restart-bound**: Step handler registration, permission registration.
