# UIX-3 Readiness and BDS-6 Necessity Audit

**Status:** Accepted (2026-08-05)  
**Date:** 2026-08-05  
**Scope:** Read-only planning audit — decision accepted; implemented by UIX-3  
**Related:** [pre-release product roadmap](../00-overview/pre-release-product-roadmap.md), [BDS-1](./bds1-business-data-surface-design-audit.md), [BDS-5](./bds5-demo-customers-surface.md), [UIX-2](./uix2-breadcrumb-shell-stabilization.md), [UIX-3 closure](./uix3-affected-record-anchors-return-context.md), [Epic 101](../02-decisions/Epic101-Top-Bar-Breadcrumbs-and-Parent-Context-Links.md)

---

## Acceptance note

Architectural review **accepted** the recommendation:

> Proceed with UIX-3 and include the named small prerequisites. Do not implement a separate BDS-6 epic first.

UIX-3 implementation: [`uix3-affected-record-anchors-return-context.md`](./uix3-affected-record-anchors-return-context.md).  
BDS-6 remains optional for **cross-run** affected-record exploration when the operator does not already have a specific `RunId`.

The remainder of this document is the original readiness analysis (kept for history).

---

## 1. Executive conclusion

**Proceed with UIX-3 and include named small prerequisites; no separate BDS-6 epic is needed.**

UIX-3 can deliver a useful Demo Customer → Run Detail (Affected records) → return-to-customer workflow using capabilities that already exist:

| Layer | Already present |
| --- | --- |
| Demo Customer projection/API | `DemoCustomerRecordSourceDto` carries `RunId`, `OperationGroupId`, `OperationItemId`, `BusinessKey`, `IntegrationId`, module/kind provenance |
| Demo Customer UI | List `/demo/customers`, detail `/demo/customers/{businessKey}` with “Open run” / “Open integration” |
| Run Detail | `RunDetail.razor` hosts `RunOperationItemizationPanel` (“Affected records”) |
| Groups / items API | `GET /api/runs/{id}/operation-groups`, `GET /api/runs/{id}/operation-groups/{groupId}/items` (paged; `status`, `retryableOnly`, `q`) |
| Stable IDs | `OperationGroupId` / `OperationItemId` are durable store keys; group rows expose `data-operation-group-id` |
| Nav seam | Empty `ManagementBreadcrumbNavigationContext` + path+query observation in `ManagementShellOrientationTracker` |

What is missing is **presentation and URL wiring** (expand group, focus item, encode return context)—not a new business-data surface or cross-run explorer.

BDS-6, as described in BDS-1 Option A (“Operation Item Explorer” / cross-run activity filter), is a different product capability and should stay optional/deferred. Finer in-run anchors belong in UIX-3, not BDS-6.

---

## 2. Existing capability map

| Area | What exists | What is missing |
| --- | --- | --- |
| **Demo Customer source surface** | `DemoCustomersList.razor`, `DemoCustomerDetail.razor`; routes `DemoCustomerUiRoutes`; API `DemoBusinessRecordsController` | No link builder that encodes group/item/return query params |
| **Provenance links** | UI: Open run → `/runs/{runId}`; Open integration when present. Technical `<details>` shows `operationItemId` / `operationGroupId` as text only | No “Open affected record” deep link; copy still says anchors are deferred |
| **Provenance identifiers (model)** | `DemoCustomerRecordSourceDto`: `SourceKind`, `ModuleId`, `IntegrationId`, `OperationKind`, `OperationItemId`, `OperationGroupId`, `RunId`, `BusinessKey`, display/email/status, `ChangeKind`, `ObservedAtUtc`, `ProvenanceLabel` | Conflict DTOs are field/value only (no run/item pointers)—acceptable for UIX-3 |
| **Run Detail** | `Pages/Runs/RunDetail.razor`; itemization summary on run DTO | No query-param consumption for group/item/from |
| **Operation groups** | Panel loads all groups via `IManagementClient.GetRunOperationGroupsAsync`; expand toggles `_expandedGroupId` | No URL-driven auto-expand; no public expand API beyond click |
| **Operation items** | Paged load (`DefaultItemPageSize` = 50), status filters, Load more, failed-first ordering, retry UI; rows: `data-testid="operation-item"` | No `data-operation-item-id`; no highlight/scroll; UI does not pass `q` to API; no focus-by-item-id |
| **API retrieval** | List groups; list items by group with filters; store `GetItemAsync` used by retry path | **No** Management `GET …/operation-items/{itemId}` summary endpoint; `q` matches `SubjectKey`/`DisplayLabel` only (not `OperationItemId`) |
| **Query / anchor support** | Tracker observes path+query; crumbs strip query for trail rules | No shared query schema; panel ignores URI |
| **Return-context seam** | `ManagementBreadcrumbNavigationContext` empty record; resolver overload ignores it (documented UIX-2/3/4 seam) | No typed fields; no page-level “Back to customer” control driven by `from` |
| **Automated tests** | `Bds5DemoCustomersSurfaceTests`, `Sdk3D`/`Sdk4C` itemization helper/source tests, `OperationItemizationQueryServiceTests` (incl. `q` + paging), `Uix2*` / `Epic101*` / `AuthShell*` | No URL parse/build helpers tests; no panel focus/expand-from-query tests; no return-context tests |

---

## 3. Proposed end-to-end URL flow

Use **query parameters on existing routes** (not a history store). Names below are recommendations; finalize in UIX-3 implementation (prefer a single shared helper).

### Happy path

```text
1) Origin
   /demo/customers/C-100

2) Leave customer for a precise affected record
   /runs/{runId}?group={operationGroupId}&item={operationItemId}&from=/demo/customers/C-100&fromKey=C-100

3) UIX-3 Run Detail behavior (not UIX-4 breadcrumbs)
   - Route-derived crumbs remain: … → Runs → {runId}   (path only)
   - Panel expands group={operationGroupId}
   - Loads/pages (or q=business key) until item={operationItemId} is present
   - Scrolls/highlights that row
   - In-page control: “Back to demo customer” → /demo/customers/C-100
     (plain <a href> / NavigateTo; no click-intercepted crumbs)

4) Deeper links from the focused item (child retry run, etc.)
   Preserve from/fromKey on outbound run links constructed in this session
   when still within the same investigation; drop when starting an unrelated workflow
   (e.g. global nav to Schedules).
```

### Malformed / stale context

| Condition | Behavior |
| --- | --- |
| Missing/unknown `group` | Load Run Detail normally; optional muted notice “Affected-record link could not be opened” |
| Known group, missing `item` | Expand group; show notice; no highlight |
| `from` not under `/demo/customers` (or fails allowlist) | Ignore return control; do not navigate arbitrarily |
| Item deleted / group empty | Expand if possible; empty/notice — no explorer fallback |

### UIX-3 vs UIX-4

| Concern | UIX-3 | UIX-4 |
| --- | --- | --- |
| Trail in top bar | Unchanged route-derived spine | May enrich crumbs from validated `ManagementBreadcrumbNavigationContext` |
| Return affordance | Explicit in-page / toolbar link from `from` | Possibly reflected in parent crumb label/href |
| Query observation | Pages + panel react; tracker already sees path+query | Resolver may consume typed context |

---

## 4. Gap classification

### A. Already sufficient

- Demo Customer list/detail and projection fields for run/group/item/business key/integration.
- Run Detail + Affected records panel (groups, items, status filter, load more, retry).
- `GET …/operation-groups` and `GET …/operation-groups/{groupId}/items` with paging.
- Server-side `q` filter on subject key / display label (`SqlOperationItemStore.QueryItemsByGroupAsync`).
- Store-level `GetItemAsync` (used by retry; proves ID lookups exist internally).
- Sticky shell / single WASM mount / plain breadcrumb anchors (UIX-2) — do not reopen.
- Path+query observation key for later contextual reevaluation.

### B. Small UIX-3 prerequisites (belong inside UIX-3)

1. **Central link helpers** — e.g. extend `DemoCustomerUiHelpers` / new `AffectedRecordNavigation` helper: build run URLs with `group`, `item`, `from`, `fromKey`; parse/validate query; strip/replace context.
2. **Demo Customer “Open affected record”** (or upgrade “Open run”) using those helpers; keep operator copy free of epic/track labels.
3. **`RunOperationItemizationPanel` URL focus** — accept optional `group`/`item` (via parameters from `RunDetail` reading `NavigationManager` URI); auto-expand; locate item (page/`q` by `BusinessKey`/`SubjectKey`, and/or scan pages for `OperationItemId`); scroll + visual highlight; graceful miss.
4. **Row identity attribute** — e.g. `data-operation-item-id="@item.OperationItemId"` (and optional element `id`) for scroll targets.
5. **Wire existing `q`** when focusing by business key (API already supports; UI currently never sends `Q`).
6. **Optional tiny API polish (only if page-scan proves brittle):** extend item `q` to match `OperationItemId`, **or** add `GET /api/runs/{id}/operation-items/{itemId}` returning summary (+ group id) using existing `GetItemAsync` + authz. This is still a UIX-3 prerequisite, not BDS-6.
7. **In-page return control** on Run Detail when `from` is a validated demo-customer path.
8. **Propagate `from`/`fromKey`** on panel-emitted deeper run links during the same investigation.
9. **Shared unit tests** for parse/build/validate + source-inspected panel/detail wiring; keep `Uix2*`/`AuthShell*` green.

### C. Substantive BDS-6 capability (not required for UIX-3)

- Dedicated Operation Item Explorer (cross-run activity browser).
- New projection/index for “all items for business key across runs.”
- Standalone item-detail page / reusable investigation workspace beyond run-scoped anchoring.
- Cross-integration lookup / Systems model (SYS-1).
- Conflict resolution or write paths.

### D. Deferred UIX-4 or later

- Contextual breadcrumb replacement / enrichment via `ManagementBreadcrumbNavigationContext` fields.
- Generalized module-contributed navigation contexts.
- Panel/drawer navigation framework (UIX-5).
- Broad link-integrity framework across the whole Management UI.
- Unrelated Run Detail redesign.

---

## 5. Recommended UIX-3 scope

### Deliverables

1. Shared URL encode/decode helpers for affected-record anchors + return context (allowlisted `from`).
2. Demo Customer source links that deep-link to run + group + item + return.
3. Run Detail / `RunOperationItemizationPanel` consume those query params: expand, focus, highlight, miss handling.
4. In-page return to originating demo customer when context valid.
5. Preserve/drop rules for context on deeper run links vs global navigation.
6. Tests + operator smoke for the Demo Customer workflow.
7. Short closure note updating this audit / UIX-2 smoke checklist as closed if not already.

### Explicit exclusions

- Contextual top-bar breadcrumb rewriting (UIX-4).
- Operation Item Explorer / cross-run item browser (BDS-6).
- New platform `BusinessRecord` table or Records IA.
- Prerender / dual `App` mount / click-intercepted crumbs / MainLayout menu `Changed` dual-subscribe (UIX-2 invariants).
- Planning labels in operator-facing copy.

### Likely projects / files

| Project | Likely touchpoints |
| --- | --- |
| `Apperture.Management.Ui.Shared` | New/extended helpers; optionally typed fields on `ManagementBreadcrumbNavigationContext` **only if** needed for parse sharing (prefer not driving crumbs yet) |
| `Apperture.Management.Ui.Client` | `DemoCustomerDetail.razor`, `RunDetail.razor`, `RunOperationItemizationPanel.razor` |
| `Apperture.Management.Client` | Optional client method if GET-by-item-id is added |
| `Apperture.Management.Api` / `ControlPlane.Security` | Optional GET-by-item-id only |
| `Apperture.Management.Ui.Shared.Tests` (+ unit tests as needed) | Parse/build, link surface, panel focus contracts, UIX-2 regression filter |

### API changes

- **Default:** none — use existing group item list (+ `q` and/or client-side page scan for `OperationItemId`).
- **Optional narrow add:** GET single item by id under run, or include `OperationItemId` in `q` match.

### Client changes

- Query read on Run Detail; parameters into itemization panel.
- Link construction on Demo Customer sources.
- Highlight CSS + scroll-into-view; return control.

### Test requirements

- Helper: round-trip encode/decode; reject unsafe `from`.
- Detail source links contain `group`/`item`/`from`.
- Panel: expand from `group`; highlight when item present; notice when absent.
- Query-only URI change still observed by location tracker (existing UIX-2 tests remain green).
- No new click-intercept / `@key` orientation remount / dual mount regressions (`Uix2ManagementBreadcrumbStabilizationTests`).

### Runtime smoke workflow

1. Seed/demo path with a multi-source customer (e.g. conflict or dual FileImport/ApiPolling).
2. Demo Customers → customer detail → Open affected record (or upgraded Open run).
3. Confirm Affected records expands correct group, focuses item, Back returns to same customer.
4. Browser Back/Forward between customer and run preserves expected URLs.
5. Console remains clean under UIX-2 rules (ignore Shift+Alt+D only).

### Definition of done

- Primary workflow works without a BDS-6 explorer.
- Malformed context fails soft.
- Route-derived breadcrumbs unchanged.
- Automated tests above pass; smoke checklist signed off.
- Operator copy remains product-like (no epic/track IDs).

---

## 6. BDS-6 recommendation

**Retain as an optional future epic; do not implement before UIX-3; do not absorb into UIX-3.**

**Narrow definition (user redefine if kept on the roadmap):**

> **BDS-6 — Cross-run affected-record explorer (optional):** From a demo customer (or integration), browse operation items **across multiple runs** with filters (time, status, module), without requiring the operator to already know a single `RunId`. Not required for deep-linking into a known run’s Affected records panel.

**Do not** keep BDS-6 merely as “finer anchors”—that work is UIX-3.

**Pending architectural review:** Roadmap Track 5 currently labels BDS-6 as *(next)*; Track 1 correctly lists UIX-3 as next after UIX-2. After review, Track 5 should say UIX-3 precedes optional BDS-6.

---

## 7. Roadmap update

**Recommended next sequence (pending architectural review — do not treat as applied):**

```text
[done] BDS-1 … BDS-5
[done] UIX-1 (design intent)
[done] UIX-2 (shell/breadcrumb stabilization + operator smoke)
[next] UIX-3 — Affected-record anchors and return context (incl. small prerequisites in §5)
[then] UIX-4 — Contextual breadcrumbs and link integrity
[later] UIX-5 — Supporting panel / overlay pattern audit
[optional] BDS-6 — Cross-run affected-record explorer (only if operators still need multi-run item browse after UIX-3/4)
```

Avoid lettered sub-epics unless a prerequisite grows into a separate durable API product (unlikely for GET-by-item-id).

---

## 8. Risks and uncertainties

| Uncertainty | Evidence gap | Impact |
| --- | --- | --- |
| Typical item count per demo group | Fixtures suggest small pages; production-like volume unknown | If groups routinely ≫ 50 items and `q` by subject is insufficient, prefer optional GET-by-item-id inside UIX-3 |
| Whether `SubjectKey` always equals demo `BusinessKey` | BDS tests assert alignment for sample modules; not a platform invariant | Prefer anchoring by `OperationItemId` when present; use `q` as assist |
| Exact query parameter names | No existing convention beyond list filters (`q`, tabs) | Choose one helper as source of truth in UIX-3 |
| UIX-1 standalone audit file | Roadmap points at BDS-1 §8–9 + Epic 101; no `uix1-*.md` | Documentation inconsistency only |
| Doc drift | BDS-5 “Next” still lists UIX-1 and “finer anchors” under BDS-6; UIX-2 audit may still say smoke open | Update when closing UIX-3 planning—not blockers for the decision |
| Browser Back with query-only focus | Tracker observes query; panel must re-apply focus on `LocationChanged` | Implementation detail for UIX-3; not a BDS-6 need |

---

## Planning vs code inconsistencies (summary)

1. **Who is “next”:** Roadmap Track 5 says BDS-6 *(next)*; Track 1 and this audit say **UIX-3**. Prefer Track 1 + this decision after review.
2. **BDS-5 deferred anchors:** Detail UI still states finer Affected-record anchors are deferred — true until UIX-3; BDS-6 wording over-claimed that gap.
3. **BDS-1 Option A vs UIX-3:** BDS-1 correctly separates explorer (A) from run-scoped itemization (B); later text blurred “finer anchors” into BDS-6.
4. **UIX-1 artifact:** Marked complete without a dedicated audit file; intent lives in BDS-1 / Epic 101.
5. **`ManagementBreadcrumbNavigationContext`:** Placeholder suitable for UIX-4 crumb enrichment; UIX-3 return context should live primarily in **URL + page UI**, not crumb mutation.

---

## Primary decision answer

> Can UIX-3 deliver a useful and technically sound Demo Customer → affected run/operation/item → return-to-customer workflow using UI, API, projection, and itemization capabilities that already exist?

**Yes.** Projection already exposes the IDs; run-scoped Affected records already loads groups/items; gaps are link construction, query-driven expand/focus, and return affordance (category **B**). A separate BDS-6 explorer is **not** a prerequisite.
