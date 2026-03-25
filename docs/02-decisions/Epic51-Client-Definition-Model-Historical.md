# Epic 51 — Client definition model (historical; superseded)

Date: 2026-03 (approximate)

## Status

**Superseded** — the platform contract described here was **removed** in favor of connector-definition catalog + typed connector code. Do not implement new features against this model.

## Context

Epic 51 introduced a **ClientDefinition** abstraction above connections: metadata for required connector definition, optional module association, “uses gateway,” and a list of **client operations** (metadata only). It included **`IClientDefinitionProvider`**, query/read services gated by **`connection.read`**, **`GET /api/clients`**, management client methods, and a **Clients** UI tab replacing a placeholder.

The intent was to catalog “what a client type can do” in the control plane for future integration usage.

## What was delivered (then reversed)

Captured in [ADR-0026](./ADR-0026-epic-51-client-definition-model.md):

- Domain types **`ClientDefinition`**, **`ClientOperationMetadata`**
- Contribution **`IClientDefinitionProvider`** and aggregation services
- HTTP **`GET /api/clients`**, **`GET /api/clients/{id}`**
- Management UI **Clients** tab listing definitions

## Why it was superseded

[ADR-0027](./ADR-0027-connector-definitions-not-platform-clients.md) records the correction: the platform should not expose a generic **client / operation** catalog that implies a second invocation plane. Typed access to external systems stays in **connector and module assemblies**; the management plane governs **connector definitions** (shape of connection material), **connections**, **slots/bindings**, and **runtime resolution**—not executable client APIs.

## Current direction (Epic 52+)

- **Connector definition catalog** endpoints and **`IConnectorDefinitionRegistry`** (host-seeded / contributor-registered) replace the clients API.
- **`connection.read`** alone is not the gate for catalog read; see Epic 52A (**`connector.definition.read`**).

## Consequences (historical)

- Any external consumer of **`GET /api/clients`** must migrate to **connector-definition** APIs (breaking change, per ADR-0027).
- ADR-0026 remains in the repo as **historical context** only.

## Related

- [ADR-0026](./ADR-0026-epic-51-client-definition-model.md) — original decision (superseded)
- [ADR-0027](./ADR-0027-connector-definitions-not-platform-clients.md) — replacement boundary
