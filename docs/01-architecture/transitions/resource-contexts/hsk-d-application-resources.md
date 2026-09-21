# HSK-D — Application Resources

Date: 2026-09-21
Status: **Implemented and validated.** Operator browser smoke passed for the exercised scenarios;
restricted-operator authorization was not manually exercised and is covered by automated tests (see §7-§9).
Supersedes/completes: the unshipped always-visible Resources section of
[`ctx3-design-gate.md` §6.4](./ctx3-design-gate.md). That gate is historical and is not rewritten; it never
shipped a persistent Resources section (only the archive-blocked warning linked to the scoped lists), and this
work delivers a richer searchable/filterable one instead.

---

## 1. What it is

An always-visible **Resources** section on Application detail (`/config/applications/{Id}`) that answers *"what
resources belong to this Application?"* Rows show the resource kind, its human-readable name (a link to its own
detail page) and its current state.

**Shared uses the same explorer.** The reserved Shared context is a real ResourceContext that owns operational
resources, and it is served by the same page, the same `ApplicationResources` component, the same endpoint and
the same presentation helpers as an Application. Only the owner wording differs (`this application` / `Shared`).
There is no Shared-specific implementation to maintain; the two branches of `ApplicationDetail.razor` call one
`ResourcesSection(...)`. (Product ruling, 2026-09-21: keep it.)

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
  "Showing the first N of M" instead of pretending to be complete. There is no cross-family paging. Shared can
  own far more than an Application, so each load there builds Shared's whole row set (still one Context,
  filtered in SQL) before applying the cap; a server-side paged query is the future path if that ever matters.

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
- **Overview count links are the first same-page fragment links in the app.** The client shell has
  `<base href="/">`, so a bare `#application-resources` href would resolve to the home route and navigate away
  (a known Blazor behavior). The link is therefore the current page URL plus the fragment
  (`ApplicationResourcesPresentation.AnchorHref`, tested). Whether Blazor then scrolls to the fragment is
  confirmed only by the browser smoke.
- **Fail-closed can read as empty.** If capabilities cannot be computed (deny-all) or a typed service denies, the
  endpoint returns an empty 200. The page does not show "No resources belong…" in that case for capability
  failures, because it gates on the same capabilities first (forbidden or error state); a mid-request denial
  would show the empty state. Accepted: it discloses nothing.
- **`SegmentedControl` arrow keys select but do not move focus** (existing behavior, not changed here).
- **Inventory hrefs are unescaped** (`/integrations/{id}`), as elsewhere; resource ids are not user text.

## 7. Authorization

There is no HSK-D permission. Each family is gated by the same permission as its ordinary surface:

| Family | Gate in the Resources query | Same as the ordinary surface |
|---|---|---|
| Integrations | `caps.CanViewIntegrations` = `integration.read`, then `IntegrationManagementService.ListIntegrationsAsync` re-checks it | Integrations list |
| Connections | `caps.CanViewConnections` = `connection.read`, then `ConnectionManagementService.ListConnectionsAsync` re-checks it | Connections list |
| Schedules | `caps.CanManageSchedules` = `schedule.manage` via `IScheduleGovernanceService.CanManageAsync` (the typed schedule service does not re-check; this is the same single gate `SchedulesController.List` uses) | Schedules list |

A family the caller cannot read contributes no rows, names, ids or states, and nothing to `TotalMatching`. A
request for a hidden family (`type=`) returns an empty result, not an error. If capabilities cannot be computed
the capability service returns deny-all, so every family is omitted (fail closed). The client offers only the
type filters the operator's capabilities allow; that reflects the operator's permissions, not whether hidden
resources exist. The overview counts come from `GET api/resource-contexts/summary`, which masks each family per
caller.

Automated evidence:
- `ResourceContextResourcesControllerTests.Get_WithAFamilyTheCallerCannotRead_ExposesNothingAboutThatFamily`
  (theory over integration, connection and schedule: whole list, direct `type=` request, and search) and
  `ApplicationResourcesQueryTests.UnauthorizedFamily_IsOmitted_WithoutLeakingItsRowsOrCounts`.
- `InventoryQueryServiceTests.Missing_capability_omits_type_without_calling_list` and
  `Unauthorized_list_omits_that_type_only` (a family whose capability is missing is never queried; one whose
  service throws `UnauthorizedAccessException` is omitted alone).
- `ResourceContextsControllerTests.Summary_MasksCountsTheCallerCannotRead` and
  `Summary_ExposesOnlyTheFamiliesTheCallerCanRead` (overview counts).
- `ApplicationResourcesPresentationTests`: `TypeOptions_OmitFamiliesTheOperatorCannotRead` and
  `LiveCounts_OmitFamiliesMaskedFromTheCaller_SoNothingLeaks`.

## 8. Browser smoke checklist

**Setup.** Restart the dev stack from this branch (a running stack keeps executing old binaries), then sign in as
a fully-authorized operator. Data:
- **Application A** (active): one Integration, one Connection, one Schedule; the Integration bound to a **Shared**
  Connection.
- **Application B** (active): one Integration.
- **Application C** (archived): retire its Integration and Connection, disable its Schedule, then archive it.
- **Application D** (active): no resources.

**Basic list (A).**
1. A's detail page has a **Resources** section below the details.
2. All three of A's resources are listed with human-readable names and no ids.
3. Each name opens the correct detail page.

**Ownership boundary.**
4. The Shared Connection used by A's Integration is not listed.
5. B's Integration is not listed under A.

**Overview counts.**
6. The counts line keeps its meaning (integrations not retired, active connections, enabled schedules).
7. A non-zero count scrolls to Resources and preselects that family (first hash-only link in the app).
8. A zero count is plain text, not a link.

**Search.**
9. Searching part of the Integration's name keeps it and hides the rest, after a short pause rather than on every
   keystroke.
10. Clearing the search restores everything.
11. Repeat with the Connection's name.

**Type filter.**
12. Cycle All, Integrations, Connections, Schedules, All.
13. Select Connections and search within it: both constraints apply.

**State display.**
14. States use the family's own words (Integration Active/Inactive/Retired, Connection Active/Retired, Schedule
    Enabled/Disabled).
15. There is no state filter (deliberately deferred, §4).

**Archived Application (C).**
16. The retired Integration, retired Connection and disabled Schedule are still listed.
17. There is no "Create…" button in the Resources section.

**Empty and no-match states.**
18. D shows "No resources belong to this application yet."
19. On A, searching gibberish shows "No matching resources" with **Clear filters**, distinct from #18.

**Focus and keyboard.**
20. Search "zzz" (no match), then backspace to empty: the search box keeps keyboard focus.
21. Select a family with no rows, then click **All**: focus stays on the control.
22. Search, type filter and links are all reachable by keyboard.

**Shared page.**
23. The Shared page (from the Applications list) has the same Resources section and behaves the same.

**Authorization (optional; manual only if practical).**
24. As an operator who cannot read one family, that family's rows, count and filter option are absent.

**Routing.**
25. A `?context=` link from the archive-blocked warning still uses the slug. The Resources section itself emits no
    Context-bearing links, and its API calls carry the canonical id.

**Appearance and responsive.**
26. Dark theme, light theme, and a narrow phone-width window: search and filter wrap, names wrap, no horizontal
    scrolling.

## 9. Execution status (2026-09-21)

Recorded from the operator's report; results were not itemized per check, and nothing here is inferred beyond it.

- **Manually exercised and passed (reported in aggregate):** the operator reported exercising nearly all of the
  checklist, including the functional, ownership, search/filter, archived, empty-state, routing, appearance and
  responsive scenarios, with passing results.
- **Partially exercised:** none identified by the operator. Which individual checks, if any, were skipped was not
  enumerated, so a check not individually confirmed should be treated as covered only by the aggregate report.
  Two behaviors could not be verified statically and rest on that aggregate report alone: the count-link scroll
  (#7) and focus retention while filtering (#20-#21).
- **Not manually exercised:** restricted-operator authorization (#24).
- **Covered instead by automated tests:** authorization and non-leakage (§7), ownership scoping including the
  Shared-referenced exclusion, archived Applications, search and type-filter composition, `TotalMatching`
  truncation, presentation rules, and structural guards for the component (canonical-id API calls, no Context
  selector/column, no create CTA, retained controls and live region). Server semantics run against the real
  Inventory pipeline over the in-memory store; the underlying SQL Context filters are the existing repository
  queries already exercised by the SQL-backed integration tests.
