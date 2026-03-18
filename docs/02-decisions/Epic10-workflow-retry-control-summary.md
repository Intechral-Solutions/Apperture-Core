# Epic 10 — Workflow Retry Control (Summary)

This stage adds a **durable, permission-aware workflow retry-as-new-run control surface** aligned with ADR-0001 runtime boundaries:

- The **control plane coordinates** (authorize + start a new run).
- The **instance plane executes** (worker consumes the new start request like any other run).

## What was implemented

### API surface

- `POST /api/runs/{runId}/retry`
  - **202 Accepted** when the source run exists, is eligible, and the caller is authorized
  - **404 Not Found** when the source run does not exist
  - **403 Forbidden** when unauthorized
  - **409 Conflict** when the source run is not eligible for retry

### Semantics

- Retry means **start the workflow again as a new run**
- The original run remains terminal and unchanged
- Retry is allowed only for terminal `failed` and `cancelled` runs
- Retry does **not** mean resume or in-place restart

### Persistence / lineage

- New runs store `RetryOfRunId` on `OperationRunEntity`
- The new run is created through the existing start dispatch pipeline, which also creates a new workflow instance and start request
- Run query surfaces project `RetryOfRunId` so lineage is visible

### Authorization model

Retry reuses the executable permission semantics:

- If definition has `RequiredPermissions`: ALL required
- Else fallback:
  - module-owned workflow → `workflow.execute` at module scope
  - otherwise → `workflow.start` at instance scope

## Deferred beyond this stage

- Signal
- In-place restart/resume
- Richer lifecycle expansion (`restarting`, etc.)
- Operation execution surfaces
- Cloud runtime adapters
- Rich telemetry/reporting

