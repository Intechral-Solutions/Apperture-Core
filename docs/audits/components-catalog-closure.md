# Components catalog — implementation closure

Date: 2026-09-23
Branch: `components-catalog` (six plan commits plus review follow-ups; not merged, not pushed)
Design authority: [Components design gate](../01-architecture/transitions/components-services-catalog/components-design-gate.md),
[Services design gate §31](../01-architecture/transitions/components-services-catalog/services-design-gate.md).
Decision record: [ADR-0043](../02-decisions/ADR-0043-component-catalog-read-model.md).

**Status: Components implementation (CMP-1…CMP-6, with CMP-X) is complete. Services implementation (SVC-1…SVC-8)
has not started.**

> **Update (2026-09-24): Services implementation is complete** — see
> [`services-connection-workspace-closure.md`](./services-connection-workspace-closure.md) and
> [ADR-0044](../02-decisions/ADR-0044-connection-workspace-and-browse-authorization.md). SVC-4 retired the Connectors
> tab described below and resolved the `?kind=` residual (the catalog now opens filtered from
> `/components?kind=connector`). The Components record itself is otherwise unchanged and accurate as of its delivery.

## What shipped

| Plan | Delivered |
|---|---|
| **CMP-1** catalog and API | `ComponentKey` / `ComponentSummary` / `ComponentDetail` / `ComponentKinds` / `ComponentOrigins` contracts; `IComponentCatalog` + `IComponentCatalogProvider`; the Connector and Notification Definition providers; `ComponentCatalogService`; additive `Origins` filter on `NotificationDefinitionListQuery` (+ SQL store); read-only `GET /api/components` and `GET /api/components/{kind}/{nativeId}`; ManagementClient DTOs/methods. |
| **CMP-2** list and navigation | `/components` page (Name / Kind / Origin / Supplied by / Id, debounced search, Kind / Origin / Module filters, no Context filter); `Config → System → Components → { Catalog, Modules }` with an ungated structural parent and the derived, explicitly mapped `ViewComponents` token; route/section/breadcrumb rules; Config landing card. |
| **CMP-3** detail | `/components/{kind}/{nativeId}` wrapper for both families; connector fields + verification fact + Create-a-connection link; notification definition read-only facts + link to the authoritative page; module attribution only when resolved; `/connections/new?connectorDefinitionId=` preselection; explicit host fallbacks for dotted native-id routes. |
| **CMP-4** usage | Detail-only usage: Active Connections per connector (new `GetVisibleByConnectorDefinitionAsync` / `ListConnectionsByConnectorAsync`), current subscription count per definition; absent (never zero) when unreadable. |
| **CMP-5** Modules and Connectors | "Components provided by this module" above the unchanged "Integrations using this module"; `ContributionCounts` "Contributes" facts row; Connectors-tab rows link to Component detail (tab retained). |
| **CMP-6** documentation | ADR-0043, the design-gate implementation record (§22b), roadmap, transitions notes, control-panel UI, glossary/terminology, decisions index and this document. |

## Invariants verified

- **Projection only** — no table, entity, migration, Component GUID, universal FK or registry; architecture tests pin no
  `DbSet<Component…>`, no `Component…Entity`, no `component.*` action, no `ComponentId`/`IComponentResource`, and no
  type named exactly `Component`.
- **Eligibility by Origin, not Scope** — `platformBuiltIn`/`moduleBuiltIn` via the new `Origins` filter (real-SQL
  test), re-applied in the provider; fixtures deliberately contradict Scope. Origin/ModuleId cross-check throws
  rather than coerces.
- **Authorization reuse** — providers depend only on authorized services (constructor-type and source-scan tests);
  denied families vanish from rows, `AvailableKinds`, `AvailableModules` and `TotalMatching`; both denied → 403;
  detail denied/unknown → 404; module display metadata only under `module.configure`.
- **Performance** — the list never touches the module listing service (strict-mock test, plus a run through the
  real listing service over a strict registry), one notification query per list, no per-Component lookup, no usage
  on list (reflection guard on the payload types).
- **Menu** — parent visibility matrix N1–N4, no capability on the parent (N5), every menu token explicitly mapped
  (N6 plus a whole-token-set unmapped guard); no new rail domain.
- **Detail delegation** — the controller has only `GET` actions; the detail page has no input, select, button or
  mutation call; built-in defaults stay on the Notification Definition page (CMP-X, not duplicated).
- **Module detail** — the HSK-A integrations section is pinned (`contextId: null`, `CanViewIntegrations` gate,
  explanatory comment); the Components section is global and issues exactly one catalog query.
- **Connectors tab retained**; retirement stays with Services (SVC-4).

## Rulings made during implementation

1. **Retired Connections are not usage.** Usage counts Active Connections: retired connections are excluded from
   binding eligibility and from every "live" count, and the runtime resolver returns no client for them. Caveat
   accepted: retiring a Connection does not remove its Bindings, so a dangling Binding to a retired Connection
   leaves the connector reading "no active connections" (operationally true; a "retired but still bound" signal
   would be a separate diagnostic).
2. **Subscription usage counts every subscription row** the authoritative subscriptions section lists, so the two
   never disagree.
3. **`take`** — non-positive means the default (200), matching Inventory; maximum 200.
4. **Kind filter naming a denied family → 403** (every consulted provider denied).
5. **Filter options** are retained from the last unfiltered-kind load, because a kind-restricted query only reports
   that kind and its modules.
6. **Partial authorization** — Module detail's empty copy is scoped to "components you can view" when the caller can
   read only one family.

## Defects found and fixed during review

- **Dotted native-id routes 404 on a hard load** (real, pre-existing, confirmed with a probe host): the Ui.Server
  default fallback `{*path:nonfile}` refuses a final segment that looks like a file. `/components/connector/Apperture.Platform.Sql`,
  the already-shipped `/modules/Sample.HelloWorld` and built-in `/config/notifications/definitions/platform.schedule.expected_run_missing`
  all failed on refresh/bookmark while in-app navigation worked. Fixed with explicit catch-all fallbacks for those three
  route families; a source test pins them. **Operator smoke should confirm on the running host.**
- Detail: not-found flashing before load, stale error carried between components, shell title published after
  navigation, duplicated help copy.
- List: blank body while reloading after an empty result, permanently disabled Refresh when forbidden, a module
  filter stranded when the kind changed, a failed load making an identical retry a no-op.
- Usage: definition usage now degrades when a definition vanishes mid-request; truncated connector usage offers the
  full Connections list.
- Module detail: an unresolved or failed load is never rendered as "provides none".

## Accepted residuals

- One inconsistent built-in Notification Definition row (Origin vs ModuleId) makes the catalog list fail loudly
  rather than skip the row — intentional per the gate ("visible, not silently coerced").
- The notification list is bounded by the store's 200-row cap; real scale is four rows.
- Module attribution on detail goes through the module listing service, which builds descriptors twice; paid once
  per detail view and only under `module.configure`, never on list.
- A connector-usage query fetches every state and materializes connection JSON for rows it then filters; trivial at
  this scale.
- The Connections list cannot filter by connector, so a truncated usage list can only offer the plain list.
- The client `ModuleDetail` has a pre-existing stale-response pattern when navigating between two modules mid-load;
  the new Components state inherits it.
- The pre-existing Integration-configuration "create connection" link emits `?connector=` and `&returnUrl=`, which
  `ConnectionNew` ignores; it could adopt `connectorDefinitionId` later.
- Module-supplied connectors are wired and covered with a fake, but none exists in production.
- Module attribution is decoration on detail: a module-registry fault (not a denial) fails that detail request
  instead of degrading to the bare module id, unlike usage which degrades.
- Notification Definition ids resolve case-insensitively (SQL default collation), so a differently-cased URL reaches
  the same Component; the response carries the canonical id. Cosmetic.
- The dotted-id fallback fix covers the `/components`, `/modules` and `/config/notifications/definitions` families
  only. Other routes whose final segment can look like a file (for example
  `/business-data/customers/{BusinessKey}` with keys such as `C.1001`) still 404 on a hard load — pre-existing and
  outside this body. There is no runtime routing test for the fix (the Ui.Server host is not test-hostable
  without an entry-point change); the guard pins the mapping in source and the probe evidence is recorded above.
- Keyboard focus is not restored after "Clear filters" / "Try again" on the list (the control removes itself).
- The list page's filters are not URL-bound, so the `/components?kind=connector` link the Services gate sketches
  for SVC-4 would land on the unfiltered list; SVC-4 must add a `kind` query parameter (or link differently).
- Component detail shows some raw tokens (mutability, scope, module state) rather than friendlier labels.
- Kind and Origin appear as metadata rows on detail, not as pills.

## Review record

Every plan was reviewed by an independent read-only reviewer before or immediately after its commit, and the whole
branch was reviewed against both gates in two halves (backend/authorization and UI/navigation/docs). No Critical
finding was raised at any point. Important findings and their resolution: CMP-2 — catalog rows linked to a detail
route that landed in the next commit (transient, same branch, resolved by CMP-3); CMP-3 — dotted native-id hard
loads returned 404 (fixed, above); final review — the closure's validation section was empty (recorded below).
All Minor findings were either fixed or listed under residuals.

## Services readiness

Nothing in Services was started: no `connection.browse`, no provider/tool seam, no SQL structure browser, no
Connection-workspace change, no Service type. The catalog and the Connectors tab are unchanged by Services and are
ready for it. SVC-1 is unblocked and **not started**.

## Validation

Run 2026-09-23 at code commit `7a939f1` (later commits on the branch are documentation only), from an isolated,
repo-local, git-ignored artifacts path because the running Aspire stack locks the normal outputs. Suites ran
sequentially, one MSBuild node, no build servers.

| Check | Result |
|---|---|
| `dotnet build Apperture.slnx` | 0 errors, 46 warnings (none in any file added or edited by this body) |
| `Apperture.UnitTests` | **2597 passed**, 0 failed, 0 skipped |
| `Apperture.ArchTests` | **38 passed**, 0 failed, 0 skipped |
| `Apperture.Management.Ui.Shared.Tests` | **1290 passed**, 0 failed, 0 skipped |
| `Apperture.Management.Ui.Client.Tests` | **135 passed**, 0 failed, 0 skipped |
| `Apperture.IntegrationTests` | **241 passed**, 0 failed, 0 skipped (4 m 02 s) |

**Proof that SQL actually executed** (the SQL tests can soft-skip as a pass, so "Skipped: 0" is not the proof):
the test databases' `sys.dm_db_index_usage_stats.last_user_update` markers advanced across the run —
`Apperture_ManagementGovernanceTests` 22:07:32 → 22:19:38 and `Apperture_IntegrationTests` 22:06:09 → 22:23:06 (UTC,
server clock), and the run took four minutes. The new Component tests are hard-failing (they throw when SQL is not
configured) and assert seeded migration rows, so they cannot pass without a real database.

Environment notes: the dedicated `apperture-test-sql` and `apperture-test-sftp` containers were stopped at the start
of the session and were started for this run; one governance test
(`Post_sftp_test_connect_persists_…`) failed on the first pass only because the SFTP container was down, and passed
with it up.

**Not performed: browser smoke.** The running stack serves builds from before this branch, and restarting it was not
authorized. The operator checklist below covers what automation cannot.

### Operator smoke checklist (restart the stack from this branch first)

1. **Menu** — Config → System shows *Components* with *Catalog* and *Modules*; *Modules* still opens `/modules`.
2. **Catalog** — `/components` lists Platform SQL, SFTP, Campus Nexus and the built-in notification definitions;
   an operator-created notification definition is absent. Search (no request per keystroke), Kind, Origin and
   Module filters, and their combinations; light/dark theme and a narrow window.
3. **Hard loads (the fix under §22b)** — refresh and open in a new tab: `/components/connector/Apperture.Platform.Sql`,
   `/modules/Sample.HelloWorld` and `/config/notifications/definitions/platform.schedule.expected_run_missing` all
   boot the app instead of returning 404.
4. **Detail** — a Connector and a Notification Definition component; technical ids readable but not dominant; the
   notification link opens the authoritative page and no editing control appears on the component page.
5. **Create connection** — from a Connector component; the connector is preselected; also open
   `/connections/new?connectorDefinitionId=Apperture.Platform.Sql&context=shared` (both compose) and an unknown
   connector id (falls back to the normal form).
6. **Usage** — a connector with connections (Active only listed, links open `/connections/{id}`) and one with none
   ("No active connections use this connector yet."); a definition's current subscription count.
7. **Module** — Module detail shows *Components provided by this module* above *Integrations using this module*;
   links work; the Integrations section is unchanged; a module that provides none shows the empty state.
8. **Connectors tab** — still present on `/connections`; row names open Component detail.
9. **Authorization (automated-only unless a second principal is easy)** — an operator without one family sees no
   rows or filter option for it; a modules-only operator still reaches Modules through the Components parent.
   These are pinned by unit tests (family omission, 403/404 semantics, menu matrix N1–N6) and were not exercised in
   a browser.
