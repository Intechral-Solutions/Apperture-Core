# Epic 95 — Recent Activity Filter and Drill-In Completion

## Status

Accepted — May 2026.

## Purpose

[Epic 94](Epic94-Entity-Local-Recent-Activity-Panels.md) added entity-local recent activity panels but left **truthful drill-in gaps**: the Runs list did not accept `moduleId` query params even though `RunQueryFilter` and `GET /api/runs` already support them, and Integration Detail help text could be read as implying integration-scoped audit.

This epic **completes** those gaps without new backend work, operation registry, or additional entity panels unless existing filters are already truthful.

## Existing source data reviewed

| Surface | Filter support | Epic 95 action |
|---------|----------------|----------------|
| `RunQueryFilter.ModuleId` | SQL + definition match in `RunQueryService`; `RunsController` `[FromQuery] moduleId` | Wired to Runs list UI |
| `ManagementClient.BuildRunsQuery` | Passes `moduleId` query param | Unchanged (already correct) |
| `RunQueryFilter.IntegrationId` | End-to-end | Preserved on Runs list |
| `GetAuditTimelineAsync(moduleId)` | Module-scoped only | Help text clarified on Integration Detail; no `integrationId` filter added |
| Schedule Detail | Audit: `scheduleId`; Runs: no `scheduleId` on `RunQueryFilter` | **Deferred** — audit-only partial scope |
| Connection Detail | No `connectionId` on audit or runs APIs | **Deferred** |

## Changes implemented

### Runs list (`/runs`)

- `[SupplyParameterFromQuery(Name = "moduleId")]` → `RunQueryFilter.ModuleId` on load.
- URL filter banner shows active module id; `BuildLoadKey` includes `moduleId` so navigation refreshes correctly.
- Existing `correlationKey`, `status`, `createdFrom`, `integrationId`, and execution-kind dropdown unchanged.

### Module Detail footer

- Footer drill-in: `Epic33UiDisplayHelpers.GetRunsListModuleFilterHref(ModuleId)` with label **“View module runs”** (replaces unfiltered `/runs`).

### Integration Detail copy

- `RecentActivityHelp` states audit is **module-scoped** and that the audit API has **no `integrationId` filter** (rows may include other integrations sharing the module).
- Runs footer uses `GetRunsListIntegrationFilterHref` for consistency.

### Shared drill-in helpers (`Epic33UiDisplayHelpers`)

- `GetRunsListModuleFilterHref(moduleId)`
- `GetRunsListIntegrationFilterHref(integrationId)`

## Schedule / Connection recent activity readiness

### Schedule Detail (`/schedules/{id}`)

| Source | Entity filter | Verdict |
|--------|---------------|---------|
| Audit timeline | `scheduleId` | Truthful for schedule-scoped audit events |
| Runs list | No `scheduleId`; optional `correlationKey` for `schedule:{id}:*` convention | Weak / convention-dependent; not used for Epic 94-style merge |
| Schedule detail fields | Last dispatch outcome on page | Already on detail; not a merged activity feed |

**Recommendation:** Defer schedule recent-activity panel until product defines whether schedule-origin runs should use correlation-key filtering in list API/UI or a dedicated schedule-run query.

### Connection Detail (`/connections/{id}`)

| Source | Entity filter | Verdict |
|--------|---------------|---------|
| Audit | No `connectionId` filter | Not truthful |
| Runs | No `connectionId` on `RunQueryFilter` | Not truthful |
| Connection test/verify | Connection DTO state | Not a chronological activity feed |

**Recommendation:** Defer until audit or run records carry stable `connectionId` references and query APIs expose them.

## Behavior preservation

- No backend, persistence, route, menu, or capability changes.
- Runs list empty/error/loading semantics unchanged (`EmptyState`: “No runs found.”).
- Epic 94 activity panels unchanged except Module footer link and Integration help/footer href helpers.

## Deferred items

- Integration-scoped **audit** filter (`integrationId` on audit API/records).
- Schedule/Connection entity-local recent activity panels.
- Global operation history / registry (Epic 91 Phase 2+).
- Runs list: combined filter hint when both `moduleId` and `integrationId` are present (edge case; no product requirement yet).

## Validation

- `dotnet build` `Apperture.Management.Ui.Client`
- `Epic94EntityRecentActivityTests` + `Epic95RecentActivityDrillInTests`
- `Epic33UiTruthfulnessTests` (link helpers)

### Manual QA notes

1. `/runs?moduleId={moduleId}` — list shows only runs whose workflow/operation definition matches that module (subject to per-run authorization).
2. `/runs?integrationId={id}` — still works as before.
3. Module Detail → “View module runs” opens filtered list.
4. Integration Detail help text — audit scope described as module-scoped, not integration-scoped.
5. `/runs?moduleId={id}` with no matching runs — standard empty state; filter banner still visible.

## Follow-on from Epic 94

See [Epic 94 § Gaps](Epic94-Entity-Local-Recent-Activity-Panels.md) — **Runs list `moduleId` query** item is resolved by this epic.
