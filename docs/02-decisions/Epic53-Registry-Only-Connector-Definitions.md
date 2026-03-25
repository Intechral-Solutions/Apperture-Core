# Epic 53 — Registry-only connector definitions (drop SQL catalog)

Date: 2026-03-24 (migration `20260324205713_DropConnectorDefinitionsTable`)

## Status

Accepted — implemented.

## Context

Connector definitions were historically mirrored in a SQL **`ConnectorDefinitions`** table. After Epic 52 / 52A, the **authoritative** catalog is **`IConnectorDefinitionRegistry`** (host startup seeding + contributor registration). Keeping a second persisted catalog risks drift, conflicting sources of truth, and confusion about which layer operators and runtime should trust.

## Summary of changes

1. **Migration** — **`DropConnectorDefinitionsTable`** removes the **`ConnectorDefinitions`** table from the SQL schema (with downgrade path recreating the legacy table for rollback scenarios).
2. **Persistence** — No parallel “definition catalog” table; connections still reference connector definition ids as strings aligned with registry ids.
3. **Documentation** — [connector-resource-family](../01-architecture/connector-resource-family.md) states the table was removed and warns against reintroducing a persisted catalog without an ADR and an end-to-end read path.

## Consequences

### Positive

- Single source of truth for definition **shape** in the running host’s registry.
- Eliminates sync/reconciliation work between DB and contributors.

### Negative / operational

- Definition catalog is **not** something you edit in SQL; changes ship with host/module/connector deployment and registration.

## Related

- [ADR-0027](./ADR-0027-connector-definitions-not-platform-clients.md) — catalog source of truth note
- [Epic 52](./Epic52-Connector-Definition-Catalog.md) — HTTP/catalog read surface

## Testing

- EF migrations apply cleanly; integration tests that assume a populated registry use host composition (e.g. governance harness with real startup).
