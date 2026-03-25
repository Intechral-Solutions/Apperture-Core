# Epic 48 — Connections surface and “Used In” foundation

Date: 2026-03 (approximate; aligns with integration epics)

## Status

Accepted — implemented.

## Context

Operators need a first-class navigation path to manage **connections** (configured connector instances) and to see **where each connection is consumed** (module slots and, later, integrations). Prior work treated connections as API-only or buried; the management UI needed a coherent menu, DTO shape, and capability signals for gated rendering.

## Summary of changes

1. **Control panel navigation** — A **Connections** menu group with **Connections** (`/connections`) and **Add Connection** (`/connections/new`). There is no separate top-level **Clients** domain; client-related ideas were consolidated into connector/connection surfaces (see Epic 51 → Epic 52 correction).
2. **Connection DTOs** — `ConnectionDto` carries a **`UsedIn`** projection: entries include module id, slot name, contract type, and (when applicable) integration id and display name for integration-owned bindings (extended in Epic 50).
3. **Management API** — List and get connection endpoints return connections with **Used In** populated where bindings exist (see `ConnectionsController` and related services).
4. **Management client** — `IManagementClient` list/get connection methods document Epic 48 alignment for Used In.
5. **Operator capabilities** — `OperatorCapabilitiesDto` includes **`CanViewConnections`**, driven by **`connection.read`**, so the UI can show or hide connection surfaces consistently.

## Consequences

- Operators can discover connection consumption without spelunking bindings-only APIs.
- Sets up Epic 50’s integration-scoped **Used In** entries (`IntegrationId` / display name on `ConnectionUsedInEntry`).

## Related

- [ADR-0024](./ADR-0024-integration-ownership-boundary.md) — Integration ownership (Epic 49) builds on the same resource family.
- [ADR-0025](./ADR-0025-epic-50-integration-composition.md) — Used In bridges to integration-owned bindings.

## Testing

- `tests/Apperture.Management.Ui.Shared.Tests/Epic48ConnectionsSurfaceTests.cs` — menu shape, DTO Used In shape, `CanViewConnections`.
