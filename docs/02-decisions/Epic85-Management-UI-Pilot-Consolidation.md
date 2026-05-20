# Epic 85 — Management UI pilot consolidation and design-system hardening

## Status

Accepted — implemented May 2026.

## Purpose

Consolidate learnings from the first five modernization pilots (Epics 80–84), harden the canonical Management UI design-system guidance, and extract only clearly repeated low-risk primitives.

This epic is explicitly a consolidation effort, not another page redesign.

Follow-on: [Epic 86 — Management UI state branch consistency sweep](Epic86-Management-UI-State-Branch-Consistency-Sweep.md) applies these state primitives to additional non-pilot pages without redesigning layouts.

## Pilots reviewed

- [Epic 80 — Integration Detail redesign pilot](Epic80-Integration-Detail-Redesign-Pilot.md)
- [Epic 81 — Run Detail redesign pilot](Epic81-Run-Detail-Redesign-Pilot.md)
- [Epic 82 — Control Center redesign pilot](Epic82-Control-Center-Redesign-Pilot.md)
- [Epic 83 — Schedules list/detail redesign pilot](Epic83-Schedules-List-Detail-Redesign-Pilot.md)
- [Epic 84 — Notification definition detail/authoring redesign pilot](Epic84-Notification-Definition-Authoring-Redesign-Pilot.md)

## Patterns confirmed

1. **Layering model holds across archetypes**:
   - Summary/orientation
   - Operational detail
   - Advanced/diagnostic
2. **State wrappers are effective defaults**:
   `LoadingState`, `EmptyState`, `ForbiddenState`, `ErrorState` are stable for page-level branches.
3. **Drill-in conventions remain truthful**:
   `DrillInLink` for normal links; plain anchor + `ui-drill-in-link` + stop-propagation when nested in clickable rows.
4. **Status/attention semantics should stay presentation-only**:
   shared mappings for broadly reused lifecycle/status tokens; domain-specific business meaning remains local.

## Primitives retained/refined

- **Retained:** `PageHeader`, `ManagementSection`, `ManagementSummaryCard`, `ManagementAdvancedSection`, `StatusPill`, `AttentionPill`, `DrillInLink`, state wrappers.
- **Refined:** added `ui-summary-card-grid` and `ui-summary-card-grid--wide` to `management-ui-primitives.css` and applied them where card-grid layout repetition was identical across pilot pages.

## Abstractions intentionally not added

- No generic “all-purpose summary band” abstraction.
- No shared “metadata row/panel” abstraction across all pilots.
- No expansion of `ManagementUiSemantics` for page-specific business interpretation.
- No operation shell/history framework, schema-driven forms framework, dashboard charting framework, wizard framework, or calendar abstraction.

## Documentation updates

- Hardened [management-ui-design-system.md](../01-architecture/management-ui-design-system.md) with:
  - pilot-proven archetype table,
  - consolidated component usage patterns,
  - drill-in guidance for clickable rows,
  - explicit “what not to abstract yet” guidance,
  - future-epic readiness notes.

## Behavior preservation

- No backend/API changes.
- No permission/capability changes.
- No route/menu changes.
- No page-level behavioral redesign beyond low-risk class normalization for existing summary-card grids.

## Validation

- `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
- `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter FullyQualifiedName~ManagementUiSemanticsTests`

## Deferred work

- Evaluate shared async action shell when multiple pages converge on identical action lifecycle telemetry/copy.
- Evaluate metadata-grid helper only when repeated structure converges without loss of domain meaning.
- Expand semantics mappings only when at least two pages require the same new presentation-only token mapping.
