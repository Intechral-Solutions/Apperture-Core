# ADR-0029: Module-contributed procedures (integration-scoped operator actions)

## Status

Accepted

## Context

Operators need a deliberate, auditable way to trigger module behavior from the management plane (start a workflow or operation with parameters) **without** exposing generic connector “client operation” catalogs. [ADR-0027](./ADR-0027-connector-definitions-not-platform-clients.md) removed platform-managed client operations; typed connector use stays in module code. We still need an **explicit, module-declared** surface that names stable actions, describes parameters for UI, and launches through the existing workflow/operation execution substrate with clear provenance (which integration and which procedure).

## Decision

1. **Procedure model** — A **procedure** is declared by a module (stable `procedureId`, display metadata, optional category, `LaunchKind` = `Workflow` or `Operation`, `TargetDefinitionId`, optional JSON parameter schema using the same `fields[]` convention as operations, optional `RequiredPermissions`; when absent, authorization defers to the target definition’s permissions).

2. **Discovery** — Procedures are listed on the module manifest (`ModuleManifest.Procedures`) and aggregated at host startup into `IProcedureRegistryService` (same lifecycle/activation patterns as operations).

3. **Integration scope** — Management API exposes procedures under `GET/POST /api/integrations/{integrationId}/procedures` (list, detail, launch). Availability is **server-owned**: integration exists, actor may read the integration, integration **readiness** is acceptable, the procedure’s module matches the integration’s `ModuleId`, and the target definition exists for that module.

4. **Launch** — Launch is **not** a generic connector RPC. `POST .../procedures/{procedureId}/launch` validates orchestration-safe parameters, optionally evaluates procedure-level permissions, then calls `IWorkflowStartDispatchService` or `IOperationStartDispatchService` with `IntegrationId` and `ProcedureId` for persisted provenance.

5. **Provenance** — Optional `ProcedureId` is stored on workflow/operation start requests and runs (alongside existing `IntegrationId` on workflows). Run queries expose these fields when present.

6. **Non-goals** — No platform-managed connector operation catalog, no arbitrary RPC tunnel, no second permission system beyond procedure overrides plus existing start authorization.

## Consequences

### Positive

- Honest operator surface: named module procedures only, aligned with ADR-0027.
- Metadata-first UI can render small forms from the shared JSON schema.
- Auditing and run forensics can tie executions to integration + procedure.

### Negative

- Modules must declare procedures explicitly (no introspection of connector APIs).
- Additional columns and API surface to maintain.

### Neutral

- Parameter schema remains the existing `fields[]` JSON shape; UI-only hints (`label`, `help`, `default`) are optional extensions.
