# Control Panel UI

The Control Panel UI provides the navigation shell for the Apperture Management interface. It consists of an icon rail and a sliding Control Panel.

## Layout

- **Icon rail** — Fixed left rail (w-17) with icon buttons for each domain. Each button is either:
  - A direct navigation link (when the domain has a single item)
  - A domain opener that opens the Control Panel

- **Control Panel** — Sliding side panel that shows submenu items for the selected domain. Slides in from the rail with translate-x animation (~300ms).

- **Content area** — Shifts horizontally based on panel state: `ml-17` when closed, `ml-70` when open.

## Terminology

- **Domain** — A grouping of related menu items (e.g. Operations, Automation, Integrations). Replaces "Module" from IntegrationCenter.
- **Control Panel** — The sliding side panel showing domain submenu items. Replaces "Module menu" from IntegrationCenter.

## Current top-level domains

- **Dashboard** (top logo owner)
- **Operations**
- **Automation**
- **Integrations**
- **Connections**
- **Config** (bottom gear owner)

`Config` is the primary home for module lifecycle management (`/modules`), while `Integrations` and `Connections` remain operational domains.
The `Automation` domain contains run history (`/runs`) surfaced in the menu as **Automation History**.

## State Flow

- **Domain icon click** → `HandleControlPanelOpen(domainName)` → set currentDomain, open + lock panel
- **Direct link click** → `HandleControlPanelClose` → close panel, navigate
- **Hamburger click** → If locked: unlock and close. If closed: open and lock.

## API Contract

- **Endpoint:** `GET /api/menus`
- **Response:** `Dictionary<string, DomainMenu>` where `DomainMenu` = `{ Title?, Items: DomainMenuItem[] }`, `DomainMenuItem` = `{ Text, Href?, Icon?, Children? }`
- **Configuration:** `ControlPanelMenuOptions.JsonSource` (default: `/api/menus`)

## Operator console (current baseline)

- **Default landing view:** `/` redirects to `/dashboard` (`OperatorLanding` → platform summary cards). The **Control Center** (`/control/center`) is the primary operational console under the Operations domain, not the root landing route.
- **Dashboard home:** `/dashboard` renders the shared default operator summary (`GET /api/dashboard/default`) with permission-aware card omission and drill-ins (see Epics 45–47).
- **Control center:** `/control/center` provides a cohesive operator facade (targets + recent activity + attention) and links into drill-down pages.
- **Runs console:** `/runs` list + `/runs/{id}` detail. Detail renders backend truth only: lifecycle events, step history, and external-control request trail.
- **Control actions:** cancel/retry/park/resume are issued via the existing Management API endpoints and are shown as cooperative/queued until the worker applies them (operator observes outcomes via refresh/poll).
- **Operation start:** `/operations/start` provides operation-centric discovery and execution. Inputs are metadata-driven from operation definition schema, not module custom UI.
- **Workflow start:** `/workflows/start` provides a minimal workflow start surface using the existing modules/workflows listing endpoints and `POST /workflows/{id}/start`.

## UI modernization and layering

Page archetypes, summary/detail/advanced layering, drill-in rules, and pilot recommendations for future Management UI work are documented in [Epic 78 — Management UI modernization groundwork](../02-decisions/Epic78-Management-UI-Modernization-Groundwork.md). That epic is documentation-only; this file remains the shell and navigation contract.
