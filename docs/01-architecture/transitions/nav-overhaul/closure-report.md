# NAV-1 closure report

Date: 2026-09-11  
Status: **Complete.** Canonical operator rail is Control Center, Integrations, Business Data, Config.

Do not reopen NTF-C. Do not start HUB-1, ResourceContext, Resource Catalog, Applications/Services, Dashboard Panels, or the final Business Data collection architecture from this closure.

## 1. What shipped

- Rail: logo → Control Center; middle → Integrations, Business Data; gear → Config.
- Landings: `/` and post-login → `/control/center`. `/dashboard` redirects to Control Center.
- Blocked work → `/runs?status=blocked` (derived facet). `/control/blocked-work` redirects. Recovery APIs unchanged.
- Business Data landing + Customers at `/business-data/customers` with a demo badge. `/demo/customers` redirects. Developer Tasks no longer lists Customers.
- Config: Overview, Inventory (`GET /api/inventory`), Governance, Attention (Notifications including diagnostics), System.
- Capability-aware `GET /api/menus`. Module contributions only attach to known rail domains.
- Breadcrumbs, login return path, and catalog links follow the new map.

Working map: [mapping.md](mapping.md). Live contract: [control-panel-ui.md](../../control-panel-ui.md). Decision: [ADR-0040](../../../02-decisions/ADR-0040-operator-information-architecture.md). Historical snapshot: [before.md](before.md).

## 2. Destination outcomes

| Old | New |
|-----|-----|
| `/`, logo, `/dashboard` | `/control/center` |
| `/control/blocked-work` | `/runs?status=blocked` |
| `/demo/customers…` | `/business-data/customers…` |
| Connections / Schedules rail | Off-rail; Config Inventory + Integration links |
| Notifications under Config root | Config → Attention → Notifications |
| Platform maintenance | Config → System |
| Demo Customers under Developer | Business Data → Customers (demo-flagged) |

Pages and APIs for connections, schedules, runs, governance, attention, and notifications were **relocated in the shell**, not rewritten as new aggregates.

## 3. Architecture constraints kept

- UI aggregation ≠ domain ownership.
- Blocked is a facet (`failed` + `[Blocked] ` prefix), not a new column.
- Customers remain `DemoBusinessRecordProjection`; no `BusinessRecord` table.
- Capability hiding is not a substitute for API/page auth.
- `Apperture.ControlPlane.Security` does not reference `Apperture.Management.Ui.Shared`.

## 4. Tests and builds

Executed 2026-09-11 (this continuation). Management Host pid 7828 and WorkerHost pid 60204 were stopped so Host/Worker binaries could copy.

| Suite | Result |
|-------|--------|
| `Apperture.Management.Ui.Shared.Tests` (full) | **731 passed** |
| UnitTests filter Epic33 / DashboardExpansion / EntraSignIn / ManagementAuthEndpoints / Epic48 / Inventory / LoginRouteHelper / RuntimeVisibleDevMetadata | **150 passed** |
| `Apperture.ArchTests` (full) | **38 passed** |
| Builds: Ui.Shared, Security, Management.Api, Management.Host (`--no-dependencies`), UnitTests, ArchTests, WorkerHost, Ui.Client | **succeeded** |

## 5. Intentional gaps / deferred

- Integration Operations Hub
- ResourceContext / CTX
- Resource Catalog / RES
- Final Business Data Collections / DAT
- Dashboard Panel engine (`/dashboard/manage` etc. stay deferred under Developer & Internal)
- Applications / Services
- No view-only capability invented for Modules, Runs, Execute, Control Center overview, or Developer & Internal
- ~~Modules inventory may be stale until `/modules` has been visited (`ListModulesAsync` vs discover)~~ — **incorrect as stated; see §8.**

## 6. Browser smoke

Not run in this continuation. No browser automation in the agent session. Management Host and WorkerHost were stopped to clear DLL locks for the Host rebuild; they were not restarted here. Restart the Aspire/dev stack and walk login → Control Center, logo, Integrations, Business Data/Customers (including `/demo/customers` redirect), Runs blocked facet, Execute, Inventory, Governance, Attention → Notifications, System, and the personal inbox in dark and light themes.

## 7. Confirmation

**The operator shell uses the four-domain rail. Control Center is the only home. Blocked work is a Runs facet. Customers live under Business Data as a demo-flagged collection. Config Inventory is an explorer, not a Resource Catalog.**

## 8. Post-review stabilization (2026-09-11, same day)

An independent review (`docs/audits/nav1-architecture-review.md`) found NAV-1 architecturally sound with a short list of fixable findings and one pre-existing, unrelated red-test issue. This section records the fix pass taken in response. No architectural decision from §1–§7 changed.

1. **Stale NTF-C test assertions (unrelated to NAV-1).** `PlatformSettingDefinitionRegistryTests` and `GatedPlatformSettingDefinitionRegistryTests` still expected 17 platform-setting definitions after NTF-C removed `Notifications.UsePlatformPipeline.Enabled` (17 → 16) without updating the two count assertions. Both closure reports had run `UnitTests` with a `--filter`, so this stayed hidden. Corrected to 16; full unfiltered `Apperture.UnitTests` is green (was 1865/2 failed, now 1881/0 failed after also adding the coverage below).
2. **Blocked-facet test coverage.** The derived Runs `status=blocked` facet (`RunFailureCapture.DeferredWorkBlockedPrefix`, unchanged from Epic 27/30) had no server-side tests. Added: fast EF-InMemory tests on `RunQueryService` for blocked-run recognition, non-blocked-failed exclusion, null-`FailureSummary` exclusion, and composition with `integrationId`/`correlationKey`; and a real SQL Server integration test (`RunsBlockedFacetSqlServerTests`) proving EF Core's `StartsWith` → `LIKE ... ESCAPE '\'` translation actually escapes the `[` in `[Blocked] ` against a live provider (the pre-existing mechanism was not redesigned).
3. **Runs status-facet URL composition.** Changing the Runs status dropdown rebuilt the URL from scratch, silently dropping `correlationKey`/`createdFrom`/`integrationId`/`moduleId`. Extracted to a testable `Epic33UiDisplayHelpers.BuildRunsListUrl` helper that preserves sibling filters and changes only `status`.
4. **Capability-aware menu parent/child href mismatch.** `Config → System`'s own `Href` equalled its "Platform settings" child's `Href`; when that capability was denied, System stayed a clickable dead end. `DomainMenuCapabilityFilter` now nulls a parent's `Href` when it matches a child that capability filtering removed (kept as an expandable node over any remaining children, dropped if none remain). No capability invented. `Attention` is correctly unaffected — its matching child (`Operational teams`) carries no capability gate today.
5. **Connections/Schedules breadcrumb ancestry.** Breadcrumbs asserted `Config → Inventory → Connections` and `Config → Inventory → Schedules`, contradicting ADR-0040 rule 3 (first-class, off-rail resources; Inventory discovers, does not own). Corrected to a flat root (`Connections`, `Schedules`), mirroring how `Integrations` already renders. Rail highlight (Config, via `RouteToSectionResolver`) is unaffected — this was a breadcrumb-only defect.
6. **Modules Inventory freshness — real cause.** The original note above ("`ListModulesAsync` vs discover") was wrong: `SqlModuleRegistryService.ListModulesAsync` discovers from DI on every call; there is no cache to warm by visiting `/modules`. The actual cause was `InventoryQueryService.MapModule` always reporting `UpdatedAtUtc: null`, which sinks modules to the bottom of the default "updated desc" sort and lets them fall past the take limit once other resource types fill the page. Fixed: `ModuleCatalogEntry` now carries a real `UpdatedAtUtc` sourced from `ModuleInstallationStates.UpdatedAt` for installed modules (still `null`, not fabricated, for modules that are discovered but never installed). Separately, `InventoryQueryService`'s default cross-type browse now guarantees at least one representative of every authorized type survives the take limit, so a whole resource family (Modules or otherwise) cannot silently disappear behind higher-volume/more-recently-updated types; an explicit `type=module` filter already returned every match before this change (filtering runs before the take limit).
7. **Dashboard disposition, decided.** `GET api/dashboard/default`, `DashboardDefaultViewService`, and the ten `IDashboardCardProvider` implementations have no production UI caller — confirmed by exhaustive reference search, not assumed. They are **not removed** in this pass (a safe removal spans a whole project plus dedicated unit tests — a separately-reviewable change, not a stabilization edit) but are now clearly documented as orphaned at the source (`DashboardController`, the DI registration block) so nothing builds against them by accident. The one genuinely useful signal they carried — the auth "no viable sign-in path" warning — is restored on Config overview via a new small, capability-safe helper (`ConfigAuthPostureUiHelpers`) that reads `GET api/config/auth-overview` directly; this is not a revival of the card framework. The governance role-count card was not restored (lower value; one click from `/config/governance`). `mapping.md`, `control-panel-ui.md`, and `current-inventory.md` §9 corrected to stop describing the retired card framework as still feeding Config overview.
8. **Blocked Work recovery link/API cleanup.** Run detail's "Retry from blocked runs" link promised a retry action that `/runs?status=blocked` does not offer; replaced with plain text pointing at the page's own "Retry (new run)" toolbar button (no duplicate recovery UX introduced). The list/retry recovery endpoints (`GET/POST api/control/recovery/blocked-work...`) have no UI caller either, but are a documented API contract (`docs/04-api/contracts.md`) with richer eligibility/inspection-only semantics than the surviving `runs/{id}/retry` path — kept, documented at the source as a candidate for a future execution-domain UI restoration rather than removed.
9. **Middle-rail interaction (logo/gear navigate; Integrations/Business Data are selector-only).** Reviewed and **retained as-is**, based on code-level reasoning only — no live browser session was available in this pass to judge interaction "feel" (see §10). Control Center and Config are fixed custom-zone owners with one canonical landing each, so both navigate on click. Middle-loop domains are deliberately selector-only regardless of today's item count (`NavRail.BuildMenuItems`'s own comment: "Treat every top-level domain icon as a domain selector"), because that count is not guaranteed to stay small — Integrations has one item today only incidentally, and module contributions could add more. Making a domain's click behavior depend on its current item count would itself be an inconsistency users would have to relearn as menus change. Recommend a real click-through pass before or shortly after HUB-1 ships more items into these domains, since that will exercise this more.
10. **Browser smoke — not performed.** No browser-automation tool was available in this session (confirmed: no Playwright/Selenium/screenshot capability). What could be verified without one: every project touched by this stabilization pass builds individually (`Ui.Shared`, `ControlPlane.Security`, `Persistence.SqlServer`, `Management.Api`, `Management.Host`, `Ui.Client`, `RuntimeHost.Windows.WorkerHost`); the full solution build was attempted but blocked by a pre-existing, already-running `Apperture.AppHost` process holding a file lock on its own executable — a known environmental quirk (both the NTF-C and original NAV-1 closure reports record stopping Management Host/WorkerHost processes for the same reason), not a regression from this pass, and the AppHost was left running rather than killed since it was not started by this session and may be in active use. Visual rendering, dark/light theming, and click-through interaction (including the item 9 decision above) remain **unverified by a human or automated browser** and should be walked before HUB-1 ships.

### Validation (post-review stabilization)

| Suite | Result |
|-------|--------|
| `Apperture.UnitTests` (full, unfiltered) | **1881 passed, 0 failed** |
| `Apperture.Management.Ui.Shared.Tests` (full) | **740 passed** |
| `Apperture.ArchTests` (full) | **38 passed** |
| `Apperture.IntegrationTests` filter Runs-blocked-facet / RuntimeHealth-blocked-deferred / Recovery-workflow-remediation (SQL Server, live DB) | **3 passed** |
| Builds: `Ui.Shared`, `ControlPlane.Security`, `Persistence.SqlServer`, `Management.Api`, `Management.Host`, `Ui.Client`, `RuntimeHost.Windows.WorkerHost` (individually) | **succeeded** |
| Full solution build (`Apperture.slnx`) | **blocked** by a pre-existing running `Apperture.AppHost` process file lock (see item 10) — not attempted further |

### Confirmation

**`main`'s unit-test baseline is green. The blocked facet, Modules Inventory, Runs filter composition, capability-aware menus, and Connections/Schedules breadcrumbs are corrected and covered. The Dashboard card framework's disposition is decided (documented, not removed) and its one useful signal is restored elsewhere. Nothing in §1–§7's architectural decisions changed. Browser-level verification of navigation feel and visual rendering is still outstanding and should happen before HUB-1 ships.**
