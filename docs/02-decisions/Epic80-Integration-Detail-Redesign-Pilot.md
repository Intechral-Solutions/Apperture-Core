# Epic 80 — Integration Detail redesign pilot

## Status

Accepted — implemented May 2026.

## Context

Epic 78 documented page archetypes and the summary/detail/advanced layering model. Epic 79 introduced reusable Management UI primitives (`ManagementSection`, `StatusPill`, `ManagementSummaryCard`, etc.) and proved them on `/config`.

Integration Detail (`/integrations/{IntegrationId}`) was Epic 78 **pilot #3**: the canonical deployment unit (ADR-0028), a large entity hub with readiness, bindings, schedules, procedures, and wizard entry — but presented as a flat stack of equal-weight panels (~1,400 lines).

## Why this pilot first (among “serious” redesigns)

- High operator value without touching execution hot paths (Run Detail) or multi-source consoles (Control Center).
- Already used `PageHeader` / `PageMessage`; good fit for Epic 79 adoption.
- Exercises summary + detail + advanced layering on a real entity with permission gates and API truthfulness requirements.
- Smaller blast radius than Control Center while still representative for later pilots.

## Before / after layering

| Layer | Before (Epic 77 baseline) | After (Epic 80) |
|-------|---------------------------|-----------------|
| **Summary** | Title “Integration”; details grid duplicated identity/state | `PageHeader` with Eyebrow + display name; `IntegrationSummaryBand` with pills and quick-link cards |
| **Detail** | Equal-weight bordered panels for readiness, procedures, slots, settings, bindings, schedules | `ManagementSection` groups: Readiness, Procedures, Configuration (slots + settings + bindings), Schedules |
| **Advanced** | “Runtime and lineage” panel always visible; identity grid above fold | `ManagementAdvancedSection` (collapsed): identity metadata, diagnostic links, lineage copy |
| **Help** | Per-section muted one-liners | `ManagementSection.Help` / `PageMessage` for warnings |

## Epic 79 components used

| Component | Usage |
|-----------|--------|
| `PageHeader` + `Eyebrow` | Integrations domain context; title = display name |
| `IntegrationSummaryBand` | Summary snapshot + Execute / Notifications / Schedule diagnostics cards |
| `ManagementSection` | Readiness, Procedures, Configuration, Schedules, wizard wrapper |
| `ManagementAdvancedSection` | Identity, diagnostics links, runtime lineage |
| `ManagementSummaryCard` | Quick navigation (in summary band) |
| `StatusPill` | Lifecycle state, runnable, slot bound/unbound |
| `AttentionPill` | Readiness issue severity, blocking/advisory counts |
| `DrillInLink` | Connections, schedules, diagnostics |
| `LoadingState` / `ForbiddenState` / `ErrorState` / `EmptyState` | Page and section states |

## Behavior preservation rules

- **No API, route, or permission changes** — same `IManagementClient` calls, capability checks, and query params (`?wizard=1`, `?step=`).
- **Actions unchanged** — activate/deactivate, delete, settings edit, binding CRUD, in-flow connection create, schedule create, procedure launch, wizard navigation, readiness “Fix now” remediation.
- **Truthfulness** — readiness issues remain visible in detail layer (not hidden in advanced); empty vs forbidden vs error unchanged in meaning.
- **Anchors** — `#schedules` id preserved for deep links / wizard scroll targets.

## Known deferred work

- Extract additional child components (bindings editor, procedures panel) if Run Detail or other pilots need reuse.
- Summary band: last run / next schedule when APIs expose stable fields without N+1 queries.
- Toolbar action buttons → shared secondary button component (optional Epic 79 follow-up).
- Other Epic 78 pilots: Control Center (#1), Run Detail (#2), notification definition detail (#4), schedules list/detail (#5).

## Implications for later pilots

- Use **summary band + grouped ManagementSection** pattern for entity hubs.
- Keep **safety-critical** readiness/attention in detail layer; collapse **identity + lineage + diagnostic links** into advanced.
- Prefer `DrillInLink` + existing URL helpers over new link components.
- Large `@code` blocks may remain in the page until a second extraction pass; markup layering delivers most scanability gains.

## Validation

- `Apperture.Management.Ui.Client` builds successfully.
- `ManagementUiSemanticsTests` includes `MapIntegrationState` coverage.
- Manual check: `/integrations/{id}`, `?wizard=1`, toolbar actions, binding/schedule flows unchanged.

## References

- [Epic 78 — Management UI modernization groundwork](Epic78-Management-UI-Modernization-Groundwork.md)
- [Management UI design system](../01-architecture/management-ui-design-system.md)
- [Epic 79 — Design system foundation](Epic79-Management-UI-Design-System-Foundation.md)
