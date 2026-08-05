# Shared-record reverse links (Affected records → business record)

**Date:** 2026-08-05  
**Status:** Approved  
**Related:** BDS-5/BDS-6 Demo Customers, UIX-3/4 navigation, `OperationItemBusinessMetadataKeys`

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
metadata (recordType + businessKey)
  → SharedRecordLinkCatalog
  → Demo Customer detail today
  → future /records/{type}/{key} (or similar) later
```

New entity types add catalog registrations. Do not invent Entity 360 or module-contributed entity definitions in this change.

## UI

`RunOperationItemizationPanel`: show a plain anchor in the item actions area when the catalog resolves a link. Target is **detail**, not Activity.

## Out of scope

Platform entity tables, existence verification batching, additional record types, drawers/panels (UIX-5), arbitrary return URLs.
