# Epic 7 Prompt 1: Control-Plane Start Path, Persistence, and Run Read — Implementation Summary

## Status

Implemented. `POST /workflows/{workflowId}/start` is no longer a 501 stub; it now persists a durable start handoff (start request + workflow instance + operation run) and returns a real run id. Runs are queryable via permission-gated read surfaces.

## Workflow start dispatch path (control plane)

**Endpoint:** `POST /workflows/{workflowId}/start`

Flow:

1. **Authorize** using `WorkflowStartAuthorizationService` (definition-level `RequiredPermissions` when present; otherwise fallback semantics).
2. **Validate** orchestration safety (`OrchestrationSafeValidator.ValidateParameters`).
3. **Persist handoff** via `IWorkflowStartDispatchService`:
   - Creates `WorkflowInstance` (status `requested`)
   - Creates `OperationRun` (status `requested`)
   - Persists `WorkflowStartRequestEntity` with parameters JSON + idempotency/correlation metadata
4. **Return** `202 Accepted` with `{ runId, workflowInstanceId, workflowDefinitionId, status }`.

The management host does **not** execute steps (ADR-0001). Runtime consumption/execution remains deferred.

### Corrective pass notes (post–Epic 7)

- **Transactional handoff**: The control-plane dispatch now persists `WorkflowInstance` + `OperationRun` + `WorkflowStartRequestEntity` in a single database transaction to avoid orphaned/partial state on failures.
- **Idempotency safety**: `WorkflowStartRequests.IdempotencyKey` is now protected by a unique index (non-null) so concurrent identical idempotency requests converge on one persisted run.

## SQL-backed persistence implemented

- Implemented `SqlWorkflowPersistence : IWorkflowPersistence` mapping runtime abstractions to:
  - `WorkflowInstanceEntity` (table `WorkflowInstances`)
  - `OperationRunEntity` (table `OperationRuns`)
- Added `WorkflowStartRequestEntity` and migration `AddWorkflowStartRequests` creating table `WorkflowStartRequests` to persist the control-plane start request payload.

## Run query/read surface

**Endpoints:**\n
- `GET /runs` — returns SQL-backed run list (filtered to runs the caller can read)\n
- `GET /runs/{id}` — returns run detail or 404\n

DTOs are returned (not raw entities): `RunSummaryDto` / `RunDetailDto`.

## Run read authorization model

Run reads use the executable permission model (coarse but coherent for Prompt 1):

- If the workflow definition has non-empty `RequiredPermissions`: ALL are required for read.\n
- Otherwise, fall back to the prior coarse workflow model:\n
  - module-owned → `workflow.execute` at `module:{moduleId}`\n
  - otherwise → `workflow.start` at instance\n

Authorization is enforced in the service layer:\n
- List filters out unauthorized runs (does not leak data).\n
- Detail returns **403** for unauthorized callers.\n

## Tests

Updated controller boundary tests so start is no longer 501 and runs controller returns real results when backed by a service.\n

## Deferred to Prompt 2

- Worker/instance consumption loop (dequeue/claim start requests)\n
- Actual workflow step execution and runtime status updates\n
- Signal endpoints\n
- Richer run lifecycle, filtering/search/analytics\n
- Operation execution runtime\n

## Files changed (high-level)

- ControlPlane.Security: `SqlWorkflowPersistence`, start dispatch service, run query + run read auth services, DI wiring\n
- Persistence.SqlServer: `WorkflowStartRequestEntity`, migration + DbContext mapping\n
- Management.Api: `WorkflowsController` (start 202), `RunsController` (real list/detail)\n
- Docs: architecture tracking updated; this summary added\n

