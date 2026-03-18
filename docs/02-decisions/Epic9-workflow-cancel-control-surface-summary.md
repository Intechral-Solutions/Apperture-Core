# Epic 9 — Workflow Cancel Control Surface (Summary)

This stage adds a **durable, permission-aware workflow cancel control surface** aligned with ADR-0001 runtime boundaries:

- The **control plane coordinates** (authorize + persist intent).
- The **instance plane executes** (worker observes intent and cooperatively stops).

## What was implemented

### API surface

- `POST /api/runs/{runId}/cancel`
  - **202 Accepted** when the run exists and the caller is authorized (idempotent, even if already terminal/cancelled)
  - **404 Not Found** when the run id does not exist
  - **403 Forbidden** when unauthorized

### Persistence model

- New table/entity: `WorkflowCancelRequests` keyed by `RunId`
  - One row per run is treated as durable cancel intent
  - Repeated cancel requests are idempotent (unique key on `RunId`)

### Worker behavior

`WorkflowStartRequestProcessor` checks for cancel intent at safe boundaries:

- before starting execution
- before each next step
- just before writing terminal success

When cancel intent is observed for a non-terminal run/instance, the worker writes a terminal `cancelled` status to:

- `WorkflowStartRequestEntity.Status`
- `OperationRunEntity.Status` and `CompletedAt`
- `WorkflowInstanceEntity.Status`, clears `CurrentStepName`, updates `UpdatedAt`

Cancellation remains **cooperative** (no attempt to interrupt a currently-executing step).

### Authorization model

Cancel authorization reuses the executable permission semantics:

- If definition has `RequiredPermissions`: ALL required
- Else fallback:
  - module-owned workflow → `workflow.execute` at module scope
  - otherwise → `workflow.start` at instance scope

## Deferred beyond this stage

- Workflow signal
- Terminate as a separate concept
- Operation execution surfaces
- Cloud runtime adapters
- Rich lifecycle expansion (`cancelling`, `aborting`, etc.)

