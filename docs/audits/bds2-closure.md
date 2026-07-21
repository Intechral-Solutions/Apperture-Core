# BDS-2 Closure — Business Metadata Alignment for Sample Modules

**Date:** 2026-07-21  
**Status:** Complete  
**Prerequisite:** [bds1-business-data-surface-design-audit.md](./bds1-business-data-surface-design-audit.md)

## What shipped

### Shared business metadata (operation item `MetadataJson`)

Both `Sample.FileImport` and `Sample.ApiPolling` emit:

| Field | Meaning |
| --- | --- |
| `recordType` | `"customer"` |
| `businessKey` | Normalized external id (same as `SubjectKey`) |
| `displayName` | Human-facing label |
| `changeKind` | FileImport: `imported` / `failed` / `skipped`; ApiPolling: existing classification kinds |
| `observedAt` | Echo of item completion time (`CompletedAtUtc` / outcome timestamps) |

Well-known key names: `OperationItemBusinessMetadataKeys` in ModuleSdk Integration operation-item helpers.

This is **metadata alignment only**. There is still **no** platform `BusinessRecord` / `BusinessRecordSource` table and **no** demo projection store (BDS-3).

### FileImport label/key split

- `SubjectKey` = external id / business key  
- `DisplayLabel` = first+last name, else email, else external id / `row N`

### Demo key strategy (`C-*`)

| Key | FileImport | ApiPolling | Role |
| --- | --- | --- | --- |
| `C-001`, `C-002` | `customers-valid` | `snapshot-initial` (+ later snapshots) | Shared overlap |
| `C-003` | `customers-valid` | — | FileImport-only |
| `C-400` | — | snapshots | ApiPolling-only |
| `C-100` | `customers-mixed` (`valid.one@example.com`, Active) | snapshots (`conflict.c100@apipolling.example`, Inactive) | **Intentional conflict** |

Former ApiPolling `A-*` snapshot ids were remapped to this `C-*` scheme.

### Run Detail copy

Section title/empty copy reframed to **Affected records** (panel framework unchanged; no Records nav).

## Explicit non-goals (still deferred)

- Demo `BusinessRecordProjection` (BDS-3)
- Platform-wide business entity model
- Systems/Services
- Conflict UI/resolution
- Cross-integration writes
- Full UI overhaul / Records navigation

## Next

**BDS-3 — Demo Business Record Projection** — complete: see [bds3-demo-business-record-projection.md](./bds3-demo-business-record-projection.md).

**BDS-4 — Demo Business Record Client & API Polish** — complete: see [bds4-demo-business-record-client-api-polish.md](./bds4-demo-business-record-client-api-polish.md).

**BDS-5** — Demo Customers UI surface (business-first list/detail + drill-through).
