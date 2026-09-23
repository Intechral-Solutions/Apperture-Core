# Control Panel UI

The Control Panel UI provides the navigation shell for the Apperture Management interface. It consists of an icon rail and a sliding Control Panel.

## Layout

- **Icon rail** — Fixed left rail (w-17) with icon buttons for each domain. Each button is either:
  - A direct navigation link (when the domain has a single item)
  - A domain opener that opens the Control Panel

- **Control Panel** — Sliding side panel that shows submenu items for the selected domain. Slides in from the rail with translate-x animation (~300ms).

- **Content area** — Shifts horizontally based on panel state: `ml-17` when closed, `ml-70` when open.

- **Top bar** — Hamburger, **orientation area** (breadcrumb trail; optional future title row), theme and account menus (right). Parent crumbs are navigable plain `<a href>` links; current crumb is text. See [Epic 101](../02-decisions/Epic101-Top-Bar-Breadcrumbs-and-Parent-Context-Links.md) and [UIX-2 stabilization](../audits/uix2-breadcrumb-shell-stabilization.md).

## View selector

- **Placement:** Top bar, leading edge of the orientation area — before the breadcrumb/title area.
- **Label:** "View" (never "Workspace", "Application", or "Context").
- **Persistence:** Three layers, in priority order — URL query string on scoped pages (`?context=`), tab state (in-memory, per browser tab), and per-user `localStorage`. There is no server-side preference.
- **Visibility:** Hidden until the first active Application exists anywhere in the instance.

Full behavioral spec: [CTX-3 design gate](transitions/resource-contexts/ctx3-design-gate.md) §3-§4.

## Terminology

- **Domain** — A grouping of related menu items that maps to a rail icon (Control Center, Integrations, Business Data, Config). Replaces "Module" from IntegrationCenter.
- **Control Panel** — The sliding side panel showing domain submenu items. Replaces "Module menu" from IntegrationCenter.

## Current top-level domains

Canonical rail (see [ADR-0040](../02-decisions/ADR-0040-operator-information-architecture.md)):

```
[Logo] Control Center
Integrations
Business Data
----------------
[Gear] Config
```

Operations, Automation, Connections, and Dashboard are **not** rail domains.

**Control Center** (logo): overview (`/control/center`), **Runs**, **Execute**, **Audit**. `/` and `/dashboard` redirect here. Blocked work is the Runs facet `/runs?status=blocked` (legacy `/control/blocked-work` redirects).

**Integrations:** fleet list `/integrations`. Connections and schedules stay their own models; they are not Integrations children.

**Business Data:** collections landing `/business-data` and **Customers** `/business-data/customers` (demo/sandbox projection with a demo badge; `/demo/customers` redirects).

**Config** (gear): **Overview**, **Inventory**, **Applications**, **Governance**, **Attention** (operational teams, routing defaults, managed contacts, Notifications including diagnostics), **System** (platform settings, **Components** → Catalog and Modules, authentication, developer & internal). Create/detail routes stay off-menu. **Applications** manages ResourceContexts — Application workspaces plus the read-only Shared context; see the [CTX-3 design gate](transitions/resource-contexts/ctx3-design-gate.md) §6.

**Components** (`Config → System → Components`): **Catalog** `/components` and **Modules** `/modules`. The catalog lists connectors and platform/module-supplied notification definitions with Kind, Origin, Module and search filters; a row opens `/components/{kind}/{nativeId}`, a read-only wrapper that links to the authoritative surface for management. The structural *Components* node has no capability of its own, so an operator who can manage modules but read no Component family still reaches Modules; *Catalog* is gated by the derived `ViewComponents` token. It is a global, system/catalog surface: it has no View/Context filter. Module detail lists "Components provided by this module" from the same projection. The `/connections` Connectors tab is retained for now and links its rows to Component detail. See [ADR-0043](../02-decisions/ADR-0043-component-catalog-read-model.md).

UI aggregation is not domain ownership: Connections, Schedules, Runs, and Business Data keep their own models. Inventory and Control Center discover them; they do not become Config or Integration entities.

**Orientation:** Control Center is the sole operator home. Deferred Dashboard Panel routes (`/dashboard/manage`, `/add`, `/layout`, `/delete`) remain under Developer & Internal.

## State Flow

- **Domain icon click** → `HandleControlPanelOpen(domainName)` → set currentDomain, open + lock panel
- **Direct link click** → `HandleControlPanelClose` → close panel, navigate
- **Hamburger click** → If locked: unlock and close. If closed: open and lock.

## API Contract

- **Endpoint:** `GET /api/menus`
- **Response:** `Dictionary<string, DomainMenu>` where `DomainMenu` = `{ Title?, Items: DomainMenuItem[] }`, `DomainMenuItem` = `{ Text, Href?, Icon?, Children?, RequiredCapability? }`
- **Capability filter:** items with `RequiredCapability` are omitted when the operator lacks that token. Empty domains are dropped except Control Center and Config (custom-zone owners). Menu hiding is never a substitute for page/API authorization.
- **Module contributions:** enabled `IModuleNavigationProvider` items attach only to known rail domains (Control Center, Integrations, Business Data, Config). Arbitrary new rail domains are ignored.
- **Configuration:** `ControlPanelMenuOptions.JsonSource` (default: `/api/menus`)

## Operator console (current baseline)

- **Default landing:** `/` and post-login redirect to `/control/center`. `/dashboard` replaces to Control Center after menus fetch.
- **Control Center:** live triage (`GET /api/control/center`) — health, blocked runs, recent outcomes, scheduler cycle. The authentication-posture warning that is not already on Control Center is summarized on Config overview via `ConfigAuthPostureUiHelpers` (post-review stabilization) — a small, direct read of `GET api/config/auth-overview`, not the retired Dashboard card framework (`GET api/dashboard/default`, `IDashboardCardProvider`), which has no remaining UI caller and is kept undeleted only as a documented API contract pending a dedicated removal pass.
- **Runs:** `/runs` list + `/runs/{id}` detail. `status=blocked` is a **derived facet** (`failed` + `[Blocked] ` failure summary), not a stored lifecycle status. Per-run retry stays on run detail.
- **Config Inventory:** `/config/inventory` searches configuration records (modules, integrations, connections, schedules, notification definitions) and recent activity (runs, audit). Opening a row goes to the canonical page.
- **Execute:** `/execute` starts operations and workflows. Legacy `/operations/start` and `/workflows/start` remain stubs to Execute.
- **Control actions:** cancel/retry/park/resume via existing Management API endpoints; operator observes outcomes via refresh/poll.
- **Personal inbox / account:** `/me/notifications` and `/account/*` stay top-bar, not rail.

## UI modernization and layering

Page archetypes, summary/detail/advanced layering, drill-in rules, and pilot recommendations are documented in [Epic 78 — Management UI modernization groundwork](../02-decisions/Epic78-Management-UI-Modernization-Groundwork.md). Reusable primitives and visual conventions are in [Management UI design system](management-ui-design-system.md) (Epic 79). This file remains the shell and navigation contract.

**Navigation audit (Epic 98):** Full route/menu inventory, information-flow journeys, hub/detail model, and recommended future domain structure — [Epic 98 — Navigation and information-flow audit](../02-decisions/Epic98-Management-UI-Navigation-and-Information-Flow-Audit.md).

**Top-bar breadcrumbs (Epic 101 / UIX-2):** Route-aware parent context in the shell top bar — [Epic 101](../02-decisions/Epic101-Top-Bar-Breadcrumbs-and-Parent-Context-Links.md); renderer stabilization — [UIX-2](../audits/uix2-breadcrumb-shell-stabilization.md).

**Stylesheets:** The dev host shell (`Apperture.Management.Ui.Server/Pages/_Layout.cshtml`) must load `css/management-ui-primitives.css` after `css/app.css` (same as WASM `index.html`). See Epic 97 toolbar corrective ADR. `css/app.css` is generated from `Apperture.Management.Ui.Client/styles/` by `build-ui` — edit the sources, not the output.
