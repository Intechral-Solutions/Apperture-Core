# S3 — Product shell & operator-focused UI conventions

**Status:** Accepted (Epic S3)  
**Scope:** Information architecture and navigation separation for v1 stabilization — not the full visual overhaul or wizard framework.

## Principles

1. **Operator-facing first** — Primary navigation emphasizes integrations, operational control, runs, schedules, and actionable issues. Summaries and outcomes appear before internal machinery.
2. **Progressive disclosure** — Module IDs, raw diagnostics, and low-level platform details live in advanced sections or developer utilities, not in page headers or primary nav labels.
3. **Developer tasks separated** — Deferred features, demos, and cutover diagnostics are grouped under **Config → Developer & internal** (`/config/developer`), not in the Dashboard or Operations menus.
4. **No placeholders in primary nav** — Stub or deferred routes remain routable (for bookmarks and tests) but are not advertised as normal product features.
5. **Summaries first, details second** — List and detail pages lead with health, status, and next actions; technical metadata follows in collapsible or advanced sections.

## Navigation zones

| Zone | Owner | Audience | Examples |
|------|-------|----------|----------|
| Dashboard (logo) | Operator | Day-to-day situational awareness | `/dashboard` |
| Operations, Automation, Integrations, Connections | Operator | Run work, monitor outcomes | `/control/center`, `/runs`, `/integrations` |
| Config (gear) | Platform admin | Authentication, governance, notifications, attention | `/config/auth`, `/config/governance` |
| Platform maintenance (Config subgroup) | Platform admin | Runtime flags, modules | `/config/platform-settings`, `/modules` |
| Developer & internal (Config) | Engineering / cutover | Deferred dashboards, diagnostics, demos | `/config/developer` |

## Deferred items moved out of primary navigation

| Route | Previous location | New location |
|-------|-------------------|--------------|
| `/dashboard/manage` | Dashboard menu | Developer tasks hub |
| `/dashboard/add` | Dashboard menu | Developer tasks hub |
| `/dashboard/delete` | Dashboard menu | Developer tasks hub |
| `/dashboard/layout` | Dashboard menu | Developer tasks hub |
| `/config/notifications/diagnostics` | Notifications menu children | Developer tasks hub (still linked from Notifications landing) |

## Code references

- Menu definitions: `ControlPanelMenuDefinitions.cs`
- Developer route constants: `DeveloperTaskRoutes.cs`, `ShellSectionRoutes.DeveloperTasks`
- Developer hub page: `Pages/Config/DeveloperTasks.razor`

## Out of scope (S3)

- Reusable wizard framework
- Notification subscriptions UI
- Full page decomposition or visual redesign
- Module-supplied UI, System/Service taxonomy, calendar/date-periods
