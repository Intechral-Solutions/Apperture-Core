# HSK-D — Application Resources

Date: 2026-09-21
Status: **Implemented and validated; manual browser smoke pending** (see §7).
Supersedes/completes: the unshipped always-visible Resources section of
[`ctx3-design-gate.md` §6.4](./ctx3-design-gate.md). That gate is historical and is not rewritten; it never
shipped a persistent Resources section (only the archive-blocked warning linked to the scoped lists), and this
work delivers a richer searchable/filterable one instead.

---

## 1. What it is

An always-visible **Resources** section on Application detail (`/config/applications/{Id}`) that answers *"what
resources belong to this Application?"* Rows show the resource kind, its human-readable name (a link to its own
detail page) and its current state. The same section renders on the reserved Shared context's detail page,
because the component and endpoint are Context-agnostic; that is a deliberate small extension, not a
requirement.

Terminology is **Resources** / **Application resources**. "Bound" is not used: *Binding* already means
Integration → Connection in this product.

## 2. Ownership semantics

The list is **current ownership**: `resource.ResourceContextId == context.Id`, for three families.

| Family | Listed states | State vocabulary (deliberately not normalized) |
|---|---|---|
| Integration | every state | Active, Inactive, Retired |
| Connection | every state | Active, Retired |
| Schedule | enabled and disabled | Enabled, Disabled |

- **Referenced is not owned.** A Shared Connection bound by an Application Integration belongs to Shared and is
  not listed under the Application. (A future "Shared resources in use" view is a separate concept.)
- **Archived Applications** still list what they currently own (Retired Integrations, retired Connections,
  disabled Schedules). No create affordance appears in the section; creation rules are unchanged.
- **Not listed:** Runs, Audit events, Notification definitions, Modules, System/Not-recorded pseudo-contexts,
  and Components. History and current ownership stay separate.

## 3. Architecture and the Config Inventory relationship

**Selected: reuse of the Inventory resource read model, behind its own endpoint.**

`GET api/resource-contexts/{id}/resources?type=&q=` (`ResourceContextResourcesController`) calls
`InventoryQueryService` with a **fixed Context** and a **three-family restriction**. Two small, additive
extensions were made to that query service (`InventoryQuery.Types`, `InventoryQueryResult.TotalMatching`); the
public `api/inventory` contract and the Config Inventory page are unchanged.

Shared: the normalized resource projection, per-family authorization (a family the caller cannot read is
omitted, never counted), SQL-side Context filtering, name/id/type-label search, sorting.
Separate: the endpoint and its DTOs, the page, and the presentation. Config Inventory remains a global
diagnostic/configuration explorer (System / Shared / Application / activity / not-recorded); Application
Resources is operational, fixed to one Context, and intentionally simpler. Neither is the domain model for
ownership (ADR-0040 rule 6: UI aggregation is not domain ownership). The page does not embed
`Inventory.razor`, and the endpoint exists so this view cannot be broken by a future Inventory-specific
permission or contract change.

No shared abstraction was manufactured: the "extraction" is one optional parameter and one count.

## 4. Search, filtering, paging

- One search box over the combined set. It is remote and debounced (300 ms); a latest-request-wins guard stops
  a slow earlier response overwriting a newer one. Matching is the Inventory query's: display name, id, or type
  label (case-insensitive substring).
- Type filter: All / Integrations / Connections / Schedules, offering only families the operator can read. Search
  and type compose. The overview counts preselect the same type filter; they are not a second mechanism.
- **State filtering is deferred.** The three families have different lifecycles, and a single "Active / Inactive"
  filter would be dishonest. Reusing Inventory's status filter is unsafe: it is a substring match, so filtering
  on "Active" also matches "Inactive". Family-aware state filters can be added later without an API change.
- **Bounded, not paged.** Each family list is unpaged but filtered by Context in SQL, so it is bounded by one
  Context's own resources. The response is capped at 500 rows and reports `TotalMatching`, so the UI says
  "Showing the first N of M" instead of pretending to be complete. There is no cross-family paging.

## 5. Overview counts

The counts on the overview keep their existing meaning (integrations not retired, active connections, enabled
schedules — the same as the Applications list and the archive-blocked message). Each non-zero count now links to
the Resources section with that family preselected. The list shows every state, so a family can list more rows
than its count; the state pill explains the difference.

## 6. Findings

- **Overview counts do not leak.** `GET api/resource-contexts/summary` already masks each family per caller
  (`null` when the caller lacks that family's read permission). No change needed.
- **Unlabelled live counts.** The overview and Applications list show live/active/enabled counts without saying
  so. Left as-is to keep the two pages consistent.
- **Inventory status filter is substring-based** (see §4). Reported, not changed.
- **Superseded requests are ignored, not cancelled.** A slower earlier search response never overwrites a newer
  one, but its server-side work still completes. Bounded by one Context's own resources; accepted for v1.
- **Overview count links use an in-page `#application-resources` anchor**, the first hash-only link in the
  app. Whether Blazor scrolls to the fragment is verified only by the browser smoke.
- **`SegmentedControl` arrow keys select but do not move focus** (existing behavior, not changed here).
- **Inventory hrefs are unescaped** (`/integrations/{id}`), as elsewhere; resource ids are not user text.

## 7. Validation and smoke

Automated: see the HSK-D completion report for exact totals. Server ownership semantics are tested against the
real Inventory pipeline over the in-memory store; the underlying SQL Context filters are the existing
repository queries already exercised by the SQL-backed integration tests. Presentation rules are pure helpers
with direct tests; the Razor component has structural source guards (there is no bUnit convention).

Manual browser smoke has **not** been performed in the authoring session (no browser automation, and the app
requires real sign-in). The checklist is in the completion report and must be run against a stack restarted from
this branch before HSK-D is treated as closed.
