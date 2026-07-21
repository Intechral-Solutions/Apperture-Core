# BDS-4 — Demo Business Record Client & API Polish

**Date:** 2026-07-21  
**Status:** Complete  
**Prerequisites:** [bds3-demo-business-record-projection.md](./bds3-demo-business-record-projection.md)

## Purpose

Prepare the demo customer business-record projection for the first UI surface (BDS-5) by adding Management Client DTOs/methods, contract tests, and small additive API ergonomics.

Still **demo-scoped**. No Records UI/nav. No platform `BusinessRecord` table. No Systems/Services.

## Client methods

| Method | Route | Notes |
| --- | --- | --- |
| `GetDemoCustomerRecordsAsync(filter?)` | `GET demo/business-records/customers` | Returns `null` on 401/403 |
| `GetDemoCustomerRecordAsync(businessKey)` | `GET demo/business-records/customers/{businessKey}` | Returns `null` on 401/403/404 |

Base address already includes `/api`.

## Filters

`DemoCustomerRecordListFilter`:

| Field | Query | Purpose |
| --- | --- | --- |
| `Q` | `q` | Search business key / display name / email |
| `SourceKind` | `sourceKind` | e.g. `file_import`, `api_polling` |
| `HasConflict` | `hasConflict` | Soft-conflict filter |
| `Skip` / `Take` | `skip` / `take` | Page over projected results |

## Response shape

- **Page:** `DemoCustomerRecordPageDto` — `items`, `totalCount`, `skip`, `take`
- **Record:** `DemoCustomerRecordDto` — business-first fields + sources + conflicts + `primaryConflictSummary`
- **Source:** `DemoCustomerRecordSourceDto` — observation + provenance ids + `sourceKindLabel`
- **Conflict:** `DemoCustomerRecordConflictDto` — field, values by source, message

## API polish (additive)

| Change | Why |
| --- | --- |
| `sourceKindLabel` on each source | Stable UI labels (“File import”, “API polling”) |
| `primaryConflictSummary` on record | One-line list/detail conflict hint |
| Deterministic conflict ordering by field | Stable UI |
| Provenance labels use display label | Friendlier drill-through copy |

List empty shape unchanged: `items=[]`, `totalCount=0`, echoed `skip`/`take`.

## Demo scope

- Sample modules only (`Sample.FileImport`, `Sample.ApiPolling`)
- Auth: authenticated + `integration.read` @ instance
- Read-only projection over operation items — no persistence of business entities

## Next

- **BDS-5** — Demo Customers UI (list/detail, business-first, drill-through to Affected records / runs)
- Still no platform entity model, Systems/Services, or conflict resolution

## Implementation anchors

| Piece | Path |
| --- | --- |
| Client DTOs | `Apperture.Management.Client/DemoBusinessRecordDtos.cs` |
| Client methods | `IManagementClient` / `ManagementClient` |
| API polish | `DemoBusinessRecordProjector` |
| Tests | `ManagementClientDemoBusinessRecordTests`, `DemoBusinessRecordProjectionTests` |
