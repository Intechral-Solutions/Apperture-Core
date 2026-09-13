# Epic 79 — Management UI design system foundation

## Status

Accepted — implemented May 2026.

## Context

[Epic 78](Epic78-Management-UI-Modernization-Groundwork.md) documented page archetypes, layering, drill-ins, and pilot recommendations but intentionally did not add components or tokens. Operators still see inconsistent section/card patterns across pages.

## Decision

1. **Documentation:** [management-ui-design-system.md](../01-architecture/management-ui-design-system.md) is the canonical design-system reference, linked from [control-panel-ui.md](../01-architecture/control-panel-ui.md) and Epic 78.
2. **Semantics:** `ManagementUiSemantics` in `Apperture.Management.Ui.Shared` maps status/attention tokens to shared CSS pill classes.
3. **Primitives (Blazor):** Minimal components in existing `Atoms` / `Molecules` / `Organisms` folders:
   - `StatusPill`, `AttentionPill`, `DrillInLink`
   - `ManagementSection`, `ManagementSummaryCard`, `ManagementAdvancedSection`
   - `LoadingState`, `EmptyState`, `ForbiddenState`, `ErrorState` (delegate to `PageMessage`)
4. **CSS:** `wwwroot/css/management-ui-primitives.css` loaded after `app.css` in **both** the WASM `wwwroot/index.html` and **`Apperture.Management.Ui.Server/Pages/_Layout.cshtml`** (dev host on port 9001); uses existing semantic theme variables.
5. **Refinements:** `PageHeader` gains optional `Eyebrow`; no replacement of `PageHeader` / `PageMessage`.
6. **Proof:** `/config` hub landing uses new primitives only; no large-page refactors.

## Consequences

- Epic 80+ pilot epics can adopt primitives incrementally on routes named in Epic 78 §9 (see [Epic 80](Epic80-Integration-Detail-Redesign-Pilot.md), [Epic 81](Epic81-Run-Detail-Redesign-Pilot.md), [Epic 82](Epic82-Control-Center-Redesign-Pilot.md), [Epic 83](Epic83-Schedules-List-Detail-Redesign-Pilot.md), [Epic 84](Epic84-Notification-Definition-Authoring-Redesign-Pilot.md)); [Epic 85](Epic85-Management-UI-Pilot-Consolidation.md) hardens cross-pilot guidance and low-risk shared primitives.
- Dashboard attention styling can migrate from inline classes to `AttentionPill` when touched.
- New status visuals should use `ManagementUiSemantics` rather than ad-hoc Tailwind color classes.

## Non-goals

As Epic 78 deferrals: no schema forms, charts, module dashboards, wizards, or calendar features. Large-page refactors proceed only via pilot epics (e.g. Integration Detail, Run Detail, Control Center).

## Validation

- `dotnet build` on Management UI client and unit tests for semantics.
- `/config` route renders four summary cards with unchanged hrefs.
