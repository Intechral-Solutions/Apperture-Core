# Epic 99 — Route / Menu Truth Cleanup

## Status

Accepted — implemented May 2026.

## Purpose

[Epic 98](Epic98-Management-UI-Navigation-and-Information-Flow-Audit.md) mapped routes, menus, and information flow. This epic implements **concrete menu truth fixes** from that audit: remove broken or demo entries, expose one canonical start surface in the menu, improve Config discoverability for contacts and modules, and confirm create-route CTAs on list hubs — **without** shell restructure, route moves, or permission changes.

## Feature-focused audit

| Item | Checked | Result |
|------|---------|--------|
| **`/tools/explorer`** | Grep repo + `@page` in Ui.Client | **No Blazor page**; only menu href and future-phase ADR mention. **Removed from menu.** No placeholder page added. |
| **Nested nav demo** | `NestedNavDemo.razor` + menu tree | Dev/demo page **preserved** at `/demo/nested-nav{/*}`; **removed from Operations menu** (and nested children). |
| **Start / execute routes** | `ExecutionStart`, `OperationStart`, `WorkflowStart` | Routes **preserved**. Menu shows **`/execute` only**; legacy `/operations/start` and `/workflows/start` removed from menu. |
| **Config → Contacts** | `ManagedContactsList.razor`, Config landing | Page at `/config/contacts` (title “Managed contacts”). **Added** gear submenu entry. Landing card unchanged. |
| **Modules** | Config submenu | Parent + child both pointed to `/modules`. **Flattened** to single **Modules** item. |
| **Create CTAs** | `SchedulesList`, `IntegrationsList`, `ConnectionsPage` | All three have capability-gated toolbar links to `/schedules/new`, `/integrations/new`, `/connections/new`. **No change required.** |

### Intentionally left alone

- Dashboard deferred menu rows (labeled deferred; pages exist as placeholders).
- Automation label **History** for `/runs` (rename deferred to shell restructure epic).
- Attention under Operations (domain placement deferred).
- Runtime shutdown routes (deep-link only; no menu).
- Integration/notification child routes off-menu (by design).
- Legacy `/governance/*` and `/config/security/sign-in` redirects.
- Module-contributed menu merge in `MenusController` (unchanged).
- Integrations/Connections create button styling (non-`ui-toolbar-btn` classes) — functional hrefs present.

## Changes implemented

### `ControlPanelMenuDefinitions.cs` (Operations)

**Removed:**

- API Explorer → `/tools/explorer`
- Nested nav demo subtree → `/demo/nested-nav` …
- Start Operation (Legacy) → `/operations/start`
- Start Workflow (Legacy) → `/workflows/start`

**Kept:**

- Control Center, Blocked Work, Audit Timeline, **Execute** (`/execute`), Operational teams, Attention routing defaults

### `ControlPanelMenuDefinitions.cs` (Config)

**Added:**

- **Managed contacts** → `/config/contacts`

**Simplified:**

- **Modules** → single item `/modules` (removed redundant “Installed Modules” child with same href)

### Tests

- `Epic99RouteMenuTruthCleanupTests.cs` — menu truth assertions
- `ShellSectionOwnerTests` — demo paths no longer resolve via menu (empty domain when not in tree)

### List / hub pages

No markup changes — create CTAs already present with correct permissions.

## Routes preserved but hidden from menu

| Route | Still routable | Notes |
|-------|----------------|-------|
| `/operations/start` | Yes | Deep links, bookmarks, API drill-ins |
| `/workflows/start` | Yes | Same |
| `/tools/explorer` | No page | Menu removed; no route added |
| `/demo/nested-nav{/*}` | Yes | Dev-only; direct URL still works |
| `/schedules/new`, `/integrations/new`, `/connections/new` | Yes | List toolbar CTAs |

## Behavior preservation

- No route handlers, `@page` declarations, or redirect components removed.
- No permission/capability checks changed on list pages.
- No API or `MenusController` merge logic changed beyond static default menu content.
- No shell layout or `RouteToSectionResolver` rule changes (except menu tree content affects prefix resolution for removed hrefs).

## Manual QA checklist

1. Open **Operations** control panel — every item navigates to a real page (no 404).
2. Confirm **no** API Explorer or Nested nav demo entries.
3. Confirm **one** start entry: **Execute** → `/execute`.
4. Open **Config** gear menu — **Managed contacts** opens `/config/contacts`.
5. **Modules** — single row, opens `/modules`.
6. **Schedules** list — “Create schedule” when `CanManageSchedules`.
7. **Integrations** list — “Create Integration” when `CanManageIntegrations`.
8. **Connections** — “Add connection” when create capability.
9. Direct navigation to `/operations/start` and `/demo/nested-nav` still works if needed for dev.

## Deferred work

- Full shell / domain navigation restructure (Epic 98 §10)
- Rename Automation “History” → Runs
- Breadcrumb / parent context component
- Domain landing pages (Operations vs Dashboard)
- Runtime / Admin menu policy
- API Explorer product page if desired later

## Validation

- `dotnet build` `Apperture.Management.Ui.Client`
- `dotnet test` `Apperture.Management.Ui.Shared.Tests` — Epic 99 + existing shell/menu tests

## References

- [Epic 98 — Navigation audit](Epic98-Management-UI-Navigation-and-Information-Flow-Audit.md)
- [control-panel-ui.md](../01-architecture/control-panel-ui.md)
