# Components catalog — implementation closure

Date: 2026-09-23
Branch: `components-catalog` (six plan commits plus review follow-ups; not merged, not pushed)
Design authority: [Components design gate](../01-architecture/transitions/components-services-catalog/components-design-gate.md),
[Services design gate §31](../01-architecture/transitions/components-services-catalog/services-design-gate.md).
Decision record: [ADR-0043](../02-decisions/ADR-0043-component-catalog-read-model.md).

**Status: Components implementation (CMP-1…CMP-6, with CMP-X) is complete. Services implementation (SVC-1…SVC-8)
has not started.**

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

## Services readiness

Nothing in Services was started: no `connection.browse`, no provider/tool seam, no SQL structure browser, no
Connection-workspace change, no Service type. The catalog and the Connectors tab are unchanged by Services and are
ready for it. SVC-1 is unblocked and **not started**.

## Validation

Recorded in the final section below at the whole-branch validation run.
