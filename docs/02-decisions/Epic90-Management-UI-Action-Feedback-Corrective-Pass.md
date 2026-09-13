# Epic 90 — Management UI Action Feedback Corrective Pass

## Status

Accepted — implemented May 2026.

## Purpose

Address **corrective findings COR-001 through COR-003** from [Epic 89](Epic89-Management-UI-Modernization-Progress-Audit.md) before expanding `ActionFeedback` adoption or starting operation-history groundwork.

This is a **targeted corrective pass**, not a new framework epic. No operation registry, global shell, or backend changes.

Canonical context: [Epic 87](Epic87-Management-UI-Action-Surface-and-Operation-Feedback-Groundwork.md), [Epic 88](Epic88-Cooperative-Action-Feedback-Shell-Pilot.md), [management UI design system](../01-architecture/management-ui-design-system.md).

## Findings addressed

| ID | Finding | Resolution |
|----|---------|------------|
| **COR-001** | `RunDetail` / `ScheduleDetail` `LoadAsync` cleared `_actionMessage` (and `_newRunId` on runs) after every action reload, so `ActionFeedback` never persisted | Added optional `preserveActionFeedback` parameter; post-action reloads pass `true`. Schedule detail markup aligned with run detail (banner above content, not exclusive branch). |
| **COR-002** | `BlockedWork` and `ModuleDetail` still used raw `PageMessage` Info for `_actionMessage` | Replaced with `ActionFeedback`; same `preserveActionFeedback` pattern on post-action reload. Restructured branches so action banner does not hide list/module content. |
| **COR-003** | `NotificationDiagnostics`, `NotificationInstanceDetail`, `IntegrationNotifications` showed `_error` as muted text | Promoted action/load failures to `PageMessage` Error (inline section errors; not `ErrorState` — page content remains visible). |
| **COR-004** | Design-system doc drift through Epics 86–89 | Verified fixed in Epic 89; added Epic 90 cross-link in design system. |

## Code changes

| Page | Change |
|------|--------|
| `Pages/Runs/RunDetail.razor` | `LoadAsync(bool preserveActionFeedback = false)`; `InvokeActionAsync` reload preserves `_actionMessage` and `_newRunId`. |
| `Pages/Schedules/ScheduleDetail.razor` | Same preserve flag; `ExecuteScheduleActionAsync` reload preserves feedback; `ActionFeedback` shown above detail while loading or loaded. |
| `Pages/Control/BlockedWork.razor` | `ActionFeedback` + preserve flag on retry/trigger reload; table remains visible with banner. |
| `Pages/Modules/ModuleDetail.razor` | `ActionFeedback` + preserve flag on lifecycle reload; module state remains visible with banner. |
| `Pages/Config/Notifications/NotificationDiagnostics.razor` | Section `_error` → `PageMessage` Error. |
| `Pages/Config/Notifications/NotificationInstanceDetail.razor` | Outbox/load `_error` → `PageMessage` Error below detail. |
| `Pages/Integrations/IntegrationNotifications.razor` | Action/load `_error` → `PageMessage` Error. |

## Behavior preservation

- Routes, menu entries, and navigation unchanged.
- Action handlers, eligibility, capability gates, and API calls unchanged.
- 403/409/HTTP and success copy unchanged; feedback kind remains Info for cooperative actions.
- Manual **Refresh** and initial page load still clear action feedback (default `preserveActionFeedback: false`).
- Run detail polling still clears action feedback on poll tick (unchanged intent).
- Notification/integration warnings, degraded-health copy, and operational diagnostics remain muted text — not promoted to `ErrorState`.

## Validation

Recorded at implementation time:

- `dotnet build` on `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
- `dotnet test` — `ManagementUiSemanticsTests`

### Manual QA recommendations

1. **Run detail** — Cancel/retry/park/resume: confirm `ActionFeedback` persists after reload; retry shows new-run drill-in link.
2. **Schedule detail** — Enable/disable/trigger: confirm banner persists and schedule sections remain visible.
3. **Blocked work** — Retry eligible item: confirm `ActionFeedback` and optional new-run link with table still shown.
4. **Module detail** — Install/enable/disable/uninstall: confirm `ActionFeedback` with module state visible.
5. **Notification diagnostics** — Trigger a failing diagnostic action: confirm red `PageMessage` Error at section bottom (not muted text).
6. **Notification instance detail** — Simulate outbox load failure: detail visible with `PageMessage` Error.
7. **Integration notifications** — Subscription/sign-off failure: `PageMessage` Error; cutover readiness warnings unchanged.

## Deferred work

- Operation history / persistent async operation shell
- Operation registry / global service
- Backend operation lifecycle APIs
- Toast normalization (Integration Detail, governance)
- Broader `ActionFeedback` adoption beyond COR-002 targets
- Mapping cooperative 403/409 to `Kind=Error` (ADV-002)
- `PageHeader` incremental adoption (ADV-001)

**Recommended next epic:** Operation history groundwork — [Epic 91](Epic91-Operation-History-and-Action-Feedback-Groundwork.md).
