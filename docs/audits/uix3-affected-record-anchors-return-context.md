# UIX-3 — Affected-Record Anchors and Return Context

**Status:** Implemented  
**Date:** 2026-08-05  
**Related:** [readiness decision](./uix3-readiness-bds6-decision.md) (accepted), [UIX-2](./uix2-breadcrumb-shell-stabilization.md), [BDS-5](./bds5-demo-customers-surface.md), [roadmap](../00-overview/pre-release-product-roadmap.md)

---

## Executive summary

UIX-3 connects Demo Customer provenance to Run Detail **Affected records** using typed URL query parameters. Operators can open a precise operation group/item and return to the originating demo customer via an in-page affordance. Top-bar breadcrumbs remained route-derived in UIX-3; contextual composition is UIX-4 ([`uix4-contextual-breadcrumbs-link-integrity.md`](./uix4-contextual-breadcrumbs-link-integrity.md)).

## Accepted BDS-6 decision

Per [`uix3-readiness-bds6-decision.md`](./uix3-readiness-bds6-decision.md): **no separate BDS-6 epic before UIX-3**.  
BDS-6 remained optional for **cross-run** item exploration when no specific `RunId` is known. It was later redefined and implemented as customer-first activity/lineage — see [`bds6-cross-run-business-record-activity-lineage.md`](./bds6-cross-run-business-record-activity-lineage.md) (not a generic Operation Item Explorer).

## Final URL contract

| Param | Meaning |
| --- | --- |
| `group` | Stable `OperationGroupId` |
| `item` | Stable `OperationItemId` |
| `q` | Optional search (subject/label/item id) |
| `from` | Typed context kind (`demo-customer`) |
| `fromKey` | Validated key for that kind (business key) |

Example:

```text
/runs/{runId}?group={operationGroupId}&item={operationItemId}&from=demo-customer&fromKey=C-100
```

Return destinations are application-owned (`demo-customer` + key → `/demo/customers/{key}`). Arbitrary `from=/…` URLs are rejected.

## Context validation rules

- `from` + `fromKey` must both be present and recognized.
- `demo-customer` keys: trimmed, length 1–128, no `/ \ ? # :` or `..`.
- Unknown/malformed context: ignore return affordance; keep usable anchors if present.
- Context is never used for authorization.

## End-to-end workflow

```text
/demo/customers/{businessKey}
  → Open affected record / group / run
  → /runs/{runId}?group=…&item=…&from=demo-customer&fromKey=…
  → Affected records expands group, loads item (via q=item id), highlights + scrolls
  → Back to demo customer {key} → /demo/customers/{key}
```

## Existing capabilities reused

- Demo Customer projection DTOs (`RunId`, `OperationGroupId`, `OperationItemId`, `BusinessKey`)
- `RunOperationItemizationPanel` / group + paged item APIs
- UIX-2 shell (single WASM mount, route-derived crumbs, path+query observation)

## Small prerequisites added

- `AffectedRecordNavigation` + models in `Apperture.Management.Ui.Shared`
- Demo Customer source deep links
- Run Detail parse + return affordance + query-only `LocationChanged`
- Panel auto-expand / focus / highlight / `data-operation-item-id`
- `appertureShell.scrollElementIntoViewById`
- CSS `.ui-operation-item-anchor` / `.ui-operation-group-anchor`

## API / query changes

Extended `QueryItemsByGroupAsync` `q` matching to include **exact `OperationItemId`** (SQL + in-memory), in addition to existing `SubjectKey` / `DisplayLabel` contains.  
No new GET-by-item endpoint.

## Focus and highlight behavior

- Expand matching group; load items with `Q={OperationItemId}` when anchoring by item.
- Row: `id`, `data-operation-item-id`, outline + inset bar highlight, `tabindex` when anchored.
- One scroll-into-view per pending focus key via JS helper; clears after success.

## Context preservation / drop

| Action | Behavior |
| --- | --- |
| Deeper child-run links inside the panel | Preserve `from`/`fromKey` via `PreserveReturnContextOnRunHref` |
| Global rail / unrelated destinations | Do not append context |
| New customer investigation | New `fromKey` replaces prior context |

## Automated test results

| Suite | Filter / scope | Result |
| --- | --- | --- |
| `Apperture.Management.Ui.Shared.Tests` | `Uix3\|Uix2\|Bds5\|AuthShell\|Epic101\|Sdk3D\|Sdk4C` | **187 passed**, 0 failed |
| `Apperture.Management.Ui.Shared.Tests` | `Uix3\|Uix2\|Bds5\|AuthShell\|Epic101` (confirm) | **167 passed**, 0 failed |
| `Apperture.UnitTests` | `OperationItemizationQueryServiceTests` | **5 passed**, 0 failed |
| `Apperture.Management.Ui.Client` | build | **succeeded** (pre-existing nullable warnings only) |

SQL store `q` now matches entity `Id` (domain `OperationItemId`). Host comment reworded so UIX-2 dual-mount guard is not confused by comment text containing `<component `.

## Runtime smoke

Operator checklist (Aspire + DevTools, Preserve Log): exact item, group-only, paged retrieval via `q`=item id, query-only anchor change, malformed params, Back/Forward/refresh/rail exit. **Not executed in this agent session** — run before merge; console must stay free of UIX-2 renderer failures (`parentNode`, orphaned component, `__internal_stopPropagation_onclick`, etc.).

## Deferred

- **UIX-4:** contextual breadcrumb composition from validated return context — **implemented** ([`uix4-contextual-breadcrumbs-link-integrity.md`](./uix4-contextual-breadcrumbs-link-integrity.md)). Historical note: UIX-3 intentionally left top-bar breadcrumbs route-derived.
- **BDS-6:** cross-run business-record activity and lineage — **implemented** ([`bds6-cross-run-business-record-activity-lineage.md`](./bds6-cross-run-business-record-activity-lineage.md))
- **UIX-5:** supporting surface pattern audit — **complete** ([`uix5-supporting-surface-pattern-audit.md`](./uix5-supporting-surface-pattern-audit.md)); no shared panel yet
- **UIX-6:** technical disclosure and confirm-action consistency (next)
