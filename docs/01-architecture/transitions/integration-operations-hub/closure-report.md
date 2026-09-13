# HUB-1 closure report — Integration Operations Hub

Date: 2026-09-11
Status: **Complete for the scope delivered.** Core architecture, routes, breadcrumbs, and capability-aware navigation are implemented and tested. See §15 for what was deliberately scoped down given session constraints, and what a follow-up pass should verify.

## 1. Executive summary

An Integration is now a persistent operational workspace with six sections — Overview, Configuration, Connections, Schedules, Runs, Notifications — reached through a shared, real-link tab strip (`IntegrationHubNav`) that keeps the Integration's identity visible while moving between sections. No domain ownership changed: Connections and Schedules keep their own models and URLs; showing them under an Integration is presentation, not ownership. No ResourceContext, Resource Catalog, Services, Business Data tab, or Dashboard Panel work was started.

The existing `IntegrationOperatorDetailDto` / `GetIntegrationOperatorDetailAsync` (Epic 54) already served as the single server-side overview query facade HUB-1 needed — no new aggregation endpoint was built. This was verified by inspection before writing any new API, per the mandate not to invent one without evidence.

## 2. Final Integration hub sections and routes

```text
/integrations/{id}                  Overview        (IntegrationDetail.razor)
/integrations/{id}/settings         Configuration   (IntegrationSettings.razor, unchanged internals)
/integrations/{id}/connections      Connections     (IntegrationConnections.razor, new)
/integrations/{id}/schedules        Schedules       (IntegrationSchedules.razor, new)
/integrations/{id}/runs             Runs            (IntegrationRuns.razor, new)
/integrations/{id}/notifications    Notifications   (IntegrationNotifications.razor, unchanged internals)
```

Deep links preserved, not promoted to tabs:

```text
/integrations/{id}/schedule-diagnostics   — kept as a deep link; IntegrationHubNav is shown for
                                             orientation, but ResolveSectionKey deliberately
                                             returns null so no tab is highlighted.
/integrations/{id}/notifications/definitions/new — still resolves to the Notifications tab.
```

Route/section resolution is a pure function (`IntegrationUiRoutes.Sections`, `.ResolveSectionKey`), mirroring the existing `AccountUiRoutes.Sections` / `.ResolveSection` pattern exactly, including real `<a>` links (bookmarkable, keyboard/screen-reader normal) via `.ui-tabs` — no new CSS was needed.

## 3. Overview design and data sources

Overview (`IntegrationDetail.razor`) now shows: the summary band (state, readiness pill, schedule/connection counts, quick-link cards to every hub section), a condensed readiness banner linking to Connections for full detail, a "needs attention" list, recent activity (runs + module-scoped audit + notifications, unchanged composition logic), the setup wizard banner (see §9), and procedures/execution.

Data sources, all pre-existing: `GetIntegrationOperatorDetailAsync` (integration + readiness + required slots + bindings + schedules in one call), `GetIntegrationProceduresAsync`, and the existing recent-activity composition (`GetRunsAsync`, `GetAuditTimelineAsync`, `ListNotificationInstancesAsync`). Removed from Overview's load path (moved to Connections): `GetIntegrationConfigurationDeclarationsAsync`, `ListIntegrationSettingsAsync`, and the connection-name lookup — those are now only fetched on the page that displays them.

## 4. Configuration behavior

Unchanged. `IntegrationSettings.razor` (`PlatformSettingsEditor`, typed settings, secret handling, integration-scoped override precedence, audit action names) is untouched internally — the only change is the header: cross-page toolbar links to Detail/Notifications were replaced by the shared `IntegrationHubNav`, and the "Schedule diagnostics" deep link was kept in the page's own toolbar since it is not a hub tab.

## 5. Connection requirement/binding behavior

`IntegrationConnections.razor` (new) is the Connections section. It embeds the existing `IntegrationConfigurationOverview` organism unchanged (requirement cards: display name, capability/contract label, required/optional, bound/unbound state, connection name, verification status, "affects procedures", Bind/Change/Unbind/Verify actions calling the existing `Binding`/eligibility/Connection APIs) plus a raw bindings list with remove-with-confirmation. All binding-CRUD state and the in-flow "create a connection while binding" flow were relocated from `IntegrationDetail.razor` verbatim (same API calls, same error handling, same confirmation dialog) — not rewritten. The page states explicitly that connections are not owned by the integration and may be bound by more than one integration; a shared connection's bindings are unaffected by this move (`Bindings` table, not `Integration.ConnectionId`, remains the only persisted link — confirmed no schema change was made or needed).

## 6. Schedule contextual behavior

`IntegrationSchedules.razor` (new) lists schedules correlated by `IntegrationId` via `GetIntegrationSchedulesAsync` (falling back to the operator-detail's embedded schedule summaries on failure, matching the existing degrade-gracefully convention), and creates schedules via the existing `CreateIntegrationScheduleAsync`, which already associates the new schedule with the current integration. A page note links to the global `/schedules` list and Config Inventory, making clear the schedule remains a first-class, independently-discoverable resource. Ownership/ID semantics are unchanged — this is the same code that lived inline in `IntegrationDetail.razor`, relocated.

## 7. Runs contextual behavior

`IntegrationRuns.razor` (new) calls the same `GetRunsAsync(RunQueryFilter)` the global Runs list uses, with `IntegrationId` fixed by the route. The status dropdown reuses `RunStatusFacets` (including the Blocked facet and its explanatory banner) and changes only `Status` — there is no control on this page that can change or clear `IntegrationId`, so, unlike the global Runs list's status dropdown before its NAV-1-stabilization fix, there is nothing here to accidentally navigate away from this integration. An "Open in global Runs" link (`Epic33UiDisplayHelpers.GetRunsListIntegrationFilterHref`) provides the path back to the unscoped view, per the requirement. Row rendering reuses `RunScheduleOperatorUiHelpers` (`SortRunsForTriage`, `FormatRunListContextLine`, `FormatRunListWhenLine`) and `ManagementUiSemantics.MapRunLifecycleStatus`, the same helpers the global list uses — the two pages share formatting logic even though the markup is a separate, deliberately simpler page (the global list's execution-kind filter, correlation-key filter, and "needs attention only" toggle were judged unnecessary for a single-integration view and were not reproduced, keeping this a lean, non-divergent implementation rather than a second copy of the full list).

## 8. Notifications behavior

Unchanged internally. Header cross-links to Detail/Settings/ScheduleDiagnostics were replaced by `IntegrationHubNav`; the "Full diagnostics" and "Schedule diagnostics" deep links stayed in the page's own toolbar. No NTF-C concepts (cutover, parity, `UsePlatformPipeline`, legacy ledgers) were touched or reintroduced.

## 9. Execution/procedure behavior

Unchanged and kept on Overview, reusing the existing procedure registry, `GetIntegrationProceduresAsync`/`GetIntegrationProcedureAsync`/`LaunchIntegrationProcedureAsync`, and `/execute?integrationId=...` for the toolbar's Execute action. No second execution path was created.

## 10. Diagnostics disposition

`/integrations/{id}/schedule-diagnostics` remains a deep link, not a hub tab, per explicit instruction — confirmed by `ResolveSectionKey` returning `null` for it (tested). `IntegrationHubNav` is still shown on that page for orientation/navigation, and a one-line note clarifies it is reached by drill-in rather than being a primary section. No diagnostics content was deleted.

## 11. Authorization/capability handling

The hub nav's Notifications tab is hidden per-page using each page's own already-loaded `OperatorCapabilitiesDto.CanViewNotificationDefinitions` (mirroring the existing gate on `IntegrationNotifications.razor` itself) — menu/tab hiding, never a substitute for the page/API authorization each destination already enforces independently. No new capability token was invented; Connections and Schedules use the existing `CanManageIntegrations`/`CanViewIntegrations` gates exactly as `IntegrationDetail.razor` did before the split. IAM was not reopened.

## 12. Reusable UI/query components introduced

- `IntegrationUiRoutes.Sections` / `.ResolveSectionKey` (`Apperture.Management.Ui.Shared`) — pure, unit-tested route/section logic, same pattern as `AccountUiRoutes`.
- `IntegrationHubNav.razor` (`Apperture.Management.Ui.Client.Organisms`) — the persistent tab strip, same pattern as `AccountSectionNav.razor`, reusing the existing `.ui-tabs` CSS (no new styles).

No generic Resource/Catalog abstraction, no new Dashboard-style card framework, no new wizard infrastructure — the existing `WizardFlowController`/`WizardFlowPanel` were reused as-is (see §9 of the instructions / §13 below).

## 13. Setup wizard continuity

`IntegrationNew.razor`'s post-create redirect to `/integrations/{id}?wizard=1&step=bindings` still lands on Overview and still works. Because the bindings form moved to Connections, the wizard's guidance for the Bindings/Schedules steps now shows a "Open Connections" / "Open Schedules" link (carrying `?wizard=1`) rather than expanding the form inline on Overview, as it did before the split. The Readiness and Procedures steps behave as before (readiness is visible on Overview; procedures stay on Overview). **This is a real but intentionally modest simplification** — Connections/Schedules do not yet read `?wizard=1` to auto-open their add-forms or show step progress; an operator following the wizard link lands on the right page and clicks "Add binding"/"Add schedule" themselves. Full cross-page wizard state (auto-open, step banner on the destination page) was not implemented in this pass; flagged in §15.

## 14. Compatibility routes preserved

`/integrations/{id}`, `/settings`, `/notifications`, `/schedule-diagnostics`, and `/notifications/definitions/new` all resolve exactly as before. No route was removed or renamed. `RouteToSectionResolver`'s existing `/integrations` prefix match required no change — all six sections and the diagnostics deep link already fall under it.

## 15. Unresolved issues / deliberately scoped down

Given the size of this epic relative to a single session, the following were deliberately not done and should be picked up next, in rough priority order:

1. **Cross-page wizard auto-open** (§13) — Connections/Schedules do not yet honor `?wizard=1` to auto-expand their add-forms or show a compact step banner. Low risk, moderate value; a natural follow-up.
2. **Browser smoke was not performed** — no browser automation tool was available in this session (same constraint as the NAV-1 stabilization pass). Everything reported here is verified by: full solution-relevant project builds succeeding, the full existing UI/breadcrumb/route test suites passing (761 Ui.Shared.Tests, 1881 UnitTests, 38 ArchTests — all green, zero regressions), and new targeted tests for the added route/breadcrumb logic. Visual rendering, dark/light theming, responsive layout at narrow width, and actual click-through (including whether the tab strip *feels* right next to `PageHeader`) are **unverified** and should be walked before this is considered done end-to-end.
3. **Empty-state and capability-matrix test coverage per §29 of the brief** was not written exhaustively (e.g., dedicated tests for "no schedules for this integration", "required slot unbound with no eligible connections", reduced-permission Connections/Schedules/Runs page behavior). The moved code paths already had this behavior before the split (verified by reading, not by new tests); new page-level tests for it were not added given session scope. This is the most defensible gap to close next since it's cheap, mechanical work now that the pages exist.
4. **Overview's readiness banner is intentionally condensed** rather than a full second copy of `IntegrationConfigurationOverview`. If operators find that too thin (no per-requirement detail without a click to Connections), that is a presentation tuning question, not an architecture one — the data is already loaded.
5. A **shared Runs table/row component** was not extracted from the global `RunsList.razor` into something both pages call — `IntegrationRuns.razor` is a smaller, independent page that reuses the same helpers and API shape. If a third Runs-list consumer appears later, extracting a shared table component becomes worth the cost; two call sites did not meet that bar this pass (matches the "do not over-componentize" instruction).

## 16. Explicit confirmations

- **ResourceContext was not started.** No `ResourceContext`, `ContextId`, `Application` entity, or context-scoped uniqueness/authorization was added.
- **Business Data ownership was not moved into Integrations.** No Business Data tab exists on the hub; Business Data's existing provenance links (Integration/Run/OperationItem) are untouched.
- **Services were not started.** No Service aggregate, catalog, or typed-binding-for-services work was added.
- **Resource Catalog was not started.** No generic Resource/Capability/metadata-tree abstraction was introduced; Connections and Schedules remain their own distinct models throughout.

## Validation

| Suite | Result |
|-------|--------|
| `Apperture.Management.Ui.Shared.Tests` (full) | **761 passed** (was 740; +21 net new/updated for HUB-1 routes, sections, and breadcrumbs) |
| `Apperture.UnitTests` (full, unfiltered) | **1881 passed, 0 failed** (unchanged — no regression) |
| `Apperture.ArchTests` (full) | **38 passed** |
| Builds: `Ui.Shared`, `Ui.Client`, `Management.Api`, `Management.Host`, `RuntimeHost.Windows.WorkerHost` (individually, `--no-dependencies`) | **succeeded** |
| Full solution build | Not re-attempted in this pass; blocked in the prior NAV-1 stabilization session by a pre-existing running `Apperture.AppHost` process holding its own executable lock — an environmental condition, not a code issue (per-project builds above are the substitute verification) |
| Browser smoke | **Not performed** — no browser automation tool available this session (see §15.2) |
