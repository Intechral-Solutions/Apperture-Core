# Epic 83 — Schedules list and detail redesign pilot

## Status

Accepted — implemented May 2026.

## Context

Epic 78 recommended `/schedules` + `/schedules/{ScheduleId}` as pilot **#5**: a scoped list/detail pair representing automation governance with known Epic 47 limitations (no degraded-only list filter).

Epics 79–82 established Management UI primitives and proved layering on config, integrations, runs, and control surfaces.

Prior to Epic 83, schedules list and detail used ad‑hoc headings, bordered grids, and inline links without a consistent summary/detail/advanced posture.

## Why this pilot

- Completes Epic 78’s pilot roadmap for schedules without backend or cron contract churn.
- Reuses Epic 79 patterns already validated on sibling routes.
- Leaves room for future degraded filtering when APIs support it truthfully.

## Before / after layering

### `/schedules` (list)

| Layer | Before (Epic 77 baseline) | After (Epic 83) |
|-------|---------------------------|-----------------|
| **Header** | Plain title + create/refresh | `PageHeader` Eyebrow **Automation**, subtitle, same toolbar actions |
| **Summary** | None | Enabled/disabled/total pills from loaded rows only; summary cards → integrations, runs, audit, blocked work |
| **Detail** | Single table | `ManagementSection` “All schedules”: table + `StatusPill` / `DrillInLink` for integration drill-in; recurrence visible from API field |
| **Advanced** | None | Collapsed notes: list DTO constraints; Epic 47 degraded-filter caveat |

### `/schedules/{ScheduleId}` (detail)

| Layer | Before (Epic 77 baseline) | After (Epic 83) |
|-------|---------------------------|-----------------|
| **Header** | Title “Schedule” + mono id | `PageHeader` Eyebrow **Schedules**, dynamic title/name, contextual subtitle string from API fields |
| **Summary** | None | Pills: enabled, target kind, optional last outcome semantic; cards: integration (+ schedule diagnostics href when integration id exists), runs (correlation drill-in), audit, blocked work |
| **Detail** | State + Links panels | Configuration & execution facts in one routine section + dedicated links section (`DrillInLink`) aligned with Epic 33 helpers |
| **Advanced** | Claim block inlined in state grid | Progressive disclosure: mono id/version + active scheduler claim / stale wording |

### `/schedules/new` (minimal consistency pass)

Same create flow; `PageHeader`, `ForbiddenState` / `ErrorState`, single `ManagementSection` wrapper for parity with Epic 82/81 authoring surfaces.

## Epic 79 components used

| Component | Usage |
|-----------|-------|
| `PageHeader` + `Eyebrow` | Automation / schedules context |
| `ManagementSection` | Summary, primary content, definitions |
| `ManagementSummaryCard` | Cross-domain drill-outs (integrates existing link targets) |
| `ManagementAdvancedSection` | Diagnostics / caveats / technical claim metadata |
| `StatusPill` | Enabled/disabled posture, last outcome (semantic via existing `ManagementUiSemantics.MapRunOutcomeSemantic`) |
| `AttentionPill` | Stale scheduler claim spotlight |
| `DrillInLink` | Operational navigation |
| `LoadingState` / `EmptyState` / `ErrorState` / `ForbiddenState` | Truthful page states |

No new semantic helpers were introduced in `ManagementUiSemantics`.

## Behavior preservation rules

- **No API/route/permission/menu changes**.
- **`GetSchedulesAsync` / `GetScheduleAsync`** and all enable/disable/recovery/editor actions untouched in contract.
- **Validation error payload** surfaced the same strings (now via `ErrorState` child markup where applicable).
- **Schedule correlation / audit hrefs** still produced through `Epic33UiDisplayHelpers`.
- **`/schedules/new`** still drives `CreateIntegrationScheduleAsync` and navigates to created detail route.

## Known deferred work

- Server-side degraded-only filtering for schedules list remains blocked on API support (Epic 47 backlog).
- Optional secondary chips when query params expose filters (today list has no persisted filters).

## References

| Doc | Relationship |
|-----|---------------|
| [Epic 78](Epic78-Management-UI-Modernization-Groundwork.md) | Pilot #5 rationale |
| [Management UI design system](../01-architecture/management-ui-design-system.md) | Primitives canon |
| [Epic 79](Epic79-Management-UI-Design-System-Foundation.md) | Foundations |
| [Epic 33](Epic33-Management-UI-Deepening.md) | Schedule action copy; audit/runs correlation helpers |
| [Epic 80](Epic80-Integration-Detail-Redesign-Pilot.md), [Epic 81](Epic81-Run-Detail-Redesign-Pilot.md), [Epic 82](Epic82-Control-Center-Redesign-Pilot.md) | Adjacent layering pilots |

## Validation

- `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`.
- Focused semantics slice: `--filter FullyQualifiedName~ManagementUiSemanticsTests`.
