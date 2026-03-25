# Epic 54 — Integration as canonical deployment unit (API, readiness, runtime lineage)

Date: 2026-03 (approximate)

## Status

Accepted — implemented.

## Context

Epic 50 left **two** configuration paths (module-scoped vs integration-scoped) and runtime slot resolution that could ignore integration, which breaks down when **multiple integrations** share the same module. [ADR-0028](./ADR-0028-integration-canonical-deployment-unit.md) declares **Integration** the canonical operator and control-plane unit; Epic 54 implements the tightening: retire unsafe creates, thread **integration id** through workflow start and persistence, and expose **evidence-based readiness** and operator detail APIs.

## Summary of changes

1. **API tightening**
   - **Legacy module-only binding creation** is no longer supported for new configuration (e.g. **`BindingsController`** documents retirement; use integration-scoped binding APIs).
   - **New schedules** require integration context where the API enforces it (schedules without **`IntegrationId`** rejected for new creates per ADR-0028).
2. **Operator surfaces** — **`GET /api/integrations/{id}/detail`** and **`GET /api/integrations/{id}/readiness`** — deterministic readiness from persisted facts (slots, bindings, connection compatibility, schedules). Management client DTOs: `IntegrationOperatorDetailDtos` and related types.
3. **Workflow start** — **`StartWorkflowRequestBody`** optional **`IntegrationId`** (must match workflow module when supplied).
4. **Persistence lineage** — **`IntegrationId`** on **`WorkflowStartRequestEntity`**, **`WorkflowInstanceEntity`**, **`OperationRunEntity`** where applicable for operator lineage and scoped behavior.
5. **Runtime execution** — **`StartWorkflowRequest`**, **`IStepExecutionContext`**, **`IWorkflowExecutor`**: optional integration scope for **slot resolution**; resolver uses **`(integrationId, moduleId, slotName)`** when known, else legacy fallback for transitional rows.
6. **Options** — **`IntegrationExecutionOptions`**: configurable fallback integration scope when persisted start requests lack **`IntegrationId`** (transitional).

## Consequences

### Positive

- One coherent operator story: configure and validate an **Integration**; runs trace back when configured.
- Avoids ambiguous “first binding wins” when integration id is supplied end-to-end.

### Negative

- Clients must migrate to integration-scoped binding/schedule flows; old module binding POST and analogous patterns are not supported for new work.

## Related

- [ADR-0028](./ADR-0028-integration-canonical-deployment-unit.md)
- [ADR-0025](./ADR-0025-epic-50-integration-composition.md) — prior parallel paths
- [connector-resource-family](../01-architecture/connector-resource-family.md) — integration detail/readiness endpoints

## Testing

- Unit tests for integration composition and control plane behavior; HTTP governance tests hitting integration detail (see Epic 55 harness).
