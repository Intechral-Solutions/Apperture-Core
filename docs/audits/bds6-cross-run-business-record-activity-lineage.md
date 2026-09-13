# BDS-6 — Cross-Run Business Record Activity and Lineage

**Status:** Implemented (automated); operator smoke pending  
**Date:** 2026-08-05  
**Related:** [BDS-5](./bds5-demo-customers-surface.md), [UIX-3](./uix3-affected-record-anchors-return-context.md), [UIX-4](./uix4-contextual-breadcrumbs-link-integrity.md), [readiness](./uix3-readiness-bds6-decision.md), [roadmap](../00-overview/pre-release-product-roadmap.md)

---

## 1. Executive summary

BDS-6 adds a customer-first **Activity** surface for Demo Customers: chronological, cross-run operation-item history spanning FileImport and ApiPolling. Entries deep-link into Run Detail via UIX-3 helpers and return with UIX-4 contextual breadcrumbs. This is **not** a generic Operation Item Explorer.

## 2. Operator problem addressed

> What has happened to this business record across integrations and runs?

## 3. Existing capabilities reused

- Demo Customer projection (`IDemoBusinessRecordProjectionService`, metadata `recordType=customer`)
- Operation items/groups, run + integration joins
- UIX-3 `AffectedRecordNavigation` / typed `from`+`fromKey`
- UIX-4 contextual breadcrumbs (unchanged contract)
- `integration.read` authorization

## 4. Final data/query model

Server query over `OperationItems` ∪ `OperationGroups` ∪ `OperationRuns` ∪ `Integrations`, scoped to demo modules and `SubjectKey == businessKey`, with metadata confirmation (`recordType=customer` + metadata business key).

## 5. Business-key relationship strategy

| Source | Role |
| --- | --- |
| `OperationItem.SubjectKey` | Primary SQL filter (indexed) — sample modules set this to the customer business key |
| Metadata `businessKey` / `recordType` | Confirmation that the item is a customer observation |
| Projection sources | Customer existence gate (404 if never projected) |

Limitation: activity requires SubjectKey alignment with the demo convention; orphan metadata without SubjectKey match is not listed.

## 6. API contract

```text
GET /api/demo/business-records/customers/{businessKey}/activity
  ?sourceKind=&status=&attentionOnly=&q=&lookbackDays=&skip=&take=

GET /api/demo/business-records/customers/{businessKey}/activity/summary
```

Client: `GetDemoCustomerActivityAsync` / `GetDemoCustomerActivitySummaryAsync`.

## 7. UI route and presentation

- Detail summary: **Record activity** + View activity  
- Full page: `/demo/customers/{businessKey}/activity`  
- Chronological list (newest first), progressive disclosure for IDs  
- Breadcrumbs: Config / Developer / Demo Customers / {key} / Activity  

## 8. Filters and paging

Source, outcome, needs-attention, search, lookback (7/30/90/all). Skip/take paging (default 25, max 100) with Load more.

**OccurredAt:** metadata `observedAt` when present, else `CompletedAtUtc ?? CreatedAtUtc`. Secondary sort: `OperationItemId` DESC.

## 9. Precise drill-through

`AffectedRecordNavigation.BuildDemoCustomerSourceHref` with item → group → run priority and typed return context.

## 10. Authorization

Same as demo projection: authenticated actor + `integration.read` at instance scope. Context does not authorize.

## 11. Performance / indexing

- Filter by SubjectKey before in-memory search/refinement  
- Index: `IX_OperationItems_SubjectKey` (migration `20260805120000_Bds6_OperationItemSubjectKeyIndex`)  
- Bounded page size; no browser-side run enumeration  

## 12. Automated test results

| Suite | Result |
| --- | --- |
| `DemoBusinessRecordActivityTests` | **7 passed** |
| Shared `Bds6\|Uix4\|Uix3\|Bds5` | **68 passed** |

## 13. Runtime smoke

Operator checklist in epic DoD — **not executed in this agent session**.

## 14. Known limitations

- Demo Customer only (FileImport / ApiPolling)  
- SubjectKey-centric matching  
- No conflict resolution / editing  
- Lookback uses UTC wall clock  

## 15. UIX-5 handoff

**UIX-5 complete** — [`uix5-supporting-surface-pattern-audit.md`](./uix5-supporting-surface-pattern-audit.md). Activity remains a dedicated route; no drawer framework. UIX-5 deferred shared panels; standardize technical `<details>` via UIX-6.

## 16. Longer-term opportunities

Additional entity types, lineage graphs, reconciliation, cross-integration workflows — deferred until a second entity proves reuse.
