# Before: Management UI navigation

**Transitional.** Snapshot of navigation **immediately before** the planned overhaul. Not the architecture contract. Not the target IA. See [README.md](README.md) in this folder.

Runtime source of truth until the overhaul ships: `ControlPanelMenuDefinitions.GetDefaultMenus()` (`src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ControlPanelMenuDefinitions.cs`). `GET /api/menus` returns that tree and may append enabled-module `NavigationContribution` items.

Shell contract (what *was* documented as current, and may lag this snapshot): [control-panel-ui.md](../../control-panel-ui.md). Older audit: [Epic 98](../../../02-decisions/Epic98-Management-UI-Navigation-and-Information-Flow-Audit.md).

Legend (as the product exists in this snapshot):

| Mark | Meaning |
|------|---------|
| **menu** | Listed in the Control Panel for that domain |
| **hub** | Linked from a landing-page summary card |
| **child** | Reachable from a list/detail page; not in the Control Panel |
| **alias** | Redirects to a canonical route |
| **public** | Auth layout; no nav rail / top bar |
| **off-rail** | Authenticated shell, but no matching rail domain |

---

## 1. Chrome (three independent surfaces)

```
┌─ Nav rail (left) ─────────────────────────────────────────────┐
│  Logo  → Dashboard                                            │
│  Middle domain icons (Operations, Automation, Integrations,   │
│                       Connections)                            │
│  Gear  → Config                                               │
│  [Control Panel slides out with that domain's tree]           │
├─ Top bar ─────────────────────────────────────────────────────┤
│  Hamburger │ breadcrumbs / title │ Theme │ Inbox │ Account    │
└─ Page body ───────────────────────────────────────────────────┘
     Account area adds in-page tabs (AccountSectionNav).
     Config pages use ConfigLayout (wrapper only; nav is the Control Panel).
```

| Surface | Owner | What it lists |
|---------|--------|----------------|
| Nav rail | `NavRail.razor` | Domain icons. Logo = Dashboard. Gear = Config. Middle loop excludes those two. |
| Control Panel | `ControlPanel.razor` | Tree for the active domain. Nested groups expand in place. |
| Top bar utilities | `TopBar.razor` | Theme (not a page), personal notification inbox, account menu. |
| Account tabs | `AccountSectionNav.razor` | Four personal sections. Not on the rail. |
| Breadcrumbs | `ManagementBreadcrumbResolver` | Parent trail in the top bar; does not add menu items. |

Layouts:

- `MainLayout` — rail + Control Panel + top bar. Default for authenticated pages.
- `ConfigLayout` — wraps `MainLayout`; no extra sidebar. Config/Governance/Demo/Developer pages use it.
- `AuthLayout` — no chrome. Sign-in and credential-lifecycle pages.

Account pages (`/account/*` signed-in sections, `/me/notifications`) sit on `MainLayout` but **do not resolve to a rail domain** (`RouteToSectionResolver` only maps `/config`, `/modules`, `/attention`, `/dashboard`, and menu hrefs).

---

## 2. Rail + Control Panel (operator menu)

Order is dictionary order in `GetDefaultMenus()`, which is also the visual middle-rail order after Dashboard/Config are pulled out.

```
Nav rail
├── [logo] Dashboard
├── Operations
├── Automation
├── Integrations
├── Connections
└── [gear] Config
```

### 2.1 Dashboard (logo)

Menu:

- **Dashboard home** → `/dashboard` **menu**

Off-menu (deferred; linked from Developer & internal, not the Dashboard menu):

- `/dashboard/manage` **child** — Manage dashboards
- `/dashboard/add` **child** — Add dashboard page
- `/dashboard/layout` **child** — Edit layout
- `/dashboard/delete` **child** — Delete dashboard page

`/` **alias** → `/dashboard` (`OperatorLanding`). Root never mounts `MainLayout`.

### 2.2 Operations

Menu (flat):

- **Control Center** → `/control/center` **menu**
- **Blocked Work** → `/control/blocked-work` **menu**
- **Execute** → `/execute` **menu**
- **Audit** → `/control/audit` **menu**

Off-menu under Operations:

- `/operations/start` **child** — Start operation (Execute links here conceptually; still routable)
- `/runtimes/{RuntimeId}/controls/shutdown/requests` **child** — Shutdown request list (from Control Center)
- `/runtimes/{RuntimeId}/controls/shutdown/requests/{RequestId}` **child** — Request detail

Control Center hub cards also cross-link **Integrations**, **Runs**, **Schedules**, **Blocked work**, **Audit** (other domains).

### 2.3 Automation

Menu (flat):

- **Runs** → `/runs` **menu**
- **Schedules** → `/schedules` **menu**

Off-menu:

- `/runs/{RunId}` **child** — Run detail
- `/schedules/new` **child** — Create schedule
- `/schedules/{ScheduleId}` **child** — Schedule detail
- `/workflows/start` **child** — Start workflow (not in menu; Execute is the operator start surface)

### 2.4 Integrations

Menu:

- **Integrations** → `/integrations` **menu**

Off-menu (entity subtree; toolbar on detail/settings/notifications, not Control Panel):

- `/integrations/new` **child** — Create integration
- `/integrations/{IntegrationId}` **child** — Integration detail
- `/integrations/{IntegrationId}/settings` **child** — Settings
- `/integrations/{IntegrationId}/notifications` **child** — Integration notifications
- `/integrations/{IntegrationId}/notifications/definitions/new` **child** — New definition (scoped)
- `/integrations/{IntegrationId}/schedule-diagnostics` **child** — Schedule diagnostics

### 2.5 Connections

Menu:

- **Connections** → `/connections` **menu**

In-page tabs on `/connections` (not Control Panel children): **Connections** | **Connectors**.

Off-menu:

- `/connections/new` **child** — Add connection
- `/connections/{ConnectionId}` **child** — Connection detail

### 2.6 Config (gear)

Control Panel tree (this is the full **in-menu** Config hierarchy):

```
Config
├── Overview                         /config
├── Authentication                   /config/auth
│   ├── Sign-in experience           /config/auth/sign-in
│   └── Entra tenants                /config/auth/tenants
├── Governance                       /config/governance
│   ├── Permission catalog           /config/governance/catalog
│   ├── Roles                        /config/governance/roles
│   └── Users                        /config/governance/users
├── Notifications                    /config/notifications
│   ├── Definitions                  /config/notifications/definitions
│   ├── Instances                    /config/notifications/instances
│   └── Delivery                     /config/notifications/delivery
├── Attention                        /attention/operational-teams
│   ├── Operational teams            /attention/operational-teams
│   ├── Routing defaults             /attention/routing-defaults
│   └── Managed contacts             /config/contacts
├── Platform maintenance             /config/platform-settings
│   ├── Platform settings            /config/platform-settings
│   └── Modules                      /modules
└── Developer & internal             /config/developer
```

Clicking the gear also navigates to `/config`.

Config landing (`/config`) hub cards match the groups above (Authentication, Governance, Notifications, Attention, Platform settings, Modules, Developer tasks).

---

## 3. Config — full page hierarchy (including off-menu)

### 3.1 Overview

- `/config` **menu** — Configuration landing

### 3.2 Authentication

- `/config/auth` **menu** — Overview
- `/config/auth/sign-in` **menu** — Sign-in experience
- `/config/auth/tenants` **menu** — Entra tenants
  - `/config/auth/entra/{RegistrationId}/users` **child** — Directory users (from tenant row)
  - `/config/auth/entra/{RegistrationId}/groups` **child** — Directory groups (from tenant row)
- `/config/security/sign-in` **alias** → `/config/auth/sign-in`

### 3.3 Governance

- `/config/governance` **menu** — Governance landing (hub: catalog, roles, users)
- `/config/governance/catalog` **menu** — Permission catalog
- `/config/governance/roles` **menu** — Roles list
  - `/config/governance/roles/{RoleId}` **child** — Role detail
- `/config/governance/users` **menu** — Users list
  - `/config/governance/users/new` **child** — New user
  - `/config/governance/users/{UserId}` **child** — User (role assignments)
    - `/config/governance/users/{UserId}/role-derived-access` **child** — Role-derived access

Legacy **alias** (all redirect into `/config/governance/…`):

- `/governance/catalog`
- `/governance/roles`, `/governance/roles/{RoleId}`
- `/governance/users`, `/governance/users/{UserId}`
- `/governance/users/{UserId}/effective-access` → `…/role-derived-access`

### 3.4 Notifications (platform)

- `/config/notifications` **menu** — Landing
- `/config/notifications/definitions` **menu**
  - `/config/notifications/definitions/new` **child**
  - `/config/notifications/definitions/{DefinitionId}` **child**
  - `/config/notifications/definitions/{DefinitionId}/edit` **child**
- `/config/notifications/instances` **menu**
  - `/config/notifications/instances/{InstanceId}` **child**
- `/config/notifications/delivery` **menu**
- `/config/notifications/diagnostics` **hub** (Notifications landing + Developer tasks; **not** in Control Panel children)

### 3.5 Attention

URL prefix is `/attention/…` except managed contacts (`/config/contacts`). Rail still highlights **Config**.

- `/attention/operational-teams` **menu** (also `/attention/contact-groups` **alias** on the same page)
  - `/attention/operational-teams/{Id}` **child** (also `/attention/contact-groups/{Id}`)
- `/attention/routing-defaults` **menu**
- `/config/contacts` **menu** — Managed contacts list
  - `/config/contacts/{Id?}` **child** — Editor (`new` or existing id)

### 3.6 Platform maintenance

- `/config/platform-settings` **menu**
- `/modules` **menu** — Modules list (not under `/config/modules`)
  - `/modules/{ModuleId}` **child** — Module detail

### 3.7 Developer & internal

- `/config/developer` **menu** — Hub of deferred/internal tools

Linked from that hub (none of these are Control Panel items except where they already belong elsewhere):

| Label | Route | Notes |
|-------|-------|--------|
| Manage dashboards | `/dashboard/manage` | Deferred Dashboard |
| Add dashboard page | `/dashboard/add` | Deferred Dashboard |
| Delete dashboard page | `/dashboard/delete` | Deferred Dashboard |
| Edit dashboard layout | `/dashboard/layout` | Deferred Dashboard |
| Worker runtime | `/control/center` | Points at Control Center |
| Worker lifecycle (quarantined) | `/control/center` | Same |
| Notification diagnostics | `/config/notifications/diagnostics` | Also a Notifications hub card |
| Nested navigation demo | `/demo/nested-nav` (+ `/{*Slugs}`) | Internal depth demo |
| Demo Customers | `/demo/customers` | Sample projection |
| Account preferences | `/account/preferences` | Personal settings (odd placement) |

Demo Customers subtree (breadcrumbs hang off Config → Developer & internal):

- `/demo/customers` **hub**
  - `/demo/customers/{BusinessKey}` **child**
    - `/demo/customers/{BusinessKey}/activity` **child**

---

## 4. Personal account (top-bar Account menu + in-page tabs)

Not a rail domain. Opened from the avatar menu (`UserAccountMenu`). Signed-in pages use `MainLayout` + `AccountSectionNav` tabs.

Account menu items (same four destinations as the tabs):

```
Account menu
├── Profile
├── Notifications          (delivery preferences — not the inbox)
├── Appearance
├── Account
└── Sign out
```

In-page tab order (`AccountUiRoutes.Sections`):

| Tab label | Route | Page purpose |
|-----------|-------|----------------|
| Profile | `/account/profile` | Name, photo, identity |
| Notifications | `/account/notifications` | Which notifications reach you, and how |
| Appearance & regional | `/account/preferences` | Theme, timezone, clock |
| Account | `/account` | Sign-in methods, contact email, password |

Aliases:

- `/account/security` **alias** — same page as `/account`
- `/preferences` **alias** → `/account/preferences`

Related personal surface (not a tab; top-bar bell):

- `/me/notifications` **off-rail** — Personal notification inbox (“View all notifications” in the bell menu)

Account Notifications tab links to the inbox. Theme menu in the top bar duplicates appearance (Light / Dark / System) without leaving the current page.

---

## 5. Auth / public (no shell)

`AuthLayout`. Listed in `LoginRouteHelper.PublicPaths`.

| Route | Page |
|-------|------|
| `/login` **public** | Primary sign-in chooser |
| `/login/local` **public** | Local backup sign-in |
| `/account/forgot-password` **public** | Request reset |
| `/account/reset-password` **public** | Consume reset |
| `/account/setup-password` **public** | Set initial password |
| `/account/verify-email` **public** | Verify contact email |
| `/access-request` **public** (path reserved) | No Blazor `@page` in the client today |

Host-level `signin` / `signout` are not Blazor pages (server auth endpoints).

---

## 6. URL prefix vs rail domain

Useful when regrouping: several Config children do not live under `/config`.

| Path prefix | Rail domain | Control Panel group |
|-------------|-------------|---------------------|
| `/dashboard` | Dashboard | Dashboard |
| `/control`, `/execute`, `/operations`, `/runtimes` | Operations | Operations |
| `/runs`, `/schedules`, `/workflows` | Automation | Automation (`/workflows/start` off-menu) |
| `/integrations` | Integrations | Integrations |
| `/connections` | Connections | Connections |
| `/config` | Config | Config |
| `/modules` | Config | Platform maintenance |
| `/attention` | Config | Attention |
| `/demo` | Config (customers via breadcrumbs; nested-nav has no crumbs) | Developer & internal |
| `/account`, `/preferences`, `/me` | *(none)* | Top-bar account / inbox |
| `/login` | *(none)* | Auth layout |

---

## 7. Module-contributed items

Enabled modules that implement `IModuleNavigationProvider` inject extra `DomainMenuItem`s into the matching domain (or create a new domain key). Those items are **not** in `ControlPanelMenuDefinitions`; they appear at runtime after `GET /api/menus`.

---

## 8. Pressure points this overhaul is expected to touch

Facts from the before-tree. Not a decided plan.

1. **Two homes for “settings”:** instance Config (gear) vs personal Account (avatar). Developer hub still links to `/account/preferences`.
2. **Notifications split three ways:** personal inbox (`/me/notifications`), personal delivery prefs (`/account/notifications`), platform admin (`/config/notifications` + per-integration `/integrations/{id}/notifications`).
3. **Attention URLs vs Config URLs:** teams/routing under `/attention`, contacts under `/config/contacts`. Same Control Panel group.
4. **Modules** sit in Config but at `/modules`, not `/config/modules`.
5. **Diagnostics** is a Notifications landing card and a Developer card, but not a Control Panel leaf.
6. **Execute vs start routes:** `/execute` is in the Operations menu; `/operations/start` and `/workflows/start` remain as pages.
7. **Dashboard customization** is buried under Developer, not Dashboard.
8. **Account has no rail highlight.** Operators on `/account/*` or `/me/notifications` see an inactive rail.
9. **Architecture doc lag:** [control-panel-ui.md](../../control-panel-ui.md) still lists Attention under Operations, and its Config menu text omits Attention / Platform maintenance / Developer as they exist in this snapshot. Update that file when the overhaul ships — do not treat this snapshot as the replacement contract.
10. **Create/detail routes are intentionally off-menu** (lists own the CTAs). Keep that rule unless IA explicitly promotes a child.

---

## 9. Scratchpad (target IA)

_Fill in as the overhaul is decided. This is the change record, not a shipped design._

**Problems to solve**

-

**Proposed rail domains**

-

**What moves (from → to)**

-

**What stays off-menu**

-

**Open questions**

-
