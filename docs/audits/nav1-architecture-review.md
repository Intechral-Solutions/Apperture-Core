# NAV-1 architecture review and handoff

Date: 2026-09-11
Reviewer: incoming agent (review-only pass; no production changes made)
Scope reviewed: commit `c7691ef` (NAV-1 — Operator Information Architecture), against `98e805e` / `05089c2` (NTF-C) baseline.

Validation performed in this pass:

| Suite | Result |
|-------|--------|
| `Apperture.Management.Ui.Shared.Tests` (full) | **731 passed** — matches closure report |
| `Apperture.ArchTests` (full) | **38 passed** |
| `Apperture.UnitTests` filter Inventory / Epic33 / DashboardExpansion / LoginRouteHelper / Menus | **81 passed** |
| `Apperture.UnitTests` (**full, 1867 tests**) | **1865 passed, 2 failed** — both inherited from NTF-C, see F-06 |
| EF SQL generation probe for the blocked-run predicate | **correct** — see F-07 |
| Browser smoke | **not run.** Aspire AppHost pid 31892 is up; Management Host and WorkerHost are not running. No browser automation available. Not started for this audit. |

---

## 1. Executive assessment

**NAV-1 is sound with minor follow-up.**

Every architectural constraint NAV-1 was given holds under code inspection, not just documentation:

- **No premature abstractions.** No `ResourceContext`, `ContextId`, `ApplicationId`, Application entity, Collection model, `BusinessRecord` table, or resource-catalog type exists anywhere under `src/`. The only `AuthenticationContextId` is Entra Conditional Access, unrelated.
- **Domain ownership is unchanged.** Connections remain instance-scoped with no `IntegrationId`; `Bindings` is still the only persisted Integration↔Connection link; Schedules/Runs keep their existing string-correlation relationships. NAV-1 relocated shell placement only — no aggregate, table, or service ownership moved.
- **Business Data is still the provisional projection.** `DemoBusinessRecordProjectionService` is a read-time view over `OperationItems`, authorized by `integration.read` at instance scope. `/business-data/customers` is the same page at a new route.
- **Blocked is genuinely derived.** No new column, no new lifecycle status. The predicate is `Status == "failed" && FailureSummary LIKE '[Blocked] %'`, reusing the pre-existing Epic 27/30 contract `RunFailureCapture.DeferredWorkBlockedPrefix` that `RuntimeHealthQueryService` already depends on.
- **Inventory is an explorer, not a catalog.** `InventoryQueryService` fans out to existing list services and maps to a read DTO. Nothing is persisted.
- **No SDK break.** `IModuleNavigationProvider` changed by doc-comment only; the constraint is enforced host-side in `MenusController` against `SectionOwnerConstants.KnownRailDomains`.
- **Capability tokens are real.** Every `DomainMenuCapabilityTokens` value maps to an actual permission evaluation in `OperatorCapabilitiesQueryService` (`integration.read`, `platform-settings.read`, `notification-definition.read`, `managed-contact.read`, `audit` …). Nothing was invented.

The findings below are cohesion, completeness and honesty-of-presentation issues — not correctness breaks in the shipped navigation. Two of them (F-01, F-02) are worth fixing before HUB-1 because HUB-1 will build on the same Config/Inventory surface and the same capability filter. One (F-06) is inherited breakage on `main` that should be fixed immediately regardless.

---

## 2. Architecture understanding (handoff picture)

### Shell

`ControlPanelMenuDefinitions.GetDefaultMenus()` is the single menu source of truth. The Blazor WASM client (`Apperture.Management.Ui.Client`, served by `…Ui.Server`) seeds that tree into `DomainMenuService` for stable first-paint keys, then replaces it with `GET /api/menus`. `MenusController` = defaults → append enabled-module `NavigationContributionDescriptor`s (only for known rail domains) → `DomainMenuCapabilityFilter`.

Rail: logo → Control Center; middle dynamic loop → Integrations, Business Data (dictionary insertion order); gear → Config. `SectionOwnerConstants.CustomZoneOwners` keeps Control Center and Config out of the middle loop and exempts them from empty-domain pruning.

Rail highlight comes from `RouteToSectionResolver` (hard-coded prefix table first, then a `DomainMenuTree.MenuContainsActiveHref` fallback that skips custom-zone owners). Breadcrumbs are a separate mechanism entirely — `ManagementBreadcrumbResolver`, an ordered `PathMatcher[]` with most-specific-first rules; `Trail()` drops the href on the terminal segment and `ResolveOrientation` splits it into `Parents` + `Title`. UIX-4 context (`ManagementNavigationContextCatalog`) composes only onto `/runs/{id}`.

Landing: `Routes.razor` (`/`) → `OperatorLanding` → `/control/center`. `LoginRouteHelper.DefaultPostLoginPath = "/control/center"`. `/dashboard` (`DashboardHome.razor`) awaits `DomainMenuService.WhenFetchedAsync()` then `NavigateTo("/control/center", replace: true)` — it is a pure redirect with no body, so it cannot act as a second home. Deferred `/dashboard/{manage,add,layout,delete}` stubs resolve to **Config** in `RouteToSectionResolver` (exact `/dashboard` resolves to Control Center) and are reachable only from Developer & Internal.

### Integrations / Connections / Execution

- `Integration` (`Integrations`, 24-hex string PK, `ModuleId` required, `ScopeKey` is authorization scope) owns by column, never by SQL FK: `Bindings`, `ScheduleDefinitions`, start requests, `WorkflowInstances`, `OperationRuns`/`OperationGroups`, `AttentionIncidents`, notification definitions/instances, `PlatformEvents`, and `PlatformSettingValues` where `ScopeKind=Integration`.
- `Connection` is **instance-scoped** — `ScopeKey` + `OwnerId`, no `IntegrationId`. Connector kinds live in the in-memory `IConnectorDefinitionRegistry` (the SQL connector table was dropped). `Bindings` (`IntegrationId?`, `ModuleId`, `SlotName`, `ContractType`, `ConnectionId?`, `CapabilityId?`) is the reusable link; uniqueness of one Active binding per slot+integration is enforced in `BindingService`, not by index. This is the seam CTX will move first.
- Execution: `ScheduleDefinitions` → `ScheduleAssignments` → `ScheduleStates` (1:1) and `ScheduleRuns` (unique `ScheduleId + FireTimeUtc`, optional `RunId`). `WorkflowStartRequests` / `OperationStartRequests` are PK'd on `RunId` with filtered-unique idempotency keys. `OperationRuns` is the run record; `OperationGroups` → `OperationItems` is the only EF FK chain in this area. WorkerHost hosts dispatch.
- Failure evidence: `RunFailureCapture` writes `FailureSummary` (≤512) plus optional `FailureDiagnosticsJson`. `DefinitionNotExecutableException` is the sole writer of the `[Blocked] ` prefix and also emits `{blocked:true, definitionId, kind, message}` in the JSON — a richer structured signal that exists today but is not queried.

### Business data

`SharedRecordIdentity` on `OperationItems` (`SharedRecordType` + `SharedBusinessKey` / `SubjectKey`, non-unique index) is the correlation key. `DemoBusinessRecordProjection` is computed at read time across `Sample.FileImport` and `Sample.ApiPolling`. Provenance/activity is derived from the same items and links out to runs and integrations — verified intact after the route move (`DemoCustomerUiHelpers.GetRunDetailHref`, `TryGetIntegrationHref`).

### Settings / secrets / modules / authorization

- `PlatformSettingDefinitionRegistry` (code-declared, gated demo gallery) + `PlatformSettingValues` scoped by `ScopeKind`/`ScopeId`; sensitive values go to `SecretStore` by reference; typed schema drives rendering.
- Modules are compiled `IModule` + `ModuleManifest` with dotted string ids; `ModuleInstallationStates` (PK `EnvironmentId + ModuleId`) is the only persistence. `SqlModuleRegistryService.ListModulesAsync` discovers from DI on every call and merges installs — **there is no cache**.
- Authorization: single-tenant instance, `ActionConstants` × `ResourceTypeConstants` × `ScopeKeyConstants` (`instance`, module, integration). `OperatorCapabilitiesQueryService` projects ~28 booleans; `ControlActionAvailabilityProjection` projects per-run action availability for the control facade. ArchTest boundary: `Apperture.ControlPlane.Security` must not reference `Apperture.Management.Ui.Shared` (holds).

The best existing reference for all of the above is `docs/01-architecture/transitions/application-contexts/current-inventory.md` (1241 lines, updated by NAV-1). It is accurate except where noted in F-01 and F-10.

---

## 3. NAV-1 findings

### F-01 — Dashboard card aggregation is orphaned, and three documents say otherwise · **Medium**

**Confirmed behavior.** `DashboardHome.razor` dropped all card rendering. No UI anywhere calls `GetDashboardDefaultAsync` (`grep` across `src` + `tests`: only the client interface/impl and a negative assertion in `Uix2ManagementBreadcrumbStabilizationTests`). `GET /api/dashboard/default`, `DashboardDefaultViewService` and all ten `IDashboardCardProvider`s remain registered and unreachable.

`ConfigLanding.razor` is entirely static `ManagementSummaryCard` links — it makes no HTTP call.

But three documents assert otherwise:
- `nav-overhaul/mapping.md`: "Governance, Authentication Posture … Surface on Config landing via existing dashboard card data."
- `control-panel-ui.md`: "Unique dashboard-card data that is not already on Control Center (governance / authentication posture) is summarized on Config overview."
- `current-inventory.md` §9: "`GET api/dashboard/default` still exists for deferred Dashboard Panel work **and Config overview cards**…"

**Evidence.** `Pages/Dashboard/DashboardHome.razor`; `Pages/Config/ConfigLanding.razor`; `Apperture.ControlPlane.Security/Dashboard/*DashboardCardProvider.cs`; `Apperture.Management.Api/Controllers/DashboardController.cs`.

**Impact.** Eight of the ten providers duplicate Control Center data, so nothing operational is lost there. Two do not: `governance-summary` (role counts) and `auth-warnings`. The latter carried a genuinely high-value signal — *"No viable sign-in path — enable local auth or fix Entra."* with `DashboardCardAttentionConvention.FromAuthPosture` attention ranking. That data is still visible on `/config/auth` (`Pages/Config/Auth/AuthOverview.razor` renders `overview.Warnings`), so the **data** survived; the **proactive surfacing** did not. Meanwhile ~10 provider classes and one endpoint are dead weight that DSH will inherit and have to reason about.

**Recommended disposition.** Pick one and make the docs match: either add the two unique cards to `ConfigLanding` as documented, or explicitly retire `/api/dashboard/default` and the two unique providers and record that decision. Do not leave it ambiguous into DSH. Docs must be corrected either way.

### F-02 — Capability filter keeps menu parents whose own destination is capability-gated · **Medium**

**Confirmed behavior.** `DomainMenuCapabilityFilter.FilterItems` drops a parent when its children are all filtered **only if the parent has no `Href`**:

```csharp
if (children.Count == 0 && item.Children is { Count: > 0 } && string.IsNullOrWhiteSpace(item.Href))
    continue;
```

Two Config parents have an `Href` and no `RequiredCapability`, and that href is the same route as a gated child:

| Parent | Parent `Href` | Gated child with the same href | Child gate |
|--------|---------------|-------------------------------|------------|
| System | `/config/platform-settings` | Platform settings | `ViewPlatformSettings` |
| Attention | `/attention/operational-teams` | Operational teams | *(none — no read capability exists)* |

So an operator without `platform-settings.read` sees a **System** entry with every child hidden, pointing at a page that 403s. `Nav1OperatorInformationArchitectureTests.Capability_filter_hides_gated_items_and_keeps_config_overview` asserts the happy path but never this case.

**Evidence.** `DomainMenuCapabilityFilter.cs:52-54`; `ControlPanelMenuDefinitions.cs` (`Id = "config-system"`, `Id = "config-attention"`); `OperatorCapabilitiesQueryService.cs:135` (`PlatformSettingsRead`).

**Impact.** The menu is neither fully honest nor fully permissive. Security is unaffected — `/config/platform-settings` and every Attention controller enforce independently (`Forbid()` / `UnauthorizedAccessException`) — but it violates NAV-1's own stated goal of not exposing obviously-unusable destinations when an existing permission can decide.

**Recommended disposition.** Add one rule to the filter: drop a parent whose `Href` equals the `Href` of a child that was filtered out. That fixes **System** precisely, invents no capability, and leaves **Attention** as a correctly-documented gap (no operational-team read token exists to gate it with). Add the matching test.

### F-03 — Breadcrumbs now assert Config → Inventory ownership of Connections and Schedules · **Medium**

**Confirmed behavior.** `ManagementBreadcrumbResolver` changed the canonical parent trail for off-rail first-class resources:

- `/connections`, `/connections/{id}`, `/connections/new` → `Config → Inventory → Connections`
- `/schedules`, `/schedules/{id}`, `/schedules/new` → `Config → Inventory → Schedules`

and `RouteToSectionResolver` resolves both prefixes to `SectionOwnerConstants.Config`, so the gear highlights.

**Evidence.** `ManagementBreadcrumbResolver.cs` — `Connections(...)` helper, `MatchConnectionsList`, `MatchSchedules{List,Detail,New}`; `RouteToSectionResolver.cs` `/connections` and `/schedules` branches.

**Impact.** Rail highlight is defensible (Config owns the explorer that discovers them). Breadcrumbs are not the same thing — they are the shell's ownership statement to the operator, and this one directly contradicts ADR-0040 rule 6 and the ADR's own "Connections and schedules keep their own models … not promoted to rail domains." It is also actively misleading in the common flow: arriving at a schedule from an Integration detail page shows a trail claiming you came from Config → Inventory. Under CTX, Connections become ResourceContext-owned, so this trail has to be undone.

**Recommended disposition.** Low-cost correction now, before HUB-1 adds more Integration→Connection/Schedule drill-ins: make the trail reflect the real product statement (e.g. `Connections` / `Schedules` as their own root crumb, matching how `/runs` and `/integrations` are treated), keep the Config rail highlight, and keep Inventory as a *discovery* entry point rather than a breadcrumb ancestor. If it's deliberately kept, ADR-0040 should say so explicitly.

### F-04 — Inventory search and filtering silently operate on a truncated per-source window · **Medium**

**Confirmed behavior.** `InventoryQueryService` loads each source, then filters, sorts and takes **in memory**. Source loading is inconsistent:

| Source | Bound |
|--------|-------|
| Modules, Integrations, Connections, Schedules | **unbounded** (`ListXAsync(cancellationToken)`, no take) |
| Notification definitions | `SourceTake` = 100 |
| Runs, Audit (24h window) | `SourceTake` = 100 |

`q`, `status` and `sort` are then applied to whatever was loaded, and `NormalizeTake` caps output at 500 (default 100). `InventoryQueryResult` carries `Items` + `AvailableTypes` — **no total count and no cursor**. `Inventory.razor` renders no pagination and no "showing N of M".

**Evidence.** `InventoryQueryService.cs` (`CollectResourcesAsync`, `CollectActivityAsync`, `Matches`, `Sort`, `NormalizeTake`); `IInventoryQueryService.cs` (`InventoryQueryResult`); `Pages/Config/Inventory.razor`.

**Impact.** Searching for the 150th notification definition or a run outside the newest 100 returns "Nothing to show" — indistinguishable from "does not exist", on a surface whose stated purpose is search. Simultaneously, three sources load with no bound at all, so the endpoint's cost grows linearly with the estimate and will be the first thing to hurt at fleet scale. The 447-line `InventoryQueryServiceTests` covers filtering/sorting/capability well but does not cover truncation.

**Recommended disposition.** Not a blocker for HUB-1, but resolve the shape before Inventory is leaned on further: either push `q`/`status`/`sort` into each source query, or keep client-side aggregation and be honest — return a total/truncated flag and render "showing first N". Make all sources consistently bounded either way.

### F-05 — Runs status facet discards sibling query filters · **Medium**

**Confirmed behavior.** `RunsList` composes correctly server-side: `status`, `correlationKey`, `createdFrom`, `integrationId`, `moduleId` and execution kind all flow into `RunQueryFilter`, and the blocked branch is applied alongside the others. But the new toolbar handler rebuilds the URL from scratch:

```csharp
Nav.NavigateTo(string.IsNullOrWhiteSpace(next) ? "/runs" : $"/runs?status={Uri.EscapeDataString(next)}");
```

**Evidence.** `Pages/Runs/RunsList.razor:327-335`; `[SupplyParameterFromQuery]` properties at 258-271; `RunQueryService.ListRunsAsync`.

**Impact.** An operator who arrives at `/runs?integrationId=abc` from an Integration or Control Center and then picks "Blocked" loses the integration scope without any indication. This is exactly the HUB-1 flow (Integration hub → filtered runs), so it will get worse, not better.

**Recommended disposition.** Preserve the existing query string and replace only `status`. Small, contained fix; worth doing before HUB-1.

### F-06 — `main` carries two failing unit tests, inherited from NTF-C · **Medium (not caused by NAV-1)**

**Confirmed behavior.** Full `Apperture.UnitTests`: **1865 passed, 2 failed.**

```
PlatformSettings.GatedPlatformSettingDefinitionRegistryTests.GetAll_excludes_demo_when_not_development_and_flag_disabled
  Expected keys to contain 17 item(s), but found 16
PlatformSettings.PlatformSettingDefinitionRegistryTests.Registry_contains_phase1_keys_with_input_kind_metadata
  Expected defs to contain 17 item(s), but found 16
```

Root cause traced to commit `05089c2` (NTF-C), which correctly removed `Notifications.UsePlatformPipeline.Enabled` from `PlatformSettingKeys.cs` and `PlatformSettingDefinitionRegistry.cs` but left two hard-coded count assertions at 17. NAV-1 touched no platform-settings file.

**Evidence.** `git show 05089c2 -- "*PlatformSettingKeys.cs" "*PlatformSettingDefinitionRegistry.cs"`; `tests/Apperture.UnitTests/PlatformSettings/GatedPlatformSettingDefinitionRegistryTests.cs:37`.

**Impact.** The default branch is red. Both closure reports (NTF-C and NAV-1) ran `Apperture.UnitTests` with a `--filter`, so neither surfaced it; NAV-1's report table shows "UnitTests filter … 150 passed", which is true and also why this was missed.

**Recommended disposition.** Change both assertions 17 → 16 as the first commit of the next work item. Consider asserting on required-key membership rather than an exact count, so registry additions/removals stop breaking unrelated tests. Separately: future closure reports should run at least one unfiltered suite.

### F-07 — The blocked facet is correct, but has zero server-side test coverage · **Low**

**Confirmed behavior.** I probed the actual EF Core 10 translation of the predicate (generated SQL only, no database touched), because `[` is a character-class metacharacter in T-SQL `LIKE`:

```sql
DECLARE @blockedPrefix_startswith nvarchar(512) = N'\[Blocked] %';
… WHERE [o].[Status] = N'failed'
  AND [o].[FailureSummary] IS NOT NULL
  AND [o].[FailureSummary] LIKE @blockedPrefix_startswith ESCAPE N'\'
```

EF escapes the bracket correctly. **The facet works.** It is also not a NAV-1 invention: `DeferredWorkBlockedPrefix` is the documented Epic 27/30 write contract (`RunFailureCapture` is the only writer, via `DefinitionNotExecutableException`) and `RuntimeHealthQueryService` already reads it with the same `StartsWith` at two call sites.

However: no test — unit or integration — exercises `RunQueryService.ListRunsAsync` with `status=blocked`. The whole feature that replaced a 257-line page rests on untested SQL translation behavior. And `OperationRuns` carries exactly one index (`RetryOfRunId`) — nothing on `Status`, `CreatedAt`, `IntegrationId` or `CorrelationKey` — so `/runs?status=blocked` with `ORDER BY CreatedAt DESC` is a scan-and-sort.

**Evidence.** `RunQueryService.cs:47-62`; `RunFailureCapture.cs:38,73`; `RuntimeHealthQueryService.cs:140,296`; `AppertureDbContext.cs:103-126`; existing blocked-prefix tests are `RuntimeHealthBlockedDeferredSqlServerTests` / `RecoveryWorkflowRemediationSqlServerTests`, neither of which touches the runs filter.

**Impact + recommendation.** **The transitional mechanism is acceptable — do not replace it now.** It reuses an established single-writer contract rather than inventing a convention, and the richer `FailureDiagnosticsJson` (`{blocked:true,…}`) is already persisted, so a future execution-domain pass can promote the derivation to structured evidence (or a computed column / filtered index) without a data migration. What it needs is not a redesign but a **SQL Server integration test** pinning `status=blocked` — one blocked run, one plain failed run, one succeeded run — because that test is what makes the convention safe to leave in place. Fold the `OperationRuns` index question into HUB-1 scoping.

### F-08 — Blocked-work recovery: circular links and an orphaned recovery API · **Low**

**Confirmed behavior.** Recovery capability survives: `RunDetail.razor:29-32` has a `Retry (new run)` button gated by control-facade `execution.retry` (`ControlActionAvailabilityProjection` → `EnsureCanRetryRunAsync`), calling `runs/{id}/retry` → `WorkflowRetryService`, which accepts `failed` and `cancelled` and returns `Status: "blocked"` when still ineligible. Item-level retry lives in `RunOperationItemizationPanel`. "Trigger scheduler cycle" moved to Control Center (`ControlCenter.razor:799`) and Schedule detail.

Two residues:

1. **Circular links.** `RunDetail.razor:219` and `:228` read *"Blocked runs"* / *"Retry from blocked runs"* and point at `/runs?status=blocked` — a list with no retry control. The retry the operator wants is the button at the top of the page they are already on.
2. **Orphaned API.** `RetryBlockedWorkAsync` and `GetBlockedWorkAsync` have no UI caller anywhere. `GetBlockedWorkItemAsync` survives (RunDetail reads it for display only). So `GET/POST api/control/recovery/blocked-work[/{id}/retry]` — including its `inspectionOnly` guidance for operation-kind blocked work, its 24h/7d window, and its module/blocked-reason columns — is UI-dead.
3. **Permission shift.** The old page's retry was gated by `recovery.manage` (`RecoveryGovernanceService.CanManageAsync`); the surviving path is gated by workflow-retry authorization. An operator holding only `recovery.manage` has lost the UI retry path. I did not enumerate seeded roles to confirm anyone is actually in that position.

**Evidence.** `Pages/Runs/RunDetail.razor:29,219,228,825,893`; `Controllers/RecoveryController.cs:51-120`; `ManagementClient.cs:1234-1290`; `WorkflowRetryService.cs:40-110`; `git show c7691ef^:…/Pages/Control/BlockedWork.razor`.

**Recommended disposition.** Fix the two link labels/targets (point at the run's own retry affordance, or drop them). Then decide the recovery endpoints' fate deliberately: they are the better-specified retry surface (eligibility reasons, inspection-only guidance) and may deserve to come back as the retry control *inside* the blocked facet rather than being quietly retired.

### F-09 — The "Modules inventory may be stale" note names the wrong cause · **Low**

**Confirmed behavior.** The closure report and `current-inventory.md` record: *"Modules inventory may be stale until `/modules` has been visited (`ListModulesAsync` vs discover)."*

That mechanism does not exist. `SqlModuleRegistryService` is the only `IModuleRegistryService` implementation (registered `AddScoped` in `ServiceCollectionExtensions.Governance.cs:121` and WorkerHost `Program.cs:88`). `ListModulesAsync` calls `DiscoverDescriptorsAsync` on **every** invocation — iterating DI-registered `IModule` instances and calling `BuildDescriptorAsync` — then merges `ModuleInstallationStates`. `DiscoverModulesAsync` does the same discovery plus an audit record. There is no cache, no memo, and nothing that visiting `/modules` could warm.

The observable symptom is real, but its cause is elsewhere: `InventoryQueryService.MapModule` sets `UpdatedAtUtc: null`. The default sort is `updated` descending with `?? DateTimeOffset.MinValue`, so **every module sorts last**, and with `take` = 100 they are the first rows truncated once integrations/connections/schedules fill the page (F-04). Modules also require `CanManageModules` to appear at all.

**Evidence.** `SqlModuleRegistryService.cs:32-60,201-214`; `InventoryQueryService.cs:89, MapModule` (`UpdatedAtUtc: null`), `Sort` default branch, `NormalizeTake`.

**Impact.** Low functionally; the misdiagnosis is what matters — it would send the next engineer looking for a cache that isn't there.

**Recommended disposition.** Correct the note in both documents. Smallest sound fix: give modules a real timestamp (e.g. `ModuleInstallationStates` last-changed) or sort null-updated rows by name within their type instead of dumping them at `MinValue`. Not a blocker for HUB-1.

### F-10 — Inventory endpoint 500s on any non-authorization source failure · **Low**

**Confirmed behavior.** `TryCollectAsync` catches `UnauthorizedAccessException` only (rolling back partial rows). Any other exception from any of seven sources propagates out of `QueryAsync` and fails the whole request. The retired Dashboard had the opposite posture — `DashboardDefaultViewService` isolates provider failures and appends a `dashboard-partial-load` card with `ProviderFailureCount`.

**Evidence.** `InventoryQueryService.cs` `TryCollectAsync`; `Pages/Config/Inventory.razor` `LoadAsync` catch → `_error = ex.Message` (raw exception text shown to the operator).

**Impact.** One unhealthy source makes the explorer look entirely broken. Also leaks exception messages into the UI.

**Recommended disposition.** Adopt the dashboard posture — per-source isolation plus a visible "some sources unavailable" indicator. Bundle with F-04.

### F-11 — Inventory page interaction gaps · **Low**

- The search `<input>` uses `@bind:event="oninput"` but nothing reloads on input, and there is no `<form>` or `@onkeydown` — so typing does nothing and **Enter does nothing**; the operator must find the `Apply` button. Keyboard-first and screen-reader users have no submit path.
- Filters are read from the query string in `OnInitializedAsync` but never written back, so Inventory views are not bookmarkable or shareable and browser Back does not restore state. (Inbound deep links like `?type=connection` do work.)
- `_statusOptions` is derived from the already-filtered, already-truncated result set, so the status dropdown collapses to the selected value (partially mitigated by the append-fallback) and cannot offer statuses that exist only beyond the take limit.

**Evidence.** `Pages/Config/Inventory.razor` (search input, `LoadAsync`, `_statusOptions`).

**Recommended disposition.** Add Enter-to-apply and write filters back to the URL. Fold the status-options source into F-04.

### F-12 — Module contributions attach flat, carry no capability, and get no rail highlight · **Observation**

No production module implements `IModuleNavigationProvider` (only unit-test stubs), so none of this bites today. Recorded so it is a decision rather than a discovery later:

- Contributions are appended directly to `domain.Items`, so a Config contribution lands as a **top-level** Config item beside Overview/Inventory/Governance/Attention/System, bypassing the System / Developer & Internal grouping the ADR establishes.
- Contributions get no `RequiredCapability`, so the capability filter can never hide them.
- Dedupe is exact-`Href` against **top-level** items only; a contribution colliding with a nested route (e.g. `/config/notifications/definitions`) is not deduped.
- `RouteToSectionResolver`'s fallback loop skips `CustomZoneOwners`, so a module route contributed to Control Center or Config gets **no rail highlight** on its own page.

**Evidence.** `MenusController.cs:40-60`; `RouteToSectionResolver.cs` fallback loop; `SectionOwnerConstants.KnownRailDomains`.

**Recommended disposition.** NAV-1 constrained this correctly and without an SDK break — the host-side `KnownRailDomains` gate is the right shape. **Retain in constrained form** for now; do not deprecate. It is a genuinely useful extension point once it can target a known surface, and the natural successor is RES: replace `NavigationContributionDescriptor` with Resource Catalog contributions (a module declares resources/capabilities; the shell decides placement) rather than removing it and re-adding a nav SPI later. Revisit at RES, not before. If a module needs nav before then, add an optional `RequiredCapability` and a parent-item id to the descriptor rather than letting contributions land flat.

### F-13 — Residual duplication, dead code and terminology leaks · **Observation**

- `ManagementBreadcrumbResolver.Operations(...)` and `Automation(...)` are now byte-identical (`Control Center → /control/center`). Two names, one behavior; `StripAutomationRunsRoot` special-cases both label strings.
- `MatchControlBlockedWork` passes `RunStatusFacets.BlockedRunsHref` as the terminal crumb's href, which `Trail()` unconditionally discards. Harmless but signals intent that does not happen.
- `SectionOwnerConstants.Dashboard = ControlCenter` is a compatibility alias kept for pre-NAV-1 tests and leftover strings; `NavRail._isDashboardActive` still carries the old name.
- `DomainMenuCapabilityTokens.ViewConnections` / `ManageSchedules` are declared and mapped in `MenusController.HasCapability` but attached to no menu item (Connections/Schedules are off-rail). They are used by `InventoryQueryService`, so not dead — just no longer menu tokens.
- Business Data pages use `@layout ConfigLayout`. `ConfigLayout` is a no-op wrapper over `MainLayout` emitting `.config-host` / `.config-content`, and **neither class is defined in any stylesheet**. Cosmetically harmless; semantically wrong for a non-Config rail domain, and it will be copy-pasted by the next collection.
- Operator-facing internal terminology: `DemoCustomerDetail.razor` title fallback is `"Demo customer"`; `DemoCustomerUiHelpers.EmptyConflictsMessage` reads *"No demo customers currently have soft conflicts…"*. Everything else was correctly re-worded to "Customers" + a `Demo` `StatusPill`.
- `InventoryPageTests` asserts on raw `.razor` **file text** (`content.Should().Contain("@page \"/config/inventory\"")`). Consistent with the existing `Uix2…` pattern, but these tests break on formatting rather than behavior.

### Things I checked and found clean

- Design system respected: no hardcoded colors, `bg-white`/`text-gray`, hex literals, or raw `dark:` variants in any NAV-1-touched page. All new markup uses `ui-input`, `ui-input--narrow`, `ui-data-table`, `StatusPill`, `PageHeader`, `ManagementSection`, `EmptyState`, `text-muted`/`text-heading` tokens — all defined in `management-ui-primitives.css`. Theme handling is unchanged, so dark/light is inherited, not re-implemented.
- Accessibility preserved: logo keeps `role="button"`, `tabindex="0"`, Enter/Space `@onkeydown`, and gained a correct `aria-label="Control Center"`. `SegmentedControl` has `role="radiogroup"` / `role="radio"` / `aria-checked` and splats `aria-label` via `CaptureUnmatchedValues`. Breadcrumb parents remain plain navigable `<a href>`; the terminal crumb remains text.
- `/demo/customers…` redirects: `DemoCustomersLegacyRedirect` covers all three shapes with `replace: true`, reads the real path to disambiguate `/activity`, and correctly re-escapes the Blazor-decoded route parameter.
- NTF-C is untouched and intact. One canonical pipeline; no `UsePlatformPipeline`; no legacy Attention SMTP path. NAV-1 only re-parented the surfaces: Notifications sits under Config → Attention in both the menu and the breadcrumb `Notifications()` helper (now 3 segments: Config → Attention → Notifications), diagnostics included, personal inbox left off-rail on the top bar. No regression.
- Route ownership is predictable and tested: `Nav1OperatorInformationArchitectureTests` pins rail membership, middle order, Config hierarchy, Attention/System contents, and the route→section table for 17 paths, including that `/me/*`, `/account/*` and `/login` claim no rail.

---

## 4. Intentional gaps reviewed

| Gap | Assessment | Blocks HUB-1? |
|-----|-----------|---------------|
| **No browser smoke** | Real but low-risk. Test coverage of menu shape, route→section and breadcrumbs is strong (731 passing UI-shared tests), and the changes are declarative. What tests cannot catch is layout/panel behavior: the `NavRail` middle-domain icons are now *selectors* rather than direct links (`Href = null`, comment: "Treat every top-level domain icon as a domain selector"), so clicking Integrations opens the panel **without navigating**, while the logo and gear both open *and* navigate. That asymmetry deserves a human eye. | **No** — but walk login → Control Center → logo → Integrations → Business Data/Customers (+ `/demo/customers` redirect) → `/runs?status=blocked` → Inventory → Attention → Notifications → System, in both themes, before HUB-1 ships. |
| **Modules Inventory freshness** | The documented cause is wrong (F-09); there is no cache. The real effect is null `UpdatedAtUtc` + descending sort + truncation. Cosmetic. | **No.** Fix the note now; fix the sort with F-04. |
| **Missing view-only capabilities** (Modules, Runs, Execute, Control Center overview, Developer & Internal) | **Acceptable transitional behavior, and the right call.** Inventing view tokens purely to tidy a menu would have created an authorization taxonomy ahead of the context-authorization work that has to extend it. Every one of these destinations is protected independently at the API layer. One place where the menu implies more than the backend delivers, and where an existing token *would* have sufficed: F-02 (System). `/modules` additionally has **no page-level check** at all while its menu item uses `CanManageModules` — menu-only gating, correctly called out in `current-inventory.md`. | **No** — except the F-02 sub-case, which is a 5-line filter change. |
| **Blocked facet implementation** | **Acceptable — leave it.** Reuses the established single-writer `[Blocked] ` contract rather than inventing one; SQL translation verified correct; composes with every other Runs filter server-side; richer structured evidence already persisted for a future promotion. Its weakness is test coverage, not design (F-07). | **No.** Add the SQL integration test; do not redesign. |
| **Module-navigation constraints** | Correctly constrained with no SDK break and no dead abstraction. Placement/capability/highlight details are unexercised (F-12). | **No.** |
| **Dashboard retirement/consolidation** | Consolidation itself is clean — `/dashboard` genuinely cannot be a second home, deferred panel routes were not implemented against the old model, and eight of ten card providers duplicated Control Center. The unfinished part is the disposal decision for the two unique providers plus the endpoint, and three documents currently describe an outcome that was not built (F-01). | **No**, but resolve before DSH so the panel engine starts from a stated baseline. |

---

## 5. Architectural debt / risk register

Speculative items are labelled.

**HUB-1 — Integration Operations Hub**
- F-05 (status facet drops sibling filters) sits directly on HUB-1's main flow: Integration hub → filtered runs. Fix first.
- F-03 (Config → Inventory breadcrumb ancestry for Connections/Schedules) will be exercised hard by HUB-1 drill-ins, and every new link inherits the wrong parent.
- `OperationRuns` has one index (`RetryOfRunId`). Per-integration run aggregation means `WHERE IntegrationId = … ORDER BY CreatedAt DESC` over an unindexed table. Decide on indexes as part of HUB-1, not after.
- Integration↔Connection↔Schedule correlation is string columns with no FKs; aggregation queries must be written defensively (orphan `IntegrationId` values are possible by construction).
- *Speculative:* if HUB-1 introduces an aggregate readiness/summary endpoint, it will be tempted into the same "fan out and merge in memory" shape as `InventoryQueryService` — inheriting F-04's truncation and F-10's failure posture. Settle that pattern once.

**CTX — ResourceContext**
- Connections are the first thing CTX has to reparent (instance-scoped `ScopeKey`/`OwnerId` today, no `IntegrationId`). `Bindings` has **no unique index** on `(IntegrationId, ModuleId, SlotName)`; the one-Active-binding rule lives in `BindingService`. Adding context scoping to an invariant that is only enforced in application code is a hazard.
- `current-inventory.md` §11 already catalogues the unique indexes that would block the same key in two contexts, plus the lookup methods that assume single-context. That section is the right starting point — re-verify it against code before planning.
- Context-scoped rail/breadcrumb resolution will collide with `RouteToSectionResolver`'s hard-coded prefix table and `ManagementBreadcrumbResolver`'s ordered rule array. Both are currently path-shape-driven with no notion of a scoping segment. F-03 should be settled before that, not during.
- `OperatorCapabilitiesDto` is a flat ~28-boolean projection with positional constructor parameters. Context-scoped capability answers will not fit that shape. Expect to need a keyed/queried capability API; the `RequiredCapability` string token in `DomainMenuItem` is actually the better-shaped half and can survive.

**RES — Resource Catalog**
- `IModuleNavigationProvider` is the natural thing to subsume (F-12). Keep it constrained until then.
- Inventory is deliberately *not* a catalog, and that boundary held. The risk is drift: each new type added to `InventoryTypeTokens` makes it look more like one. Keep the read-DTO-only rule explicit in whatever supersedes it.
- Connector kinds live in an in-memory `IConnectorDefinitionRegistry` (the SQL table was dropped) — relevant precedent for how RES should hold definitions vs instances.

**DAT — Business Data Collections**
- The transitional landing scales acceptably to a handful of collections (one card per collection on `/business-data`) but three things will not: `ConfigLayout` on Business Data pages (F-13), `DemoCustomer*` type/route/helper naming as the de facto collection API, and `BusinessDataUiRoutes` hardcoding `Customers` as a constant rather than a collection key. A second collection is the moment to generalize.
- Customers is gated by `integration.read` — correct today (that's what `DemoBusinessRecordProjectionService` enforces) and exactly wrong under "Business Data is not Integration-owned." Menu token and backend must move together.
- Provenance survives via `SharedRecordIdentity` on `OperationItems` with a **non-unique** index. Per-record provenance queries at volume will need attention.
- Terminology leaks (F-13) are trivial now and become migration noise later.

**SVC — Services**
- No Service aggregate exists (`ResourceTypeConstants.ServiceAccount` is an auth type only). Greenfield — the main constraint inherited from here is that typed Connection bindings must go through `Bindings`, whose slot invariant is application-level (see CTX).

**DSH — Dashboard Panels**
- F-01 is the entry condition: ten card providers and an endpoint with no consumer, plus docs describing a Config-overview integration that was never built. Resolve the disposal decision before DSH, or DSH will start by reverse-engineering what the cards were for.
- Useful precedent, if kept: `DashboardDefaultViewService`'s per-provider failure isolation and `DashboardCardAttentionConvention` ranking are both better than what Inventory does (F-10).
- Card `Type` is always `"summary"`; no chart library exists anywhere in the repo. DSH starts from zero on visualization.

**WIZ / workflow work**
- Workflows are code-declared graphs; `/workflows/start` is a stub to `/execute?kind=workflow`. Visual builder is explicitly off-roadmap. No NAV-1 debt here.
- *Speculative:* whatever promotes blocked evidence from `FailureSummary` prefix to structured state (F-07) probably belongs to execution-domain work rather than WIZ; the `FailureDiagnosticsJson` `{blocked:true,…}` payload is the ready-made input.

---

## 6. Recommended immediate sequence

The agreed sequence is not flawed. Four steps, then HUB-1.

1. **Make `main` green.** Fix the two NTF-C count assertions 17 → 16 (F-06). Prefer membership assertions over exact counts. One commit, minutes.

2. **NAV-1 follow-up batch — code.** Small, contained, all in areas HUB-1 touches:
   - F-02: drop a menu parent whose `Href` matches a filtered-out child's `Href`; add the test.
   - F-05: preserve sibling query parameters when the Runs status facet changes.
   - F-08: fix the two circular "Retry from blocked runs" links on run detail.
   - F-07: add a SQL Server integration test for `status=blocked` (blocked / plain-failed / succeeded).
   - F-11: Enter-to-apply on Inventory search.

3. **NAV-1 follow-up batch — decisions + docs.** Two real decisions, then correct the record:
   - Dashboard providers (F-01): surface the two unique cards on Config overview, or retire endpoint + providers explicitly.
   - Connections/Schedules breadcrumb ancestry (F-03): correct it, or amend ADR-0040 to state the aggregation-as-ancestry choice deliberately.
   - Docs: fix the Config-overview claim in `mapping.md`, `control-panel-ui.md` and `current-inventory.md` §9; replace the Modules-freshness note with the real cause (F-09).

4. **Proceed to HUB-1**, scoping in two things the audit surfaced: an `OperationRuns` index decision, and the aggregation-endpoint shape question (F-04/F-10) so HUB-1 does not clone Inventory's truncation-and-500 posture.

Do not reopen NTF-C, and do not let step 3 expand into CTX/RES design.

---

## 7. Handoff notes

**Single sources of truth.** `ControlPanelMenuDefinitions.GetDefaultMenus()` for menus — nothing else may define a rail domain. `SectionOwnerConstants` for domain identity, custom-zone owners and `KnownRailDomains`. `ShellSectionRoutes` / `BusinessDataUiRoutes` / `RunStatusFacets` for canonical routes and the blocked href; no string literals for these anywhere else. `RouteToSectionResolver` for rail highlight, `ManagementBreadcrumbResolver` for ancestry — **these are independent mechanisms and can disagree** (F-03 is exactly that). `docs/01-architecture/transitions/application-contexts/current-inventory.md` is the best map of everything else; it is accurate except F-01 and F-09.

**Conventions worth not breaking.**
- Menu items gate by a `RequiredCapability` string token mapped in `MenusController.HasCapability` to a real `OperatorCapabilitiesDto` boolean. Never add a token without a backing permission evaluation. Menu hiding is never the security boundary — pages and APIs enforce independently, and `DomainMenuService` falls back to the **unfiltered** defaults when `/api/menus` fails, by design.
- Breadcrumb rules are an **ordered, most-specific-first** `PathMatcher[]`. Insert new rules in the right position; `Trail()` silently drops the terminal href.
- Blazor shell timing is load-bearing: `await Task.Delay(1)` / `Task.Yield()` before `StateHasChanged` or `NavigateTo` in nav handlers, `ControlPanel` is not mounted while closed, pages `await DomainMenuService.WhenFetchedAsync()` before their own HTTP. These are UIX-2 fixes for real orphaned-component crashes — don't "clean them up."
- Styling goes through `management-ui-primitives.css` tokens (`ui-input`, `ui-data-table`, `text-muted`, `text-heading`, `StatusPill`). `app.css` is **generated** from `Apperture.Management.Ui.Client/styles/` by `build-ui` — edit the sources. The dev host `_Layout.cshtml` must load `management-ui-primitives.css` after `app.css`.
- Operator objects use 24-hex-char string PKs (`Guid.NewGuid().ToString("N")[..24]`), stored `nvarchar(64)`. Cross-object references are **string columns with indexes, not FKs** — only identity, teams/contacts and `OperationGroups→OperationItems` have real EF FKs.
- `[Blocked] ` (`RunFailureCapture.DeferredWorkBlockedPrefix`) is a **write contract**: every writer of blocked deferred evidence must prefix `FailureSummary`. Three readers depend on it (`RunQueryService`, `RuntimeHealthQueryService` ×2).

**Tests to run and to keep green.** `Apperture.Management.Ui.Shared.Tests` (731) is the navigation contract suite — `Nav1OperatorInformationArchitectureTests`, `ShellSectionOwnerTests`, `Epic101ManagementBreadcrumbResolverTests`, `Epic99RouteMenuTruthCleanupTests`, `S3ProductShellNavigationTests`. `Apperture.ArchTests` (38) holds the layering boundaries, including `ControlPlane.Security` ⇏ `Management.Ui.Shared`. `InventoryQueryServiceTests` (447 lines, fake-based) covers Inventory filter/sort/capability. **Run `Apperture.UnitTests` unfiltered** — filtered runs are how F-06 stayed hidden through two closure reports.

**Boundaries that must survive the next epics.** UI aggregation is not domain ownership — Inventory and Control Center may *list* connections, schedules, runs and collections without owning them (F-03 is the current violation, in breadcrumbs only). Config Inventory must stay a read DTO/facade; no persisted universal resource. Business Data is not Integration-owned, even though today's authorization says otherwise. Blocked stays derived. Modules may not create rail domains. And nothing in HUB-1 introduces `ResourceContext`.
