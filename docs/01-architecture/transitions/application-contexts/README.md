# Application / context architecture — current inventory

## Status: Superseded (2026-09-18) — pre-CTX before-snapshot, kept as history

The later planning pass this folder was gathered for happened: it became CTX-1 through CTX-3B
(ResourceContext ownership, Shared + Applications, resource movement, archive integrity), all of which
are complete and merged to `master`. Per this README's own original condition below ("archive... when it
is no longer needed as a before-snapshot"), that condition is now met. This folder is kept in place as
history, per repository convention (compare
[`ADR-0026`](../../../02-decisions/ADR-0026-epic-51-client-definition-model.md)/
[`Epic51-Client-Definition-Model-Historical.md`](../../../02-decisions/Epic51-Client-Definition-Model-Historical.md)
for how this repository marks a superseded decision without deleting it) — **not rewritten, not deleted.**

**Do not use this folder as a current-state reference.** For the current, living status of everything it was
gathered to inform, see:

- [`ctx1-closure.md`](../resource-contexts/ctx1-closure.md), [`ctx2-closure.md`](../resource-contexts/ctx2-closure.md), [`ctx3a-closure.md`](../resource-contexts/ctx3a-closure.md), [`ctx3b-closure.md`](../resource-contexts/ctx3b-closure.md) — what CTX delivered and what it deliberately deferred.
- [`components-services-catalog/README.md`](../components-services-catalog/README.md) — the Resource Catalog question this folder raised, now resolved (retired as a standalone concept, split into Components + Services).

`current-inventory.md` carries its own superseded banner at its top, including the one specific row now
known to be factually wrong (`ApplicationId` absence) alongside the rows that remain accurate.

---

*Original README content below, preserved as written:*

**Not a target architecture.** This set is an evidence snapshot of the **current** codebase, gathered so a later planning pass can decide whether and how to introduce Application/Context containers, a Shared context, and a Resource Catalog.

Do not treat this folder as a contract. Runtime source of truth remains the code cited in [current-inventory.md](current-inventory.md).

| File | Role |
|------|------|
| [current-inventory.md](current-inventory.md) | Factual inventory: nav, domain relationships, settings, modules, connections, execution, notifications, business data, dashboards, IAM-5 authz, ContextId migration hazards, reuse map |
| This README | How to use the set |

## Original status note

Evidence gathered. No implementation and no roadmap in this folder.

## After a later planning pass

1. Record decided direction in `docs/02-decisions/` (ADR).
2. Update canonical docs under `docs/01-architecture/` only for what ships.
3. Archive or delete this folder when it is no longer needed as a before-snapshot.
