# Epic 49 — Integration ownership boundary and management API

Date: 2026-03 (approximate)

## Status

Accepted — implemented.

## Context

The platform needed an operator-facing resource that represents a **configured instance of a module**—distinct from the module **definition**. Bindings and schedules were previously “loose”; Epic 49 introduces **Integration** as the ownership anchor and wires CRUD + governance so operators can manage that unit. Schema preparation for nullable `IntegrationId` on bindings and schedules is recorded in ADR-0024; Epic 49 adds the **Integrations** table, domain model, persistence, API, and permissions.

## Summary of changes

1. **Domain** — `Integration` aggregate: references a module id, display metadata, scope/state, timestamps; documented as the operator deployment/configuration unit alongside Epic 50 settings.
2. **Persistence** — `Integrations` table (`AddIntegrationTables` migration), `IntegrationEntity`, `SqlIntegrationRepository`, `IIntegrationRepository`.
3. **Application** — `IIntegrationManagementService`, `IntegrationManagementService` (authorization-enforced workflows), create/update request models.
4. **Governance** — Resource type **`Integration`**; actions such as **`integration.read`**, **`integration.create`**, **`integration.update`**, **`integration.delete`** (see `ActionConstants`, `ResourceTypeConstants`). Platform admin seed/repair includes integration management permissions.
5. **API** — `IntegrationsController`: list, get, create, update, delete integrations (`/api/integrations/...`).
6. **Management client** — DTOs and `IManagementClient` methods for integration CRUD.
7. **Operator capabilities** — `CanViewIntegrations` / `CanManageIntegrations` derived from integration permissions.

### Explicit scope in Epic 49 (per ADR-0024)

- **Schema/entity and nullable `IntegrationId`** on bindings and schedule definitions for forward compatibility.
- **Integration CRUD** as the new hub; binding/schedule **composition** behavior expands in Epic 50; tightening to integration-only create paths is Epic 54.

## Consequences

- Clear story: **Module** = reusable definition; **Integration** = configured instance.
- Legacy rows may still have `IntegrationId = null` until migration/backfill.

## Related

- [ADR-0024](./ADR-0024-integration-ownership-boundary.md)
- [ADR-0025](./ADR-0025-epic-50-integration-composition.md)
- [ADR-0028](./ADR-0028-integration-canonical-deployment-unit.md)

## Testing

- Unit and integration coverage around integration APIs and governance (see control plane and integration test suites referencing integrations).
