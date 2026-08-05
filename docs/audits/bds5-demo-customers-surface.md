# BDS-5 — Demo Customers Business Record Surface

**Date:** 2026-07-21  
**Status:** Complete  
**Prerequisites:** [bds4-demo-business-record-client-api-polish.md](./bds4-demo-business-record-client-api-polish.md)

## Purpose

First business-facing Management UI surface: **Demo Customers** list/detail, powered by the BDS-3/BDS-4 demo customer projection.

Business records are primary. Platform details (run/item/module ids) are supporting drill-through context.

This is **not** the final platform Records navigation or `BusinessRecord` entity model.

## Routes

| Page | Route |
| --- | --- |
| List | `/demo/customers` |
| Detail | `/demo/customers/{businessKey}` |

## Navigation

Entry lives under **Config → Developer & internal** (`/config/developer`) as **Demo Customers**.

Intentionally **not** in primary Operations/Automation rail — demo-scoped honesty.

## How to produce data

1. Start AppHost / Management host.
2. Run **Sample.FileImport** (e.g. valid or mixed fixture).
3. Run **Sample.ApiPolling** (`snapshot-initial` and/or `snapshot-changes`).
4. Open Demo Customers from Developer & internal tasks.
5. Shared keys (e.g. `C-001`, `C-002`) show multiple sources; `C-100` shows soft conflicts; FI-only / AP-only keys appear as single-source.

## List behavior

- Business-first columns: Customer, Business key, Email, Status, Sources, Conflicts, Last observed
- Filters: search (`q`), source kind, conflicts only, load more pagination
- Calls `GetDemoCustomerRecordsAsync`
- Auth: `CanViewIntegrations` (maps to `integration.read`)

## Detail behavior

- Primary summary: name, key, email, status, sources, conflict status, last observed
- Conflicts section with field / values-by-source / message (no resolution)
- Source cards with labels, observed fields, Open run / Open integration
- Platform ids behind `<details>` disclosure

## Conflict behavior

Soft conflicts only (email / status / displayName). Informational — no edit, no resolution, no blocking.

## Provenance / drill-through

| Link | Target |
| --- | --- |
| Open run / Open affected record | `/runs/{runId}` with optional `group`/`item`/`from`/`fromKey` (UIX-3) |
| Open integration | `/integrations/{integrationId}` when present |

**Historical (BDS-5):** Finer-grained anchors into Run Detail Affected records were deferred (open run, use that panel).  
**Update (UIX-3):** Those in-run anchors are implemented — see [`uix3-affected-record-anchors-return-context.md`](./uix3-affected-record-anchors-return-context.md).

## Limitations

- Demo modules only
- No platform `BusinessRecord` table
- No Systems/Services
- No conflict resolution / writes
- No final Records IA / panel overhaul
- No module `.Ui`

## Implementation anchors

| Piece | Path |
| --- | --- |
| Routes / helpers | `DemoCustomerUiRoutes`, `DemoCustomerUiHelpers`, `AffectedRecordNavigation` |
| List / detail | `Pages/Demo/DemoCustomersList.razor`, `DemoCustomerDetail.razor` |
| Nav card | `Pages/Config/DeveloperTasks.razor` |
| Tests | `Bds5DemoCustomersSurfaceTests`, `Uix3AffectedRecordNavigationTests` |

## Next

- **UIX-3** — Affected-record anchors and return context — **implemented** ([`uix3-affected-record-anchors-return-context.md`](./uix3-affected-record-anchors-return-context.md))
- **UIX-4** — Contextual breadcrumbs — **implemented** ([`uix4-contextual-breadcrumbs-link-integrity.md`](./uix4-contextual-breadcrumbs-link-integrity.md))
- **BDS-6** — Cross-run business-record activity and lineage — **implemented** ([`bds6-cross-run-business-record-activity-lineage.md`](./bds6-cross-run-business-record-activity-lineage.md))
- **UIX-5** — Supporting surface pattern audit — **complete** ([`uix5-supporting-surface-pattern-audit.md`](./uix5-supporting-surface-pattern-audit.md))
- **UIX-6** — Technical disclosure and confirm-action consistency (next)
- **SYS-1:** Systems/Services model design (when revisit triggers fire)
- Promote patterns to a platform entity model only after demo value is proven

**Historical note:** At BDS-5 closeout, this doc listed “finer Affected-record anchors” under optional BDS-6 and said Run Detail anchors were deferred. That deferred in-run anchoring is now UIX-3. BDS-6 was redefined as customer-first cross-run activity/lineage (not a generic Operation Item Explorer) and is now implemented.
