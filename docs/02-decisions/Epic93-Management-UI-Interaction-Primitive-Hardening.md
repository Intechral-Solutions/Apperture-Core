# Epic 93 — Management UI Interaction Primitive Hardening

## Status

Accepted — implemented May 2026.

## Purpose

After Epics **78–92** (pilots, state branches, `ActionFeedback`, QA stabilization), harden **repeated interaction conventions** — toolbar buttons, section action rows, metadata grids, destructive placement — before visual polish or operation history.

This is **not** a redesign, not a visual density pass, and not a new framework.

Canonical context: [Epic 85](Epic85-Management-UI-Pilot-Consolidation.md), [Epic 87](Epic87-Management-UI-Action-Surface-and-Operation-Feedback-Groundwork.md), [Epic 92](Epic92-UI-Modernization-QA-and-Corrective-Stabilization.md), [management UI design system](../01-architecture/management-ui-design-system.md).

## Patterns reviewed

| Pattern | Finding | Action |
|---------|---------|--------|
| **PageHeader toolbar** | Repeated `rounded bg-surface-muted px-3 py-2…` on pilots | `ui-toolbar-btn` + `PageHeader` → `ui-page-header__toolbar` |
| **Destructive toolbar** | Integration Delete uses distinct destructive styling | `ui-toolbar-btn--danger`; documented last-in-toolbar placement |
| **Section actions** | Schedule edit Save/Cancel, Module lifecycle buttons | `SectionActions` / `ui-section-actions` |
| **Secondary action rows** | Blocked Work filter+refresh; Schedule/Module drill-ins | `ui-action-row` |
| **Metadata key-value** | Same label/value grid on Run summary, Schedule detail, Module state | `MetadataGrid` + `MetadataItem` |
| **Action feedback placement** | Epic 90 — below header, above content | No change |
| **Integration Activate CTA** | Primary `bg-body` emphasis | **Not** normalized — documented |

## Primitives added/refined

### CSS (`management-ui-primitives.css`)

| Utility | Role |
|---------|------|
| `ui-page-header__toolbar` | Toolbar flex layout (used by `PageHeader`) |
| `ui-toolbar-btn` | Neutral toolbar link/button |
| `ui-toolbar-btn--danger` | Destructive toolbar control |
| `ui-action-row` | Wrapped drill-ins, filters, header secondary controls |
| `ui-section-actions` | Section-local button groups |
| `ui-metadata-grid` / `ui-metadata-grid--3` | Two- and three-column responsive metadata |

### Components (`Molecules/`)

| Component | Justification |
|-----------|---------------|
| `MetadataGrid` | Repeated on Run summary band, Schedule detail, Module state (≥3 surfaces) |
| `MetadataItem` | Label + value + optional monospace |
| `SectionActions` | Repeated section footers (Schedule edit, Module lifecycle) |

### Organism tweak

| Component | Change |
|-----------|--------|
| `PageHeader` | Toolbar wrapper uses `ui-page-header__toolbar` |

## Pages lightly normalized

| Page | Changes |
|------|---------|
| `RunDetail.razor` | `ui-toolbar-btn` on cooperative controls + Refresh |
| `RunSummaryBand.razor` | `MetadataGrid` / `MetadataItem` for timing fields |
| `ScheduleDetail.razor` | Toolbar `ui-toolbar-btn`; read-only config → `MetadataGrid`; `SectionActions` for edit; `ui-action-row` links; advanced ids → `MetadataGrid` |
| `SchedulesList.razor` | Toolbar `ui-toolbar-btn` |
| `ScheduleCreate.razor` | Toolbar `ui-toolbar-btn` |
| `IntegrationDetail.razor` | Toolbar `ui-toolbar-btn`; Delete → `ui-toolbar-btn--danger` |
| `ControlCenter.razor` | Refresh `ui-toolbar-btn` |
| `BlockedWork.razor` | Header `ui-action-row`; toolbar `ui-toolbar-btn` |
| `ModuleDetail.razor` | Toolbar `ui-toolbar-btn`; lifecycle `SectionActions`; state `MetadataGrid`; links `ui-drill-in-link` |
| `NotificationDefinitionDetail.razor` | Toolbar back/edit links → `ui-toolbar-btn` |

**Not normalized in this epic:** `NotificationDefinitionEdit/Create`, `IntegrationNotifications`, `NotificationDiagnostics`, `NotificationInstanceDetail` (limited toolbar repetition or different layout patterns).

## What was intentionally not abstracted

- **Integration Detail Activate** — keeps `bg-body` primary emphasis.
- **Integration summary band** — horizontal flex metrics, not `MetadataGrid`.
- **Notification definition `<dl>` metadata** — compact definition grid; different density.
- **Run Detail investigation sections** — many conditional fields; remain local markup.
- **Table row actions** — per-row Retry, subscription toggles.
- **Toast-only hubs** — Integration Detail mutations.
- **ActionButton** — connection test pattern unchanged.
- **Global operation history drawer** — Epic 91 deferred.

## Behavior preservation

- Action handlers, confirmations, permission gates, API calls, routes, and navigation unchanged.
- Button disabled/title/eligibility logic unchanged.
- Cooperative `ActionFeedback` semantics unchanged.
- Destructive `ConfirmDeleteAsync` unchanged.

## Validation

Recorded at implementation time:

- `dotnet build` on `Apperture.Management.Ui.Client`
- `ManagementUiSemanticsTests` — 23 passed
- `Epic93UiInteractionPrimitiveTests` — 4 passed (new static guards)

## Recommended next epic

**Entity-local recent activity panel** (audit/timeline-backed, per Epic 91) or **visual/density pass** on pilots — only after manual QA confirms toolbar/metadata consistency in the browser.

## Deferred work

- Visual polish / density / typography pass
- Operation history implementation
- Toast normalization on Integration Detail
- Extend `ui-toolbar-btn` to all non-pilot management pages
- Run Detail poll + `ActionFeedback` coexistence (DEF-STAB-001 from Epic 92)
