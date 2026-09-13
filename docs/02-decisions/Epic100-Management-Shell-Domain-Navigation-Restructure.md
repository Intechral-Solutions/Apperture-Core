# Epic 100 — Management Shell Domain Navigation Restructure

## Status

Accepted — implemented May 2026.

## Purpose

[Epic 99](Epic99-Route-Menu-Truth-Cleanup.md) made the Control Panel menu **truthful** (no broken or demo entries). This epic improves **orientation**: clearer domain labels, Operations item ordering, Automation “Runs” naming, Config submenu consistency, and short hub copy so operators understand **Dashboard** (summary) vs **Control Center** (live console) — **without** moving routes or redesigning pages.

## Feature-focused audit

| Domain | Reviewed | Change |
|--------|----------|--------|
| **Dashboard** | Menu + `DashboardHome` | Page copy clarifies summary vs Control Center drill-in |
| **Operations** | Menu order, labels, attention grouping | Reordered; Audit shortened; Attention nested group |
| **Automation** | “History” / “Scheduling” labels | **Runs**, **Schedules** |
| **Integrations** | Single list entry | No change (label already clear) |
| **Connections** | Single list entry | No change |
| **Config** | Submenu order and labels | Overview label; auth/governance/notification label polish; contacts/modules order |
| **Modules** | Under Config, `/modules` path | Modules card on Config landing; menu unchanged path |

### Labels intentionally not changed

- Rail **domain names** (Operations, Automation, Integrations, …) — would require shell/icon map work.
- **Integrations** / **Connections** top-level menu text (already match hubs).
- **Dashboard** deferred personalization menu rows (still labeled deferred).
- Moving **Runs** to Operations domain (deferred route/shell policy).
- **Attention** placement under Config (deferred; grouped under Operations menu only).
- Runtime shutdown routes (still deep-link only).

### Route moves intentionally avoided

All `Href` values unchanged. No `@page` route renames.

## Changes implemented

### `ControlPanelMenuDefinitions.cs`

**Operations** (order + labels + grouping):

1. Control Center  
2. Blocked Work  
3. Execute  
4. Audit (was Audit Timeline)  
5. **Attention** (parent) → Operational teams, Routing defaults  

**Automation:**

- History → **Runs**  
- Scheduling → **Schedules**  

**Config:**

- Config → **Overview** (still `/config`)  
- Sign-in chooser → **Sign-in experience**  
- Entra registrations → **Entra tenants**  
- Permission Catalog → **Permission catalog**  
- Submenu order: Overview → Authentication → Governance → Notifications → Managed contacts → Modules  

### Hub orientation copy (minimal)

| Page | Adjustment |
|------|------------|
| `DashboardHome.razor` | Summary vs Control Center distinction + drill-in link |
| `ControlCenter.razor` | Subtitle notes live console vs Dashboard |
| `ConfigLanding.razor` | Subtitle lists admin areas; **Modules** summary card added |
| `RunsList.razor` | Title **Runs**, Automation eyebrow, Control Center cross-reference |
| `ExecutionStart.razor` | Broader orientation vs console/runs |

### Tests

- `Epic100ShellDomainNavigationTests.cs` — labels, order, attention grouping, section resolution  
- `ShellSectionOwnerTests`, `Epic41ConfigGovernanceUiTests` — Config **Overview** label  

## Routes preserved

Every route path from Epic 98/99 unchanged (`/control/*`, `/runs`, `/execute`, `/config/*`, `/modules`, attention paths, legacy starts, etc.).

## Current navigation model

| Domain | Role |
|--------|------|
| **Dashboard** | High-level instance summary (cards, drill-outs). Default after `/` redirect. |
| **Operations** | Live operational work: Control Center console, blocked work remediation, **Execute** start surface, audit timeline, attention teams/routing. |
| **Automation** | **Runs** history/detail and **Schedules** definitions — time-based execution artifacts. |
| **Integrations** | Integration fleet list and detail (configuration, bindings, scoped notifications/diagnostics). |
| **Connections** | Connection instances and connector catalog. |
| **Config** | Platform admin: auth, governance, notifications, managed contacts, installed modules (`/modules` under Config section owner). |

## Behavior preservation

- No permission, API, `MenusController` merge, or `RouteToSectionResolver` algorithm changes.
- Module-contributed navigation unchanged.
- Deep links and legacy routes unchanged.

## Manual QA checklist

1. **Dashboard** — copy mentions Control Center; link works.  
2. **Operations** menu — order: CC → Blocked → Execute → Audit → Attention (expand).  
3. **Automation** — **Runs** and **Schedules** labels.  
4. **Runs** page — title “Runs”, not “Automation History”.  
5. **Control Center** — subtitle mentions Dashboard.  
6. **Config** gear — **Overview**, ordered sub-areas, **Managed contacts**, **Modules**.  
7. **Config landing** — Modules card present.  
8. **Execute** — orientation copy visible.  
9. Rail highlights correct domain on `/runs`, `/control/center`, `/config/auth`, `/modules`, `/attention/routing-defaults`.

## Deferred work

- Breadcrumb / parent context component  
- Domain landing page redesign (Operations/Automation hubs)  
- Possible future Runs under Operations (route prefix policy)  
- Runtime/Admin explicit menu policy  
- Shell/nav visual redesign  
- API Explorer product decision  
- Rail tooltip/domain rename pass  

## Validation

- `dotnet build` `Apperture.Management.Ui.Client`  
- `Epic100ShellDomainNavigationTests`, `Epic99RouteMenuTruthCleanupTests`, `ShellSectionOwnerTests`, `Epic41ConfigGovernanceUiTests`

## References

- [Epic 98 — Navigation audit](Epic98-Management-UI-Navigation-and-Information-Flow-Audit.md)  
- [Epic 99 — Menu truth cleanup](Epic99-Route-Menu-Truth-Cleanup.md)  
- [control-panel-ui.md](../01-architecture/control-panel-ui.md)
