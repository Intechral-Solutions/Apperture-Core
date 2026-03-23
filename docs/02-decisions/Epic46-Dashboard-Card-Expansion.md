# Epic 46 — Dashboard card expansion and drill-in surfaces

Date: 2026-03-23

## Status

Accepted

## Context

Epic 45 established the shared default operator dashboard, the `IDashboardCardProvider` model, partial-load truthfulness, and permission-driven omission. The next step is to make that dashboard more operationally useful with a small set of additional real cards, better drill-ins, lightweight query support where needed, and clearer attention/severity conventions — without personalization, layout editing, or a widget framework.

## Decision

1. **New cards (four)** — All implemented as `IDashboardCardProvider` in `Apperture.ControlPlane.Security`, registered with existing governance DI:
   - **Executions (24h)** — Single permission-filtered `IRunQueryService.ListRunsAsync` over a 24h window; facts for failed / in progress / completed / cancelled / other; **Warning** if any failed (not Critical, to avoid double-alarm with Needs Attention). Omitted when there is no authenticated actor. Drill-in: `/runs`, or `/runs?status=failed&createdFrom=…` when failures exist.
   - **Schedules** — Omitted unless `IScheduleGovernanceService.CanManageAsync` (same gate as schedules API). Data from `IScheduleManagementService.ListDefinitionsAsync` (enabled/disabled/total only; no N× execution state). Drill-in: `/schedules`.
   - **Audit (24h)** — Omitted unless `audit.read` on `AuditPolicy` at instance scope. Exact count via new `IAuditService.CountAsync` with the same filter shape as `QueryAsync` (implemented in `SqlAuditService`). Drill-in: `/control/audit`.
   - **Governance** — Omitted unless `user.read` on `Role` at instance scope (aligned with governance descriptor catalog). Data from `IRoleProvider.GetRolesAsync` (role count and custom role count). Drill-in: `/config/governance`.

2. **Drill-in corrections** — **Needs Attention** now links to `/control/center` (where the attention list and per-item drill-ins live), not blocked work. **Recent Activity** remains `/control/center` (unified activity table). **Runs** list accepts **`status`** and **`createdFrom`** query parameters and passes them through `RunQueryFilter` so dashboard and bookmarks can deep-link.

3. **Attention convention** — `DashboardCardAttentionConvention` in `Apperture.ControlPlane.Dashboard` documents and centralizes mappings: runtime health overall status, attention severity tokens, auth posture (including **Critical** for `EffectiveAuthExperienceKind.NoViableSignIn`), execution failure counts (Warning), schedules definitions snapshot, audit volume (Info), governance role count, blocked work count.

4. **Navigation labels** — Optional `NavigationLabel` on `DashboardCard` is exposed through the API and client DTO; dashboard home uses it for link text when set, otherwise “Open details”.

5. **Shared scope copy** — Dashboard home includes a short line stating the view is shared, not personalized, and not layout-managed.

6. **Unchanged** — Partial-load placeholder, permission omission (no locked stubs), single shared scope, provider ordering by `Order`, no dashboard builder.

## Consequences

- Operators with the right permissions see more truthful, actionable summaries and land on the correct surfaces from the dashboard.
- `IAuditService` gains `CountAsync` for efficient audit volume without paging tricks.
- `Apperture.ControlPlane.Dashboard` references `Apperture.Core.Application` for `RuntimeHealthStatus` constants used by the attention convention.

## Corrective pass (recommended)

- Manual click-through each card link (including filtered `/runs` from Executions when failures exist).
- Confirm role mix in real environments for the Governance card (system vs custom counts).
- If WorkerHost or other processes lock build outputs locally, stop them before full solution `dotnet test`.

## Explicit deferrals

- Per-user personalization, multi-page dashboard management UX, drag-and-drop layout, widget marketplace — unchanged from Epic 45.
