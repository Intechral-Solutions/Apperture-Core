# Epic 101 — Top-Bar Breadcrumbs and Parent Context Links

## Status

Accepted — implemented May 2026.

## Purpose

After [Epic 98](Epic98-Management-UI-Navigation-and-Information-Flow-Audit.md) (navigation audit), [Epic 99](Epic99-Route-Menu-Truth-Cleanup.md) (menu truth), and [Epic 100](Epic100-Management-Shell-Domain-Navigation-Restructure.md) (shell domain labels), operators still lacked **page-local orientation** in the shell: where am I, what entity context am I in, and which parent list/hub can I return to?

This epic adds a **compact breadcrumb trail in the top bar** — complementing the left rail and `PageHeader` titles, without route moves, shell rewrites, or entity-name API lookups.

## Product direction

- Breadcrumbs live in the **top bar** (previously section name + raw path).
- Answer: “Where am I in the management UI, and what parent context can I return to?”
- **Not** a replacement for page titles (`PageHeader`) or the control panel menu.
- **Generic labels** for dynamic IDs (`Integration`, `Run`, `Schedule`, …); real names stay in page headers.

## Implementation

### Model

`BreadcrumbItem` (`Apperture.Management.Ui.Shared/Models/BreadcrumbItem.cs`):

- `Label`, optional `Href`, `IsCurrent` (current crumb is text only).

### Resolver

`ManagementBreadcrumbResolver` — static, explicit route rules (most specific first):

- Input: request path (`Uri.AbsolutePath`).
- Output: ordered crumbs or empty (login, demo, unknown).
- Parent links use **stable menu/hub routes** (`/control/center`, `/config`, `/integrations`, …).
- Legacy `/governance/*` paths resolve with **canonical `/config/governance/*` hrefs**.
- No capability filtering in v1 (same stable parent links as menu; no new auth behavior).

### Presentation

- `ManagementBreadcrumbs.razor` (Molecules) — `<nav aria-label="Breadcrumb">`, parent links, current text, `/` separators.
- `TopBar.razor` — renders breadcrumbs in center slot (`hidden md:block`); theme/account controls unchanged.
- Styles: `ui-breadcrumbs*` in `management-ui-primitives.css`.

### Baseline trails (examples)

| Path | Trail |
|------|--------|
| `/dashboard` | Dashboard |
| `/control/center` | Operations / Control Center |
| `/runs/{id}` | Automation / Runs / Run |
| `/integrations/{id}/notifications` | Integrations / Integration / Notifications |
| `/config/notifications/definitions/{id}/edit` | Config / Notifications / Definitions / Definition / Edit |
| `/modules/{id}` | Config / Modules / Module |
| `/config/contacts/{id}` | Config / Managed contacts / Contact |

Full rule set is in `ManagementBreadcrumbResolver.cs` and covered by unit tests.

## Out of scope (unchanged)

- Route renames or menu merges
- Entity display names from APIs
- Async breadcrumb loading
- Permission model changes
- Page redesigns
- Replacing `PageHeader` eyebrows

## Tests

`Epic101ManagementBreadcrumbResolverTests.cs` — static/dynamic paths across Operations, Automation, Integrations, Connections, Config, Modules, attention, governance legacy; unknown/excluded paths; parent href stability.

## Related documentation

- [control-panel-ui.md](../01-architecture/control-panel-ui.md) — shell top bar
- [management-ui-design-system.md](../01-architecture/management-ui-design-system.md) — breadcrumb component
- [Epic 98](Epic98-Management-UI-Navigation-and-Information-Flow-Audit.md) — navigation audit (recommended breadcrumbs)

## Corrective pass (QA — placement and navigation)

### Assessment

| Question | Finding |
|----------|---------|
| **Difficulty** | **Low–moderate**, contained to `TopBar.razor`, `ManagementBreadcrumbs.razor`, and `management-ui-primitives.css`. No route/menu/shell rail changes. |
| **Why below the top bar?** | Breadcrumbs were already mounted inside `<header id="topbar">`, but the bar used a fixed `h-16` single-row flex layout with `hidden md:block` on the center slot. Long trails wrapped to a second line (looked like a row under the bar), and viewports below `md` hid shell breadcrumbs entirely so operators only saw `PageHeader` **Eyebrow** text in page content — visually “below” the bar and not part of the shell. |
| **Why not navigable?** | Resolver already assigned parent `Href` values; the component rendered `<a href>`. Links used muted, non-underlined styles (looked like labels). SPA navigation was not explicit (plain anchors can be inconsistent in some hosts); corrected with `NavigationManager.NavigateTo` on click plus clear link affordance. |

### Changes (corrective)

- **Top bar layout:** `ui-topbar` grid-like flex — menu | **orientation** (breadcrumbs + reserved title slot) | utilities. Removed `hidden md:block`; breadcrumbs always in the orientation region with horizontal truncation (no wrap).
- **Navigation:** Parent crumbs use underline + primary color; click calls `NavigateTo` (current crumb remains plain text).
- **Tests:** Non-terminal crumbs must have `Href`; integration/definition dynamic parent hrefs asserted.

### Deferred

- Shell **page title/subtitle** row (`ui-topbar__title-slot`) — markup reserved, not wired to pages.
- Capability-aware crumb filtering.
- Entity display names from existing page state (no API).
- Narrow-viewport overflow menu if truncation is insufficient.
