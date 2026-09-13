# SDK-7 — Cross-Integration Shared Record Identity and Lookup

**Status:** Implemented  
**Date:** 2026-08-06  
**Related:** [post-UIX reentry](./post-uix-product-roadmap-reentry.md), [BDS-6](./bds6-cross-run-business-record-activity-lineage.md), [JOB-1](./job1-failed-work-resolution-bulk-retry.md), [shared-record reverse links](../superpowers/specs/2026-08-05-shared-record-reverse-links-design.md)

---

## 1. Executive summary

SDK-7 introduces a typed **shared-record identity** (`record type` + `business key`) in the Module SDK, migrates FileImport and ApiPolling off handcrafted `recordType`/`businessKey` metadata, persists normalized columns for index-backed lookup, and provides a bounded application reference query. No platform `BusinessRecord` entity is introduced. Recommendation: **continue deferring** a platform entity model.

## 2. Operator and authoring problem

Shared customer behavior previously depended on scattered metadata conventions. Module authors had no typed contract; the platform had no reusable cross-integration reference lookup.

## 3. Final terminology

| Term | Meaning |
| --- | --- |
| `SharedRecordIdentity` | Typed pair `(RecordType, BusinessKey)` |
| `SharedRecordTypes.Customer` | Built-in proof type `customer` |
| `SharedRecordReference` | One operation-item occurrence of that identity (provenance, not the record) |
| Compatibility reader | Extracts identity from columns and/or legacy metadata |

## 4. SDK contract

Located in `Apperture.ModuleSdk.Integration` under namespace `Apperture.Core.Application.OperationItems` (same pattern as `OperationItemReporting`).

```csharp
new SharedRecordIdentity(SharedRecordTypes.Customer, customerKey)

OperationItemReporting.BuildAppendRequest(..., sharedRecord: identity)
```

Platform validates, merges `recordType`/`businessKey` into metadata, sets `SharedRecordType`/`SharedBusinessKey`, and aligns `SubjectKey`.

Record type governance for SDK-7: `customer` is a **reserved built-in constant** (`SharedRecordTypes.Customer`). Future types can add constants + catalog registrations without module-to-module coupling; no third-party runtime type marketplace.

## 5. Validation and normalization

- Record type: required, trimmed, **lowercase**, pattern `^[a-z][a-z0-9._-]{0,63}$`, max 64  
- Business key: required, trimmed, max 256, no `/ \ ? # :`  
- Comparison: record type and customer keys are **OrdinalIgnoreCase** (matches Demo Customer projection); stored business key preserves source casing after trim  
- SubjectKey must match BusinessKey (ignore-case) or be omitted (platform sets it)  
- DisplayLabel is not identity  

## 6. Persistence representation

Normalized nullable columns on `OperationItems`:

- `SharedRecordType`  
- `SharedBusinessKey`  

Index: `(SharedRecordType, SharedBusinessKey, CompletedAtUtc)`  
Migration `20260806010000_Sdk7_OperationItemSharedRecordIdentity` backfills from legacy metadata JSON (with SubjectKey fallback when `businessKey` absent).

No `BusinessRecords` table.

## 7. Backward compatibility

`SharedRecordIdentityCompatibility` reads:

1. Normalized columns when both present  
2. Else metadata `recordType` + (`businessKey` ‖ `SubjectKey`)  
3. Never SubjectKey alone without record type  

Used by Demo Customer projection. Ui.Shared reverse links mirror the same rules (cannot reference ModuleSdk.Integration — SDK-6D boundary). Metadata key constants remain for readers; authors should use the typed contract.

## 8. Shared lookup contract

`ISharedRecordReferenceQueryService.GetReferencesAsync(identity, query)`

Filters: integration, module, status, lookback, paging (max 100). Newest-first with deterministic `Id` secondary sort. Returns references, not a merged entity. Primary predicate uses normalized columns (no JSON scan on the hot path).

## 9. API exposure decision

**No Management API endpoint in SDK-7.** Consumers are application services and tests; Demo Customers / BDS-6 remain the operator surfaces. A generic HTTP API can be added when a non-demo product consumer exists.

## 10. FileImport and ApiPolling proof

Both modules attach `SharedRecordIdentity(Customer, key)` via `BuildAppendRequest`. They do not reference each other. Identical logical keys produce identical identities. Module metadata no longer includes handcrafted `recordType`/`businessKey` literals.

## 11. BDS integration

Projector identity extraction uses the compatibility reader. Activity and reverse links remain correct. Demo Customer projection stays customer-specific. `SharedRecordLinkCatalog` remains the UI route catalog only.

## 12. Authorization

Lookup requires authenticated actor with instance-scoped `integration.read` (same as Demo Customer projection). Shared identity does not grant access by itself.

## 13. Performance and indexing

Primary predicate uses normalized columns + composite index. No JSON scan on the hot path. Legacy rows are backfilled by migration. No caching. Expected scale: bounded page queries over indexed type+key; known limitation if columns are null and migration was not applied.

## 14. SDK compatibility

Additive: existing reports without `sharedRecord` remain valid. Metadata key constants remain for compatibility readers; typed identity is preferred for authors.

## 15. Automated test results

| Suite | Filter / scope | Result |
| --- | --- | --- |
| Unit | `SharedRecord*` + `Bds2BusinessMetadata*` (earlier) | **24** passed |
| Unit | SharedRecord + Bds2/3/5/6 + Job1 + Sdk6 + FileImport/ApiPolling fixtures | **56** passed |
| Management UI Shared | SharedRecord + Bds + Sdk3D/4C + Uix | **173** passed |
| Architecture | Full suite (includes `Sdk7SharedRecordIdentityBoundaries`) | **38** passed |
| Integration (SQL) | OperationItem / Sdk3A | **4** passed |
| Management UI Client | Full project build | **0 warnings / 0 errors** |

## 16. Runtime smoke results

Manual Aspire checklist (operator):

1. Apply migration `20260806010000_Sdk7_OperationItemSharedRecordIdentity`.  
2. Run FileImport + ApiPolling producing customer `C-100`.  
3. Confirm Demo Customer Sources and Activity include both integrations.  
4. Confirm affected-record reverse links and exact Run/group/item navigation.  
5. Shared lookup proven by `SharedRecordReferenceQueryServiceTests` (two modules, type scoping, case-insensitive keys, auth denial).  
6. Console gate: same UIX/JOB criteria (no `parentNode` / Symbol / unhandled render errors).  

Automated proof covers the cross-integration lookup acceptance criteria; full Aspire console smoke remains an operator checklist item when hosts are available.

## 17. Known limitations

- Only built-in type `customer` is reserved; no third-party type governance  
- Lookup requires normalized columns (backfill + new writes); metadata-only rows without migration remain readable by projection/reverse links but not by column lookup  
- Ui.Shared duplicates the metadata reader algorithm (boundary constraint)  
- No Management API for generic lookup yet  
- BDS-6 activity still uses a lightweight metadata prefilter (not the full lookup service)  

## 18. Platform BusinessRecord recommendation

**A — Continue deferring** a platform `BusinessRecord` model.

Evidence gained: two integrations intentionally share a stable type/key; lookup returns references usefully. Still insufficient: no second business entity type, no ownership/conflict story, Demo Customer projection remains adequate without a canonical entity.

## 19. Next roadmap handoff

**AUP-1 — Account, Profile, and Personal Preferences**, then **UIX-7 — Visual Language and Theme Overhaul**.

Conditional later: UIX-8 when UIX-5 trigger criteria are met. Do not replace this sequence unless a critical blocker appears.
