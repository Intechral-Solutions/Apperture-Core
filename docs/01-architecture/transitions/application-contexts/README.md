# Application / context architecture — current inventory

**Not a target architecture.** This set is an evidence snapshot of the **current** codebase, gathered so a later planning pass can decide whether and how to introduce Application/Context containers, a Shared context, and a Resource Catalog.

Do not treat this folder as a contract. Runtime source of truth remains the code cited in [current-inventory.md](current-inventory.md).

| File | Role |
|------|------|
| [current-inventory.md](current-inventory.md) | Factual inventory: nav, domain relationships, settings, modules, connections, execution, notifications, business data, dashboards, IAM-5 authz, ContextId migration hazards, reuse map |
| This README | How to use the set |

## Status

Evidence gathered. No implementation and no roadmap in this folder.

## After a later planning pass

1. Record decided direction in `docs/02-decisions/` (ADR).
2. Update canonical docs under `docs/01-architecture/` only for what ships.
3. Archive or delete this folder when it is no longer needed as a before-snapshot.
