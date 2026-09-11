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
- Modules inventory may be stale until `/modules` has been visited (`ListModulesAsync` vs discover)

## 6. Browser smoke

Not run in this continuation. No browser automation in the agent session. Management Host and WorkerHost were stopped to clear DLL locks for the Host rebuild; they were not restarted here. Restart the Aspire/dev stack and walk login → Control Center, logo, Integrations, Business Data/Customers (including `/demo/customers` redirect), Runs blocked facet, Execute, Inventory, Governance, Attention → Notifications, System, and the personal inbox in dark and light themes.

## 7. Confirmation

**The operator shell uses the four-domain rail. Control Center is the only home. Blocked work is a Runs facet. Customers live under Business Data as a demo-flagged collection. Config Inventory is an explorer, not a Resource Catalog.**
