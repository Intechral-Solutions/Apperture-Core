# Epic 73 — Execution hot path, host roles, and runtime boundary

## Status

Accepted

## Context

The repository mixed **aspirational runtime contracts** (`IWorkflowStarter`, `IWorkflowRuntime`) with the **actual SQL-backed execution path**, which made host dependencies and documentation misleading. WorkerHost also pulled in broad control-plane packages only to obtain schedule and workflow start dispatch. Module-specific tables were modeled on the platform `AppertureDbContext`, and connector client factories were centralized with hard references to specific connector assemblies.

## Decision

1. **Execution hot path (today)**  
   - Workflow and operation starts are **durably enqueued** via `IWorkflowStartDispatchService` and `IOperationStartDispatchService` (contracts in `Apperture.Core.Application`; SQL implementations in `Apperture.RuntimePlane.Dispatch.SqlServer`).  
   - The worker **polls/claims** `WorkflowStartRequest` (and related) rows and runs **inline step execution** through `IWorkflowExecutor` / the runtime engine.  
   - This path is **not** DTFx or Azure Durable Functions orchestration today.

2. **Host roles (current deployment)**  
   - **WorkerHost:** Claim/execute start requests; hosts `ScheduleDispatchBackgroundService` and `IScheduleDispatchRunner` so **schedule-driven dispatch runs in the worker process** in the current layout.  
   - **SchedulerHost:** Present as a stub; **not** the live schedule dispatcher.  
   - **Management.Host:** HTTP/control surface, governance, recovery, authentication; shares the database and uses the same dispatch services for API-triggered starts.

3. **Contracts**  
   - **`IWorkflowStarter`:** Portable shape for “start a workflow” but **not wired on the hot path** today; prefer dispatch services for durable enqueue.  
   - **`IWorkflowRuntime`:** Future-facing adapter surface for DTFx / Durable Functions; current stubs throw or are non-operational for production orchestration.

4. **Replay / idempotency (what authors can rely on today)**  
   - Start and operation dispatch paths accept **idempotency keys** and dedupe or no-op when appropriate (see dispatch service implementations and schedule repository fire semantics).  
   - Schedule dispatch uses the runner’s interaction with persisted schedules; workers do not execute arbitrary “scheduler logic” outside that pipeline.

5. **Runtime / persistence boundaries**  
   - Platform `AppertureDbContext` owns **cross-cutting instance data** (workflows, operations, schedules, connectors, identity, audit, etc.).  
   - **Module-specific business tables** use a **module-owned `DbContext`** with an **isolated EF migrations history** (separate history table), still against the same SQL Server database where appropriate.  
   - First slice: UNISA payment plan tables moved to `UnisaPaymentPlansDbContext` with `__EFMigrationsHistory_UnisaPaymentPlans`; platform migration `Epic73_RemoveUnisaTablesFromPlatformModel` updates the platform model **without** dropping existing tables.

6. **Connector registration**  
   - `Apperture.ConnectorResolution` stays core resolution only; connector packages register **definitions and client factory contributors** via their own DI extensions (no hard project references from ConnectorResolution to specific connector implementations).

## Consequences

### Positive

- WorkerHost no longer depends on the monolithic control-plane security package for dispatch.  
- Documentation and agent playbooks align with the real enqueue and execute path.  
- Module and connector boundaries are clearer for Epic 74 and later integration work.

### Negative

- Operators must understand **two migration histories** for the same database when module contexts are used (platform + module history tables).  
- `IWorkflowStarter` naming in older prose can confuse readers until fully reconciled with dispatch interfaces.

### Neutral

- `IWorkflowRuntime` and DTFx/Durable Functions adapters remain **future** work; Management may still register DTFx-related services for non-hot-path scenarios.
