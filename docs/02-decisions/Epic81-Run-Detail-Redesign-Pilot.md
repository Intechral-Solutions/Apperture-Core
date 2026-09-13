# Epic 81 — Run Detail redesign pilot

## Status

Accepted — implemented May 2026.

## Context

Epic 78 identified Run Detail (`/runs/{RunId}`) as pilot **#2** (execution hot path, high read traffic). Epic 79 introduced Management UI primitives and proved them on `/config` and in the Epic 80 Integration Detail pilot.

Run Detail already exposed rich forensics (Epic 35), cooperative actions, timeline, and supporting records, but the page read as a long stack of equal-weight bordered panels.

## Why this pilot

- Validates summary/detail/advanced layering on a **run-scoped** execution surface without changing APIs, routes, or permissions.
- Reuses the Epic 79 vocabulary consistently with Integration Detail (Epic 80).
- Keeps **canonical timeline** and **result** in the detail layer while collapsing **raw supporting evidence** and **technical identifiers** into advanced.

## Before / after layering

| Layer | Before (Epic 77 baseline) | After (Epic 81) |
|-------|---------------------------|-----------------|
| **Summary** | Status and key facts scattered in grids and duplicated headers | `RunSummaryBand`: `StatusPill` / outcome pill, `AttentionPill` for blocked work, origin line, created/completed/current step, `ManagementSummaryCard` quick links |
| **Detail** | Investigation, actions, overview, result, timeline as similar-weight boxes | `ManagementSection`: Investigation, action availability, related context, execution overview, **Result**, **Execution timeline**; **SDK-3D:** `RunOperationItemizationPanel` (itemized business outcomes) below summary band |
| **Advanced** | Start request, ids, JSON payloads mixed with operator narrative | `ManagementAdvancedSection`: technical ids + correlation + start request metadata/JSON; **Supporting records** (lifecycle/step/control tables) |
| **Page chrome** | Basic header | `PageHeader` with Eyebrow **Runs**, subtitle = execution kind · status · integration (if any) · origin kind (from forensics when present); mono `RunId` preserved |

## Epic 79 components used

| Component | Usage |
|-----------|--------|
| `PageHeader` + `Eyebrow` | Runs domain; title **Run**; subtitle from existing DTO + forensics |
| `RunSummaryBand` | Summary snapshot and drill-in cards |
| `ManagementSection` | Investigation, actions, related context, execution overview, result, timeline |
| `ManagementAdvancedSection` | Technical identifiers + start request; supporting records |
| `ManagementSummaryCard` | Integration, inferred schedule, audit, blocked work, correlation-filtered runs |
| `StatusPill` / `AttentionPill` | Lifecycle + outcome semantics; blocked work |
| `DrillInLink` | Cross-entity navigation (unchanged targets) |
| `LoadingState` / `ErrorState` / `EmptyState` | Page and in-section empty states |

## Presentation-only semantics

- `ManagementUiSemantics.MapRunLifecycleStatus` and `MapRunOutcomeSemantic` map API strings to `ManagementStatusSemantic` for pills only; labels remain API text.

## Behavior preservation rules

- **No API, route, capability, or permission changes** — same `IManagementClient` calls and action kinds.
- **Polling, refresh, cancel/retry/park/resume** unchanged.
- **403 / errors / not found** messaging unchanged in meaning (`ErrorState` + same strings).
- **No inferred business truth** beyond existing `RunForensicContextDto` and DTO fields already shown.

## Known deferred work

- Optional: `ForbiddenState` for 403 if we standardize across pages in a later UX pass.
- Toolbar buttons → shared secondary button style (Epic 79 follow-up).
- Further extraction of timeline or result panels if reused elsewhere.
- Other Epic 78 pilots: Control Center (#1) — [Epic 82](Epic82-Control-Center-Redesign-Pilot.md), notification definition detail (#4) — [Epic 84](Epic84-Notification-Definition-Authoring-Redesign-Pilot.md), schedules list/detail (#5) — [Epic 83](Epic83-Schedules-List-Detail-Redesign-Pilot.md).

## Validation

- `Apperture.Management.Ui.Client` builds successfully.
- `ManagementUiSemanticsTests` covers `MapRunLifecycleStatus` and `MapRunOutcomeSemantic`.

## References

- [Epic 78 — Management UI modernization groundwork](Epic78-Management-UI-Modernization-Groundwork.md)
- [Management UI design system](../01-architecture/management-ui-design-system.md)
- [Epic 79 — Design system foundation](Epic79-Management-UI-Design-System-Foundation.md)
- [Epic 80 — Integration Detail redesign pilot](Epic80-Integration-Detail-Redesign-Pilot.md)
- [Epic 82 — Control Center redesign pilot](Epic82-Control-Center-Redesign-Pilot.md)
- [Epic 84 — Notification definition detail/authoring redesign pilot](Epic84-Notification-Definition-Authoring-Redesign-Pilot.md)
