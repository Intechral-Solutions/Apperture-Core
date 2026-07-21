# BDS-3 — Demo Business Record Projection

**Date:** 2026-07-21  
**Status:** Complete  
**Prerequisites:** [bds1-business-data-surface-design-audit.md](./bds1-business-data-surface-design-audit.md), [bds2-closure.md](./bds2-closure.md)

## Purpose

Provide a **demo/sample read-only** projection of customer business records correlated from:

- `Sample.FileImport` and `Sample.ApiPolling` operation items (BDS-2 metadata)
- Provenance links to operation item / group / run

This is **not** the final platform `BusinessRecord` entity model. Naming and routes are deliberately demo-scoped.

## Why not a platform entity table

- Systems/Services ownership model is still deferred (ADR-0034).
- Sample modules are the only aligned emitters today.
- Read-time projection proves UX/value without locking schema.

## Data sources

| Source | Role |
| --- | --- |
| Platform `OperationItems` + `OperationGroups` | Primary: observations, provenance, change kinds |
| Item metadata (`recordType`, `businessKey`, `displayName`, `changeKind`, `observedAt`, `email`, `status`) | Correlation + business fields |
| Change summary `email=` (FileImport) | Fallback when metadata email absent |

ApiPolling durable last-seen is **not** required for BDS-3 (status/email now emit on items). It remains a sync watermark, not a business entity store.

## Correlation rules

- Group by `recordType=customer` + `businessKey`.
- Keep **latest** observation per `(moduleId, integrationId)` by `observedAt` / `CompletedAtUtc`.
- Skip items missing required BDS-2 metadata (incomplete → omitted, not hard-fail).

## Primary field selection

When a single display value is needed:

1. Prefer an **ApiPolling** source observation when present (current-state bias).
2. Else use the latest observation by `observedAtUtc`.

## Soft conflicts

When two or more sources have values for the same field and normalized values differ:

- `email` (case-insensitive)
- `status` (trim)
- `displayName` (trim)

Conflicts are informational only — no resolution, no blocking.

## Provenance

Each source observation includes:

- `operationItemId`, `operationGroupId`, `runId`
- `moduleId`, `integrationId`, `operationKind`
- `provenanceLabel` for UI copy

## API

| Method | Route |
| --- | --- |
| GET | `/api/demo/business-records/customers` |
| GET | `/api/demo/business-records/customers/{businessKey}` |

List query params: `q`, `sourceKind`, `hasConflict`, `skip`, `take`.

## Authorization

Requires authenticated actor with **`integration.read`** at **instance** scope (same coarse gate used for instance-wide integration listing/admin repairs). Documented here; not anonymous.

## Implementation anchors

| Piece | Path |
| --- | --- |
| Projector | `…/DemoBusinessRecords/DemoBusinessRecordProjector.cs` |
| Service | `…/DemoBusinessRecordProjectionService.cs` |
| API | `DemoBusinessRecordsController` |
| Tests | `DemoBusinessRecordProjectionTests` |

## Known limitations

- Demo modules only (`Sample.FileImport`, `Sample.ApiPolling`).
- No Records nav / UI (BDS-5).
- No Systems model.
- No write/edit/conflict resolution.
- Cross-run scan is unbounded for demo scale; pagination is on projected results only.

## Next

- **BDS-4** — harden read APIs / Management Client / optional filters polish  
- **BDS-5** — Business Record Surface (list/detail UI, drill-through to Affected records / runs)
