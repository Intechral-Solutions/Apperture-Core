# NAV-1 destination map

Working record for the operator information-architecture change. Runtime source of truth after ship: `ControlPanelMenuDefinitions`, `RouteToSectionResolver`, `control-panel-ui.md`.

Legend: **redirect** = `NavigateTo(..., replace: true)`; **alias** = same page, new canonical URL preferred; **keep** = same URL; **new** = added in NAV-1.

| Old destination | New destination | Redirect / alias | Functionality |
|-----------------|-----------------|------------------|---------------|
| `/` | `/control/center` | **redirect** | Operator landing |
| Logo | `/control/center` | **keep behavior, new target** | Sole operator home |
| `/dashboard` | `/control/center` | **redirect** | Dashboard no longer a competing home |
| `/dashboard/manage`, `/add`, `/layout`, `/delete` | unchanged URLs | **keep** | Deferred Dashboard Panel work; Developer & Internal only |
| `/control/center` | `/control/center` | **keep** | Control Center overview |
| `/control/blocked-work` | `/runs?status=blocked` | **redirect** | Blocked is a Runs facet; recovery preserved |
| `/execute` | `/execute` | **keep** | Control Center menu |
| `/control/audit` | `/control/audit` | **keep** | Control Center + Inventory Activity |
| `/runs` | `/runs` | **keep** | Control Center (not Config) |
| `/runs/{id}` | `/runs/{id}` | **keep** | Includes blocked retry when applicable |
| `/schedules`, `/schedules/{id}` | same | **keep** | Off-rail; Inventory Resources + Integration links + Control Center |
| `/connections`, `/connections/{id}` | same | **keep** | Off-rail; Inventory Resources + Integration links |
| `/integrations` | `/integrations` | **keep** | First-class rail |
| `/demo/customers` | `/business-data/customers` | **redirect** | Customers collection (demo-flagged) |
| `/demo/customers/{key}` | `/business-data/customers/{key}` | **redirect** | Provenance preserved |
| `/demo/customers/{key}/activity` | `/business-data/customers/{key}/activity` | **redirect** | Provenance preserved |
| `/demo/nested-nav` | same | **keep** | Developer & Internal |
| `/config` | `/config` | **keep** | Overview of Inventory / Governance / Attention / System |
| `/config/inventory` | **new** | — | Config → Inventory |
| `/config/governance/*` | same | **keep** | Config → Governance |
| `/config/notifications/*` | same | **keep** | Config → Attention → Notifications |
| `/attention/*`, `/config/contacts` | same | **keep** | Config → Attention |
| `/config/auth/*` | same | **keep** | Config → System → Authentication |
| `/config/platform-settings` | same | **keep** | Config → System |
| `/modules` | same | **keep** | Config → System |
| `/config/developer` | same | **keep** | Config → System; Demo Customers removed |
| `/me/notifications`, `/account/*` | same | **keep** | Personal / top-bar; not rail |
| `/operations/start`, `/workflows/start` | `/execute?...` | existing stubs | Unchanged |
| `/governance/*` | `/config/governance/*` | existing redirects | Unchanged |

## Rail (after)

```
[Logo] Control Center
Integrations
Business Data
----------------
[Gear] Config
```

Operations, Automation, Connections, and Dashboard are not rail domains.

## Dashboard cards

| Provider | Decision |
|----------|----------|
| Runtime Health, Blocked Work, Integrations, Needs Attention, Executions, Recent Activity, Schedules, Audit | Already on Control Center (or its drill-ins). Do not duplicate. Update Blocked Work hrefs to `/runs?status=blocked`. |
| Governance, Authentication Posture | Unique vs Control Center. **Post-review stabilization:** the authentication "no viable sign-in path" warning is surfaced on Config landing via `ConfigAuthPostureUiHelpers` (a small, capability-safe summary reading the same `IAuthenticationAdminOverviewService` data the old `auth-warnings` card used) — not the retired dashboard card framework. Governance's role-count summary was not restored (lower-value; role management itself is one click away at `/config/governance`). Canonical pages remain `/config/governance` and `/config/auth`. |

Dashboard Panel engine (`/dashboard/manage` etc.) stays deferred.

## Capability-aware menus

Hide menu items when an established view/manage capability exists and the operator lacks it. Page/API authorization remains independent. Gaps (no view capability today, not invented here): Developer & Internal, Runs list, Execute, Control Center overview, Modules (manage-only).

## Module navigation

`GET /api/menus` no longer creates new rail domains. Contributions may attach only to known domains: Control Center, Integrations, Business Data, Config. Other domains are ignored.

## Not in this epic

HUB-1, ResourceContext / CTX, Resource Catalog / RES, final Business Data Collections / DAT, Dashboard Panels / DSH, Applications / Services / SVC, workflow IA beyond existing Execute.
