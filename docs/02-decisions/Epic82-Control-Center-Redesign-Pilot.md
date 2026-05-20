# Epic 82 — Control Center layered console redesign pilot

## Status

Accepted — implemented May 2026.

## Context

Epic 78 identified Control Center (`/control/center`) as pilot **#1** for Management UI modernization because it is the default multi-source operator console with high section density. Epic 79 introduced reusable primitives and Epic 80/81 validated layering on entity-detail pages.

Before Epic 82, Control Center presented platform health, targets, recent activity, and attention in a mostly flat stack with uneven visual priority and ad-hoc status/link styling.

## Why this pilot

- Implements the Epic 78 operator-console archetype where scanability is most critical.
- Applies Epic 79 primitives to a multi-source console without changing route, permissions, links, or API contracts.
- Keeps actionable operational concerns visible while moving low-frequency diagnostics to a progressive disclosure section.

## Before / after layering

| Layer | Before (Epic 77 baseline) | After (Epic 82) |
|-------|---------------------------|-----------------|
| **Header** | Ad-hoc heading + refresh button | `PageHeader` with Eyebrow **Operations**, subtitle, same refresh action |
| **Summary** | No dedicated orientation layer | `ManagementSection` summary with status pills (blocked work, attention, activity, platform health) + `ManagementSummaryCard` quick links |
| **Detail** | Platform health, targets, activity, attention as equal-weight blocks | `ManagementSection` groups: Platform health, Operational concerns, Targets, Recent activity |
| **Advanced** | No progressive disclosure for verbose diagnostics | `ManagementAdvancedSection`: degradation reasons, restart-bound surfaces, not-yet-implemented surfaces |
| **State wrappers** | Ad-hoc loading/error text | `LoadingState`, `ErrorState`, `EmptyState`, `ForbiddenState` (runtime health section only) |

## Epic 79 components used

| Component | Usage |
|-----------|--------|
| `PageHeader` + `Eyebrow` | Operations context and preserved refresh toolbar action |
| `ManagementSection` | Summary, platform health, operational concerns, targets, recent activity |
| `ManagementSummaryCard` | Quick navigation: blocked work, runs, schedules, audit, integrations |
| `ManagementAdvancedSection` | Low-level diagnostics and platform evidence |
| `StatusPill` | Summary facts, platform status, recent-activity status |
| `AttentionPill` | Attention severity token visualization |
| `DrillInLink` | Existing navigation targets (`/runs`, `/schedules`, `/modules`, `/control/*`) |
| `LoadingState` / `ErrorState` / `EmptyState` / `ForbiddenState` | Truthful page/section states |

## Behavior preservation rules

- **No API or route changes** — still uses `GetControlCenterAsync` and optional `GetRuntimeHealthAsync`; route remains `/control/center`.
- **No permission changes** — runtime health fetch remains capability-gated and still handles 403 as unavailable.
- **No action semantic changes** — refresh behavior, target action availability display, attention and activity link logic unchanged.
- **No navigation target changes** — same destinations for blocked work, runs, schedules, modules, audit, runtime request detail.
- **No inferred business truth** — summary and pills are derived directly from existing DTO fields and helper counts.

## Known deferred work

- Optional extraction of targets/activity tables into dedicated presentational child components if reused in other consoles.
- Evaluate whether recent-activity status semantics should move to shared `ManagementUiSemantics` when another page needs the same mapping.
- Other Epic 78 pilots: notification definition detail (#4), schedules list/detail (#5) — [Epic 83](Epic83-Schedules-List-Detail-Redesign-Pilot.md).

## Validation

- `Apperture.Management.Ui.Client` builds successfully.
- Relevant unit test slice still passes (`ManagementUiSemanticsTests`).

## References

- [Epic 78 — Management UI modernization groundwork](Epic78-Management-UI-Modernization-Groundwork.md)
- [Management UI design system](../01-architecture/management-ui-design-system.md)
- [Epic 79 — Design system foundation](Epic79-Management-UI-Design-System-Foundation.md)
- [Epic 80 — Integration Detail redesign pilot](Epic80-Integration-Detail-Redesign-Pilot.md)
- [Epic 81 — Run Detail redesign pilot](Epic81-Run-Detail-Redesign-Pilot.md)
