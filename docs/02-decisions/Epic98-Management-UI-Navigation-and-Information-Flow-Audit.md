# Epic 98 — Management UI Navigation and Information Flow Audit

## Status

Accepted — May 2026 (audit and recommendations only; no shell/route/menu implementation).

## 1. Purpose

Epics 78–97 improved page structure, interaction primitives, entity-local recent activity, ActionFeedback, and visual treatment on pilot surfaces. Operators can read individual pages more easily, but **overall information flow** — how domains, menus, hubs, lists, details, diagnostics, and deep links connect — is still incremental and uneven.

This epic produces a **grounded navigation and flow map** before any shell/domain restructure or broad non-pilot facelifts. It inventories routes and menu truth, classifies pages by archetype and hub relationships, documents major user journeys and friction, and recommends a future navigation model and follow-up epics.

**Relationship to Epic 78:** [Epic 78](Epic78-Management-UI-Modernization-Groundwork.md) established archetypes and a route inventory snapshot. Epic 98 **refreshes** that inventory post–Epic 97, focuses on **menu ↔ route ↔ journey** alignment, and adds **hub/detail/diagnostic** and **target navigation** recommendations. No route or menu code changes in this epic.

## 2. Scope

| Source | Reviewed |
|--------|----------|
| Blazor routes | All `@page` in `Apperture.Management.Ui.Client/Pages/**/*.razor` (May 2026) |
| Menu definitions | `ControlPanelMenuDefinitions.cs`, `GET /api/menus` (`MenusController`) |
| Section resolution | `RouteToSectionResolver.cs`, `SectionOwnerConstants`, `NavPathMatching`, `DomainMenuTree` |
| Shell | `NavRail.razor`, `ControlPanel.razor`, `MainLayout.razor`, `ConfigLayout.razor` |
| Architecture | [control-panel-ui.md](../01-architecture/control-panel-ui.md), [management-ui-design-system.md](../01-architecture/management-ui-design-system.md) |
| Prior modernization | Epics 78, 85, 89–97 ADRs |

**Out of scope:** Shell rewrite, route moves, menu edits, page redesigns, backend/API/permission changes.

## 3. Current domain / menu inventory

Canonical menu: `ControlPanelMenuDefinitions.GetDefaultMenus()` (API merges module `NavigationContribution` items into matching domains).

| Domain (rail) | Menu label | Route (`Href`) | Component / notes |
|---------------|------------|----------------|-------------------|
| **Dashboard** (logo) | Dashboard home | `/dashboard` | `DashboardHome.razor` |
| Dashboard | Manage dashboards (deferred) | `/dashboard/manage` | `ManageDashboards.razor` — placeholder |
| Dashboard | Add dashboard page (deferred) | `/dashboard/add` | `AddDashboardPage.razor` |
| Dashboard | Delete dashboard page (deferred) | `/dashboard/delete` | `DeleteDashboardPage.razor` |
| Dashboard | Edit dashboard layout (deferred) | `/dashboard/layout` | `EditDashboardLayout.razor` |
| **Operations** | Control Center | `/control/center` | `ControlCenter.razor` — primary ops console |
| Operations | Blocked Work | `/control/blocked-work` | `BlockedWork.razor` |
| Operations | Audit Timeline | `/control/audit` | `AuditTimeline.razor` |
| Operations | Execute | `/execute` | `ExecutionStart.razor` — preferred start surface |
| Operations | Start Operation (Legacy) | `/operations/start` | `OperationStart.razor` |
| Operations | Start Workflow (Legacy) | `/workflows/start` | `WorkflowStart.razor` |
| Operations | API Explorer | `/tools/explorer` | **No `@page` in Ui.Client** — broken menu target |
| Operations | Nested nav demo | `/demo/nested-nav` (+ nested children) | `NestedNavDemo.razor` — dev/demo only |
| Operations | Operational teams | `/attention/operational-teams` | `AttentionContactGroups.razor` (alias `/attention/contact-groups`) |
| Operations | Attention routing defaults | `/attention/routing-defaults` | `AttentionRoutingDefaults.razor` |
| **Automation** | History | `/runs` | `RunsList.razor` |
| Automation | Scheduling | `/schedules` | `SchedulesList.razor` |
| **Integrations** | Integrations | `/integrations` | `IntegrationsList.razor` |
| **Connections** | Connections | `/connections` | `ConnectionsPage.razor` (+ embedded connectors) |
| **Config** (gear) | Config | `/config` | `ConfigLanding.razor` — hub cards |
| Config | Authentication | `/config/auth` | `AuthOverview.razor` (+ children in submenu) |
| Config → Auth | Sign-in chooser | `/config/auth/sign-in` | `SignInExperience.razor` |
| Config → Auth | Entra registrations | `/config/auth/tenants` | `EntraTenants.razor` |
| Config | Modules | `/modules` | `ModulesList.razor` (submenu duplicates same href) |
| Config → Modules | Installed Modules | `/modules` | Same list — redundant child |
| Config | Governance | `/config/governance` | `GovernanceLanding.razor` (+ children) |
| Config → Governance | Permission Catalog | `/config/governance/catalog` | `GovernanceCatalog.razor` |
| Config → Governance | Roles | `/config/governance/roles` | `RolesList.razor` |
| Config → Governance | Users | `/config/governance/users` | `UsersList.razor` |
| Config | Notifications | `/config/notifications` | `NotificationsLanding.razor` (+ children) |
| Config → Notifications | Definitions / Instances / Delivery / Diagnostics | respective `/config/notifications/*` | List/diagnostic surfaces |

**Not in default menu (intentional or gap — see §5):** `/`, `/login*`, `/account`, `/preferences`, all entity detail/create/edit routes, integration child routes, runtime shutdown routes, `/config/contacts*`, Entra directory browse, governance effective-access, legacy `/governance/*` redirects.

**Section ownership rules (`RouteToSectionResolver`):**

- `/config/**` and `/modules/**` → **Config** (gear), regardless of Automation/Operations semantics.
- `/` and `/dashboard/**` → **Dashboard**.
- Other paths → domain whose menu tree contains a matching `Href` (stable sort order).

## 4. Route inventory by area

**Legend — Archetype:** DH = Dashboard hub; CC = Operator console; HL = Hub landing; FL = Filtered list; ED = Entity detail; CR = Create; EX = Execute/start; AU = Authoring; DX = Diagnostics; AUTH = Auth/session; RD = Redirect; DEF = Deferred placeholder; DEMO = Demo; RT = Runtime admin; — = N/A.

**Legend — Menu:** ✓ = default menu; (hub) = via hub/parent only; — = not in menu.

### Shell and auth

| Route | Component | Domain | Archetype | Menu | Parent / hub |
|-------|-----------|--------|-----------|------|----------------|
| `/` | `Routes.razor` → `OperatorLanding` | Dashboard | RD | — | → `/dashboard` |
| `/dashboard` | `DashboardHome.razor` | Dashboard | DH | ✓ | Platform summary |
| `/dashboard/manage`, `/add`, `/delete`, `/layout` | Dashboard deferred pages | Dashboard | DEF | ✓ deferred | — |
| `/login`, `/login/local` | `Login.razor`, `LoginLocal.razor` | — | AUTH | — | Public |
| `/account`, `/preferences` | `Account.razor`, `Preferences.razor` | — | ED / prefs | — | User chrome |

### Operations

| Route | Component | Archetype | Menu | Parent / hub |
|-------|-----------|-----------|------|----------------|
| `/control/center` | `ControlCenter.razor` | CC | ✓ | **Operations hub candidate** |
| `/control/blocked-work` | `BlockedWork.razor` | FL | ✓ | Control Center / dashboard drill-in |
| `/control/audit` | `AuditTimeline.razor` | FL | ✓ | Control Center, run/integration detail |
| `/execute` | `ExecutionStart.razor` | EX | ✓ | Integration detail, menu |
| `/operations/start` | `OperationStart.razor` | EX | ✓ Legacy | Superseded by `/execute` |
| `/workflows/start` | `WorkflowStart.razor` | EX | ✓ Legacy | Superseded by `/execute` |
| `/tools/explorer` | — | — | ✓ | **Missing page** |
| `/demo/nested-nav{/*}` | `NestedNavDemo.razor` | DEMO | ✓ | Should not ship in operator menu |
| `/attention/operational-teams`, `/attention/contact-groups` | `AttentionContactGroups.razor` | FL | ✓ (teams label) | — |
| `/attention/.../{Id}` | `AttentionContactGroupDetail.razor` | ED | — | List |
| `/attention/routing-defaults` | `AttentionRoutingDefaults.razor` | AU | ✓ | — |

### Automation

| Route | Component | Archetype | Menu | Parent / hub |
|-------|-----------|-----------|------|----------------|
| `/runs` | `RunsList.razor` | FL | ✓ History | Dashboard, control center, entity activity |
| `/runs/{RunId}` | `RunDetail.razor` | ED | — | Runs list, drill-ins |
| `/schedules` | `SchedulesList.razor` | FL | ✓ Scheduling | Integration detail |
| `/schedules/{ScheduleId}` | `ScheduleDetail.razor` | ED | — | Schedules list |
| `/schedules/new` | `ScheduleCreate.razor` | CR | — | **Not in menu**; list toolbar if capabilities |

### Integrations

| Route | Component | Archetype | Menu | Parent / hub |
|-------|-----------|-----------|------|----------------|
| `/integrations` | `IntegrationsList.razor` | FL | ✓ | **Integrations hub** |
| `/integrations/new` | `IntegrationNew.razor` | CR | — | List (not menu) |
| `/integrations/{IntegrationId}` | `IntegrationDetail.razor` | ED | — | List, dashboard |
| `/integrations/{id}/notifications` | `IntegrationNotifications.razor` | AU/DX | — | Integration detail toolbar |
| `/integrations/{id}/notifications/definitions/new` | `IntegrationNotificationDefinitionCreate.razor` | CR | — | Integration notifications |
| `/integrations/{id}/schedule-diagnostics` | `IntegrationScheduleDiagnostics.razor` | DX | — | Integration detail / summary band |

### Connections

| Route | Component | Archetype | Menu | Parent / hub |
|-------|-----------|-----------|------|----------------|
| `/connections` | `ConnectionsPage.razor` | FL | ✓ | **Connections hub** |
| `/connections/new` | `ConnectionNew.razor` | CR | — | Connections page |
| `/connections/{ConnectionId}` | `ConnectionDetail.razor` | ED | — | Connections list |

### Config (gear owner)

| Route | Component | Archetype | Menu | Parent / hub |
|-------|-----------|-----------|------|----------------|
| `/config` | `ConfigLanding.razor` | HL | ✓ | **Config hub** |
| `/modules` | `ModulesList.razor` | FL | ✓ | Config; path `/modules` |
| `/modules/{ModuleId}` | `ModuleDetail.razor` | ED | — | Modules list |
| `/config/auth` | `AuthOverview.razor` | HL | ✓ | Config landing |
| `/config/auth/sign-in` | `SignInExperience.razor` | AU | ✓ | Auth overview |
| `/config/auth/tenants` | `EntraTenants.razor` | FL/ED | ✓ | Auth overview |
| `/config/auth/entra/{RegistrationId}/users` | `EntraDirectoryUsers.razor` | FL | — | Tenants |
| `/config/auth/entra/{RegistrationId}/groups` | `EntraDirectoryGroups.razor` | FL | — | Tenants |
| `/config/security/sign-in` | `SignInExperienceRedirect.razor` | RD | — | → `/config/auth/sign-in` |
| `/config/governance` | `GovernanceLanding.razor` | HL | ✓ | Config landing |
| `/config/governance/catalog` | `GovernanceCatalog.razor` | FL | ✓ | Governance landing |
| `/config/governance/roles` | `RolesList.razor` | FL | ✓ | Governance landing |
| `/config/governance/roles/{RoleId}` | `RoleDetail.razor` | ED | — | Roles list |
| `/config/governance/users` | `UsersList.razor` | FL | ✓ | Governance landing |
| `/config/governance/users/{UserId}` | `UserDetail.razor` | ED | — | Users list |
| `/config/governance/users/{UserId}/role-derived-access` | `UserEffectiveAccess.razor` | DX | — | User detail |
| `/governance/*` | `GovernanceRedirects.razor` | RD | — | → `/config/governance/*` |
| `/config/contacts` | `ManagedContactsList.razor` | FL | (hub) | **Config landing only** |
| `/config/contacts/{Id?}` | `ManagedContactEditor.razor` | ED/CR | (hub) | Contacts list |
| `/config/notifications` | `NotificationsLanding.razor` | HL | ✓ | Config landing |
| `/config/notifications/definitions` | `NotificationDefinitionsList.razor` | FL | ✓ | Notifications landing |
| `/config/notifications/definitions/new` | `NotificationDefinitionCreate.razor` | CR | — | Definitions list |
| `/config/notifications/definitions/{DefinitionId}` | `NotificationDefinitionDetail.razor` | ED/AU | — | Definitions list |
| `/config/notifications/definitions/{DefinitionId}/edit` | `NotificationDefinitionEdit.razor` | AU | — | Detail |
| `/config/notifications/instances` | `NotificationInstancesList.razor` | FL | ✓ | Notifications landing |
| `/config/notifications/instances/{InstanceId}` | `NotificationInstanceDetail.razor` | ED | — | Instances list |
| `/config/notifications/delivery` | `NotificationDelivery.razor` | DX | ✓ | Notifications landing |
| `/config/notifications/diagnostics` | `NotificationDiagnostics.razor` | DX | ✓ | Notifications landing |

### Runtime admin (deep-linked)

| Route | Component | Archetype | Menu | Parent / hub |
|-------|-----------|-----------|------|----------------|
| `/runtimes/{RuntimeId}/controls/shutdown/requests` | `RuntimeShutdownRequestsList.razor` | FL | — | Control Center activity/targets |
| `/runtimes/{RuntimeId}/controls/shutdown/requests/{RequestId}` | `RuntimeShutdownRequests.razor` | ED | — | Shutdown requests list |

**Route count:** ~58 distinct route patterns across ~52 routable components (excluding child components without `@page`).

## 5. Route / menu truth gaps

### Missing route for menu entry

| Menu entry | Href | Severity |
|------------|------|----------|
| API Explorer | `/tools/explorer` | **High** — menu advertises a page that does not exist in Ui.Client |

### Route not linked from menu or obvious hub

| Route | Expected parent | Notes |
|-------|-----------------|-------|
| `/schedules/new` | `/schedules` | Create not in Automation submenu; may exist on list toolbar |
| `/integrations/new` | `/integrations` | Create not in menu |
| `/connections/new` | `/connections` | Create not in menu |
| `/config/contacts`, `/config/contacts/{Id?}` | `/config` | On Config landing cards only — not in gear submenu |
| `/config/auth/entra/.../users|groups` | `/config/auth/tenants` | Drill-in from tenant rows only |
| `/config/governance/users/{id}/role-derived-access` | User detail | Diagnostic; no menu item |
| All `ED` / `CR` / integration child routes | Respective list or entity | **By design** — details should not be top-level menu |
| `/runtimes/...` | Control Center | **By design** — runtime admin deep links |

### Hidden but intentional (document policy)

| Surface | Policy recommendation |
|---------|----------------------|
| Runtime shutdown request list/detail | Deep-link from Control Center only; optional future “Runtime controls” under Operations |
| Entra directory users/groups | Drill-in from registration detail |
| Integration notifications / schedule diagnostics | Toolbar + summary band from Integration Detail |
| Governance effective-access | Link from User Detail |
| Legacy `/governance/*`, `/config/security/sign-in` | Keep redirects; document in operator docs |

### Stale / legacy / confusing

| Issue | Notes |
|-------|-------|
| Three start entry points in Operations menu | `/execute` + two “Legacy” labels — consolidate menu copy and default |
| Nested nav demo in Operations menu | Product menu should not expose demo routes |
| Dashboard “deferred” items in menu | Pages exist as placeholders; menu labels say deferred — OK if intentional |
| `/modules` under Config domain | Path is top-level `/modules` but rail opens Config — mental model split |
| `/runs` under Automation | Runs are operational forensics; domain label “History” is easy to miss |
| Attention under Operations | Contact groups/routing are configuration; could live under Config or “Attention” subdomain |
| Notifications split | Platform definitions in Config; integration-scoped authoring under `/integrations/{id}/notifications` |
| `Modules` submenu child duplicates parent href | Redundant tree node |

### Ambiguous domain placement

| Page / route | Current domain | Tension |
|--------------|----------------|---------|
| `/runs`, `/runs/{id}` | Automation (menu) | Investigation and remediation are **Operations** workflows |
| `/execute`, blocked work, audit | Operations | Correct |
| `/modules` | Config (resolver) | Lifecycle is admin; overlaps with Integrations/Connections |
| Attention routes | Operations (menu) | Policy/configuration nature |
| Platform notifications | Config | Correct for definitions; integration overlay is correct |

### Duplicate / confusing entry concepts

- **Execute vs legacy starts** — same mental task, three menu rows.
- **Operational teams vs contact-groups** — two URLs, one component; menu only shows “Operational teams”.
- **Config vs Notifications landing** — both are hub landings; Notifications also has four submenu entries (good).

## 6. Current user journeys

### Operational investigation

| Step | Current path | Friction | Recommendation |
|------|--------------|----------|----------------|
| Entry | Dashboard cards → `/control/center`, `/runs`, `/control/audit`, or Operations menu | Dashboard is home, not control center — two “homes” | Clarify **Operations hub** (`/control/center`) vs **Dashboard** in nav copy; cross-link prominently |
| Correlate | Control Center → activity/targets → run detail, audit, blocked work, runtime shutdown | Runtime links hidden from menu | Document runtime deep links; add “related” on CC |
| Deep dive | Run detail: summary band, investigation, controls, audit link | Toolbar dense; park/resume eligibility opaque | Keep detail; add journey doc links in hub |
| Exit | DrillInLink back to integration, schedules, runs list | Epic 94/95 improved module filter on runs | Breadcrumb epic optional |

### Run / retry work

| Step | Current path | Friction | Recommendation |
|------|--------------|----------|----------------|
| Entry | `/runs` (Automation → History), dashboard, integration activity | “History” understates live remediation | Rename submenu to **Runs** or **Run history** |
| Act | Run detail toolbar; blocked work for workflow retry | Blocked work separate from run detail | Keep; link blocked-work row → run detail (exists) |
| Start new | `/execute` vs legacy starts | Menu clutter | Menu shows **Execute** only; legacy routes redirect or hidden |

### Integration setup / maintenance

| Step | Current path | Friction | Recommendation |
|------|--------------|----------|----------------|
| Entry | `/integrations` menu | Single list — good hub | Promote **Create integration** on list + optional menu |
| Configure | Integration detail (large), notifications, schedule diagnostics | Many sections; notifications off-menu | Summary band quick links (Epic 80) — extend to diagnostics |
| Execute | Toolbar → `/execute?integrationId=` | Good | Document query contract on Execute hub |
| Related | Runs (module filter), schedules, connections | Schedule diagnostics not in menu | Expected — entity-local links |

### Schedule management

| Step | Current path | Friction | Recommendation |
|------|--------------|----------|----------------|
| Entry | Automation → Scheduling | No create in menu | Add **Create schedule** to list toolbar (may exist) + submenu or list CTA |
| Detail | Schedule detail enable/disable, global run-cycle | Run-cycle is global, not schedule-scoped — confusing | Keep truth label (Epic 33); link to blocked work / runs |
| Diagnostics | Integration schedule diagnostics only | Not on schedule detail | Optional future schedule-scoped diagnostic link |

### Notification management

| Step | Current path | Friction | Recommendation |
|------|--------------|----------|----------------|
| Platform | Config → Notifications → definitions/instances/delivery/diagnostics | Strong hub + submenu | Keep |
| Integration-scoped | Integration detail → notifications routes | Off menu — correct | Cross-link from notification definition detail when integration-owned |
| Authoring | Definition detail/edit/create | Dense authoring | Already piloted Epic 84 |

### Connection / client management

| Step | Current path | Friction | Recommendation |
|------|--------------|----------|----------------|
| Entry | Connections menu | Hub list + connectors embedded | Good single hub |
| Create | `/connections/new` | Not in menu | List CTA |
| Detail | Connection detail verification | — | Back to list link |

### Governance / user management

| Step | Current path | Friction | Recommendation |
|------|--------------|----------|----------------|
| Entry | Config → Governance | Landing + submenu | Good |
| Roles/users | Lists → detail | Effective access only from user detail | Add drill-in on user detail (may exist) |
| Legacy bookmarks | `/governance/*` | Redirects work | Document |

### Module lifecycle management

| Step | Current path | Friction | Recommendation |
|------|--------------|----------|----------------|
| Entry | Config → Modules (path `/modules`) | Path vs domain mismatch | Future: **Config → Modules** label with consistent copy |
| Detail | Module detail lifecycle + recent activity | Epic 94 panel | Link to filtered runs |

### Audit / runtime inspection

| Step | Current path | Friction | Recommendation |
|------|--------------|----------|----------------|
| Audit | Operations → Audit; query params from entities | Module-scoped audit without integration filter documented | Epic 95 copy |
| Runtime | Control Center → shutdown request links only | No menu | Policy: deep-link only or add Operations submenu |

## 7. Hub / detail / diagnostic model

### Recommended domain hubs (menu-visible landings)

| Hub route | Role | Should stay in menu |
|-----------|------|---------------------|
| `/dashboard` | Instance-wide summary | Yes (logo) |
| `/control/center` | **Operations console** | Yes |
| `/runs` | Automation run index | Yes (rename label) |
| `/schedules` | Schedule index | Yes |
| `/integrations` | Integration fleet | Yes |
| `/connections` | Connection + connector index | Yes |
| `/config` | Config area index | Yes (gear) |
| `/config/auth`, `/config/governance`, `/config/notifications` | Config sub-hubs | Yes (submenu parents) |

### Entity detail pages (not in menu)

Run, schedule, integration, connection, module, role, user, notification definition/instance, contact editor, attention group, runtime shutdown request, etc. — **toolbar + summary band + DrillInLink** pattern (Epics 80–84, 93–97).

### Diagnostic drill-ins (not in menu)

| Route | Owner entity |
|-------|----------------|
| `/config/notifications/diagnostics` | Platform notifications |
| `/config/notifications/delivery` | Delivery health |
| `/integrations/{id}/schedule-diagnostics` | Integration |
| `/config/governance/users/{id}/role-derived-access` | User |
| `/control/audit` | Instance (query-scoped) |

### Tools / start surfaces

| Route | Treatment |
|-------|-----------|
| `/execute` | **Primary** operator start — keep in Operations menu |
| `/operations/start`, `/workflows/start` | Legacy — remove from menu or redirect to `/execute` with query |
| `/tools/explorer` | **Fix or remove** from menu |
| `/demo/nested-nav` | Remove from operator menu |

### Orphaned / weak-parent pages

| Page | Issue |
|------|-------|
| `/tools/explorer` | Orphan menu target |
| `/config/contacts` | Hub card only — easy to miss |
| `/schedules/new`, `/integrations/new`, `/connections/new` | No menu; depend on list CTAs |
| Runtime routes | Orphan by design unless CC linked |

## 8. Recommended future navigation model

**Do not implement in Epic 98.** Proposed target shape grounded in repo audit:

### Top-level rail (stable work areas)

| Domain | Purpose | Menu focus |
|--------|---------|------------|
| **Dashboard** | Cross-domain summary | Home + deferred personalization (hidden until product-ready) |
| **Operations** | Live operations, remediation, audit, execution | Control Center, Blocked Work, Audit, **Execute** (single start) |
| **Automation** | Time-based and historical execution | **Runs**, Schedules (+ create CTAs on lists) |
| **Integrations** | Deployment units | Integrations list (+ create CTA) |
| **Connections** | Connection instances | Connections list (+ create CTA) |
| **Config** | Admin, policy, modules, notifications, auth, governance, contacts | Config hub + structured submenu |

**Evaluate merging or sub-grouping (later epic):**

- **Attention** — submenu under Config (“Operational contacts”) or under Operations if treated as incident routing ops.
- **Runtime controls** — submenu under Operations (read-only list entry) vs remain deep-link-only.
- **Modules** — keep under Config; consider renaming menu to “Modules (installed)” and drop duplicate child item.

### Menu rules for restructure epic

1. **Menu = hubs and consoles**, not every route.
2. **Lists** appear when they are the natural domain entry (`/runs`, `/integrations`).
3. **Create** routes: list header CTA + optional submenu only when high-traffic.
4. **Remove or implement** `/tools/explorer`; **remove** demo from production menu.
5. **Legacy starts**: one menu row + redirects.
6. **Config landing** cards mirrored in submenu (add **Contacts**).
7. **Document** runtime and Entra drill-ins in control-panel-ui.md.

### Breadcrumbs / context links (optional epic)

Not required today; `PageHeader` eyebrow + `DrillInLink` + summary cards cover most flows. Consider lightweight **parent link** component when shell restructure lands (e.g. “← Schedules” on schedule detail).

## 9. Navigation principles

1. **Menu represents stable work areas**, not every `@page`.
2. **Detail / create / edit routes are reached from a parent list or entity**, except documented deep links.
3. **Diagnostic routes** attach to an owning entity or config hub; never orphan without documentation.
4. **Runtime / admin surfaces** use deliberate placement (deep link or explicit Operations submenu).
5. **Hidden routes are documented** in ADR + control-panel-ui (runtime, redirects, integration children).
6. **Legacy redirects stay explicit** (`/governance/*`, `/config/security/sign-in`, legacy starts).
7. **Route names follow operator mental model** (`/runs` not buried only as “History”).
8. **Cross-links use `DrillInLink` / `Epic33UiDisplayHelpers`** with preserved query filters (Epic 95).
9. **Section resolver and menu must agree** — today `/modules` forces Config; document when adding new path prefixes.
10. **Stylesheets and shell assets** must load from **Ui.Server `_Layout.cshtml`** as well as WASM `index.html` (Epic 97 corrective lesson).

## 10. Recommended follow-up epics (priority order)

| Priority | Epic (proposed) | Scope |
|----------|-----------------|-------|
| 1 | **Route / menu truth cleanup** | **Done — [Epic 99](Epic99-Route-Menu-Truth-Cleanup.md)** |
| 2 | **Shell / domain navigation restructure** | Rename Automation “History”; optional Attention/Runtime grouping; align menu labels with hubs |
| 3 | **Operations & Automation landing improvements** | Control Center as Operations home cross-links; optional `/automation` hub card page |
| 4 | **Breadcrumb / parent context component** | Reusable “Back to {parent}” without full shell rewrite |
| 5 | **Remaining archetype migration** | Non-pilot pages per Epic 78/89 backlog |
| 6 | **Operation history / feedback shell** | Epic 91 registry — separate from nav |

## 11. Validation

- **Docs-only epic** — no production code changes.
- **No build required** for acceptance.
- Route inventory verified against `@page` grep in `Apperture.Management.Ui.Client` and `ControlPanelMenuDefinitions.cs` (May 2026).

## References

- [Epic 78 — Modernization groundwork](Epic78-Management-UI-Modernization-Groundwork.md)
- [Epic 85 — Pilot consolidation](Epic85-Management-UI-Pilot-Consolidation.md)
- [Epic 97 — Visual polish](Epic97-Management-UI-Visual-Density-and-Polish-Pass.md)
- [Epic 97 toolbar corrective](Epic97-Toolbar-Affordance-Corrective-Patch.md) — Ui.Server layout stylesheet lesson
- [control-panel-ui.md](../01-architecture/control-panel-ui.md)
- [management-ui-design-system.md](../01-architecture/management-ui-design-system.md)
