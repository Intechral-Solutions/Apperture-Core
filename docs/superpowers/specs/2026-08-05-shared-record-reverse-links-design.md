# Shared-record reverse links (Affected records → business record)

**Date:** 2026-08-05  
**Status:** Approved  
**Related:** BDS-5/BDS-6 Demo Customers, UIX-3/4 navigation, `OperationItemBusinessMetadataKeys`

**Supersession note (SDK-7, 2026-08-06):** Modules now author identity via typed `SharedRecordIdentity` (platform writes metadata + normalized columns). Reverse-link detection still uses metadata/`SubjectKey` compatibility rules in `SharedRecordLinkCatalog` (Ui.Shared cannot reference ModuleSdk.Integration). Cross-integration reference lookup is application-owned — see [`../../audits/sdk7-cross-integration-shared-record-identity-lookup.md`](../../audits/sdk7-cross-integration-shared-record-identity-lookup.md). Platform `BusinessRecord` remains deferred.

## Problem

Operators see FileImport/ApiPolling affected records on Run Detail but cannot navigate to the Demo Customer hub. Reverse navigation (run item → shared record) is missing.

## Decision

**Approach A — metadata-driven catalog**

- Detect shared records from operation-item `MetadataJson` (`recordType` + `businessKey`), with `SubjectKey` as business-key fallback when metadata key is absent (same as `DemoBusinessRecordProjector`).
- Resolve links through an app-owned `SharedRecordLinkCatalog` keyed by `recordType`.
- First registration: `customer` → Demo Customer detail (`/demo/customers/{key}`), label **Open demo customer**.
- Use `AffectedRecordNavigation.WithoutContext` so run `from`/`fromKey` do not leak onto the customer page.
- No existence API pre-check; no SubjectKey-only guessing without `recordType`; no platform `BusinessRecord` model.

## Extension seam (later entity platform)

```text
SDK SharedRecordIdentity
  → persistence / compatibility reader
  → SharedRecordReference lookup (application)
  → SharedRecordLinkCatalog (UI destinations only)
  → Demo Customer detail today
  → future /records/{type}/{key} (or similar) later
```

New entity types add catalog registrations. Do not invent Entity 360 or module-contributed entity definitions in this change.

## UI

`RunOperationItemizationPanel`: show a plain anchor in the item actions area when the catalog resolves a link. Target is **detail**, not Activity.

## Out of scope

Platform entity tables, existence verification batching, additional record types, drawers/panels (UIX-5), arbitrary return URLs.
