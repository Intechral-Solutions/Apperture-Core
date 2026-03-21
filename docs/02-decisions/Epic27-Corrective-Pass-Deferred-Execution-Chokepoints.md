# Epic 27 Corrective Pass — Deferred execution chokepoints

## Purpose

This inventory identifies where deferred work becomes actual execution. Each path must be lifecycle-gated so disabled modules cannot begin new work through persisted, scheduled, queued, or retried flows.

## Chokepoint inventory

| Path | Location | Classification | Lifecycle gating |
|------|----------|----------------|------------------|
| Persisted workflow start | `WorkflowStartRequestProcessor.ProcessClaimedAsync` → `ResolveDefinitionOrThrowAsync` | **Gated** | Definition resolution fails for disabled modules → `DefinitionNotExecutableException` → run marked Failed, audit recorded |
| Persisted operation start | `OperationStartRequestProcessor.ProcessClaimedAsync` → `ResolveDefinitionOrThrowAsync` | **Gated** | Same pattern as workflow |
| Workflow retry (creates new run) | `WorkflowRetryService.RequestRetryAsync` → `IWorkflowStartDispatchService.StartAsync` | **Gated (corrective)** | Dispatch returns null when definition unavailable → returns blocked result, audit recorded (no throw) |
| Scheduled dispatch | `SchedulerHostStub` | **Stub / contract** | Not implemented; `ISchedulerLifecycleGuard` contract added so future scheduler must gate before dispatch |
| API workflow start | `WorkflowsController` → `IWorkflowStartDispatchService` | **Gated** | Dispatch returns null → 404/blocked, audit |
| API operation start | `OperationsController` → `OperationStartDispatchService` | **Gated** | Same |
| Parked workflow resume | `WorkflowStartRequestProcessor` (parked + resume control) | **Gated** | Re-enters `ProcessClaimedAsync` → `ResolveDefinitionOrThrowAsync` at next claim |
| Stale run reclaim | `WorkflowStartRequestProcessor` (stale lease reclaim) | **Gated** | Same; resolution at start of each `ProcessClaimedAsync` invocation |
| Step handler invocation | Within `ProcessClaimedAsync` step loop | **Gated** | Definition already resolved at loop start; no new resolution until next claim cycle |
| Step handler registration | WorkerHost startup | **Restart-bound** | Handlers registered for all DI modules; invocation still requires resolved definition at process boundary |

## New start vs continuation

- **New start**: Requested, Parked (with resume), or reclaimed stale run — each invokes `ResolveDefinitionOrThrowAsync` at the start of `ProcessClaimedAsync`.
- **Continuation**: Within a single `ProcessClaimedAsync` invocation, steps execute using the definition resolved at entry. No re-resolution until the run completes, parks, or loses the lease.

## In-flight work policy (enforced)

- Work already executing (inside `ProcessClaimedAsync` step loop) continues to completion.
- On the next claim cycle (new run, resume, reclaim), resolution runs and fails if the module is disabled.
- Disabled modules with draining in-flight work are visible via `HasInFlightWork` in runtime composition.

## Restart-bound surfaces

- **StepHandlerRegistry** — Handlers not unregistered when module disabled; invocation gated by definition resolution.
- **ModulePermissionRegistration** — Permission descriptors seeded at host start.
