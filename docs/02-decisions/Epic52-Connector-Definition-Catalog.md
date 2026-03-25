# Epic 52 — Connector definition catalog (and Epic 52A authorization)

Date: 2026-03 (approximate)

## Status

Accepted — implemented.

## Context

After Epic 51 was rolled back philosophically ([ADR-0027](./ADR-0027-connector-definitions-not-platform-clients.md)), the platform still needs an **honest catalog** of connector **types**: identity, display metadata, **connection field descriptors** (config vs secret hints), and optional **capability flags**—without exposing generic “operations” or implying the platform executes connector methods.

Epic 52 implements that catalog at the HTTP and client layers. Epic 52A corrects **which permission** gates catalog read so catalog access is not overloaded onto **`connection.manage`**.

## Summary of changes (Epic 52)

1. **API** — **`GET /api/connector-definitions`** and **`GET /api/connector-definitions/{id}`** return catalog entries (fields array, capabilities string, etc.).
2. **DTOs** — `ConnectorDefinitionCatalogDto`, `ConnectionFieldCatalogDto` on the management client; documented as shape metadata, not executable operations.
3. **UI/tests** — Connections area treats connector definitions as catalog input for “add connection” and related flows; unit tests in `ConnectorDefinitionsControllerTests`.
4. **Removal** — Legacy **`/api/clients`** surface removed (per ADR-0027).

## Summary of changes (Epic 52A)

1. **Action** — Instance-scoped **`connector.definition.read`** for reading the connector-definition catalog (see `ActionConstants`).
2. **Authorization** — `ConnectorDefinitionCatalogReadService` (and related checks) evaluates **`connector.definition.read`** on **`ConnectorDefinition`** at instance scope—not **`connector.manage`**.
3. **Operator capabilities** — `OperatorCapabilitiesDto.CanViewConnectorDefinitions` reflects catalog read permission.
4. **Tests** — `ConnectorDefinitionCatalogReadServiceTests` assert the correct permission evaluation.

## Consequences

- Operators with only connection management rights do not automatically see full definition catalog unless policy grants **`connector.definition.read`** (or equivalent role mix).
- Catalog content is served from in-process registry contributors; Epic 53 removes the obsolete SQL mirror (see below).

## Related

- [ADR-0027](./ADR-0027-connector-definitions-not-platform-clients.md)
- [Epic 53](./Epic53-Registry-Only-Connector-Definitions.md) — drop persisted `ConnectorDefinitions` table
- [connector-resource-family](../01-architecture/connector-resource-family.md)

## Testing

- `tests/Apperture.UnitTests/ControlPlane/ConnectorDefinitionsControllerTests.cs`
- `tests/Apperture.UnitTests/ControlPlane/ConnectorDefinitionCatalogReadServiceTests.cs`
- `tests/Apperture.Management.Ui.Shared.Tests/Epic48ConnectionsSurfaceTests.cs` (catalog DTO shape assertions)
