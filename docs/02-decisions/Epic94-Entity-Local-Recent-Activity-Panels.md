# Epic 94 — Entity-Local Recent Activity Panels

## Status

Accepted — May 2026.

## Purpose

This epic is the **first implementation slice** toward operation-history usefulness described in [Epic 91](Epic91-Operation-History-and-Action-Feedback-Groundwork.md). It adds bounded, drill-in-oriented **recent activity** panels on entity detail pages using **existing APIs only** — no global operation registry, no new persistence, and no backend contract changes.

Panels answer: *“What has recently happened for this entity, and where can I drill in?”*

Canonical context: [Epic 87](Epic87-Management-UI-Action-Surface-and-Operation-Feedback-Groundwork.md), [Epic 88](Epic88-Cooperative-Action-Feedback-Shell-Pilot.md), [Epic 90](Epic90-Management-UI-Action-Feedback-Corrective-Pass.md), [Epic 91](Epic91-Operation-History-and-Action-Feedback-Groundwork.md), [Epic 93](Epic93-Management-UI-Interaction-Primitive-Hardening.md), [management UI design system](../01-architecture/management-ui-design-system.md).

## Source data reviewed

| API / client method | Scope filter | Used on |
|---------------------|--------------|---------|
| `GetRunsAsync(RunQueryFilter)` | `integrationId`, `moduleId`, `createdFrom`, `take` | Integration Detail, Module Detail |
| `GetAuditTimelineAsync(...)` | `moduleId`, `fromUtc`, `pageSize` | Integration Detail (integration’s module), Module Detail |
| `ListNotificationInstancesAsync(integrationId, take)` | `integrationId` | Integration Detail only |
| `GetIntegrationOperatorDetailAsync` | (existing page load; not activity feed) | Integration Detail |
| `GetModuleAsync` | (existing page load) | Module Detail |

**Not used (no truthful entity filter without backend work):**

- `integrationId` on audit timeline (API supports `moduleId`, `scheduleId`, `runId` only).
- Schedule diagnostics timeline as activity rows (separate surface; not merged here).
- Global `ControlActivity` feed (instance-wide, not entity-local).

## Implemented panel(s)

### Integration Detail (`/integrations/{id}`)

- **Placement:** `ManagementSection` “Recent activity” immediately after `IntegrationSummaryBand`.
- **Sources merged (newest first, max 10, 7-day lookback, 5 per source before merge):**
  1. **Runs** — `RunQueryFilter(IntegrationId, CreatedFrom, Take=5)`; per-run authorization via existing run query service.
  2. **Audit** — when `CanViewAudit`; `moduleId` = integration’s `ModuleId` (module-scoped audit, not integration-id filter).
  3. **Notification instances** — when `CanViewNotificationDefinitions`; `ListNotificationInstancesAsync(integrationId, take: 5)`.
- **Row fields:** When (UTC), Source (Run / Audit / Notification), type label, summary, actor (audit), status/outcome pill, `DrillInLink` (run detail, schedule/run from audit refs, notification instance, or module audit).
- **Footer drill-ins:** Runs list with `integrationId` query; module audit; integration notifications route.
- **Failure isolation:** Section-level `PageMessage` Error; main integration content unchanged.

### Module Detail (`/modules/{id}`)

- **Placement:** `ManagementSection` “Recent activity” after state metadata.
- **Sources merged:** Authorized **runs** (`ModuleId` on definition via `RunQueryFilter`) and **audit** (`moduleId` filter) under same bounds as integration.
- **Footer drill-ins:** Module-filtered runs list (Epic 95); module audit when permitted.

### Shared UI

- `EntityRecentActivityItem` + `EntityRecentActivityComposer` (`Apperture.Management.Ui.Shared`) — mapping and merge only; no invented fields.
- `EntityRecentActivityPanel` (`Organisms`) — `LoadingState`, `EmptyState`, `PageMessage` Error, compact table, `StatusPill`, `DrillInLink`.

## Behavior and permission preservation

- **No** new backend APIs, tables, routes, menu entries, or capability flags.
- **No** changes to `ActionFeedback`, cooperative actions, or page-level load/error semantics beyond additive activity section.
- Audit rows omitted on 403; run list may return fewer than `take` when runs are unauthorized (existing service behavior).
- Activity load errors do not set page `_error` on Integration or Module detail.

## Gaps / deferred backend support

| Gap | Impact | Recommendation |
|-----|--------|----------------|
| Audit API has no `integrationId` filter | Integration activity uses **module-scoped** audit only | Add optional `integrationId` on audit records/query when integration-scoped audit events are first-class |
| Runs list UI lacks `moduleId` query param | Module footer cannot deep-link filtered runs list | **Resolved in [Epic 95](Epic95-Recent-Activity-Filter-and-Drill-In-Completion.md)** |
| No unified chronological “operation” row | Merge is client-side across heterogeneous sources | Defer global operation registry per Epic 91 Phase 2+ |
| Run timeline not shown on entity panel | Run rows link to Run Detail | Optional: add run timeline snippet on Run Detail before entity panels expand |

## Validation

- `dotnet build` on `Apperture.Management.Ui.Client`
- `dotnet test` filter `Epic94EntityRecentActivityTests`
- `ManagementUiSemanticsTests` when semantics touched (not required this epic)

### Manual QA notes

1. Open an integration with recent runs and (if permitted) audit on its module — verify “Recent activity” shows merged rows, drill-ins work, Refresh reloads section.
2. Deny or use principal without `audit.read` — audit rows absent; panel still shows runs/notifications when permitted.
3. Simulate audit API failure (e.g. stop API) — integration page body loads; activity section shows error message only.
4. Module detail after lifecycle action — activity reloads; `ActionFeedback` preserved.
5. Empty integration (no runs in window) — `EmptyState` in activity section.

## Next recommended slice

1. **Runs list:** add `moduleId` query param to match API filter for Module Detail footer drill-in.
2. **Schedule Detail / Connection Detail:** entity-local panels only where `scheduleId` / connection audit prefixes are truthful.
3. **Epic 91 Phase 1:** optional `ManagementOperationRecord` projection — only after more panels prove merge patterns; do not replace audit/run sources of truth.
