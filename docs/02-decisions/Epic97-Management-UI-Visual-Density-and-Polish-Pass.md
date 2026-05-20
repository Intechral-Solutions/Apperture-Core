# Epic 97 — Management UI Visual Density and Polish Pass

## Status

Accepted — May 2026.

## Purpose

Epics 78–96 established structure, primitives, interaction patterns, recent activity, and ActionFeedback on Integration Detail. The UI was functionally layered but still felt **flat and cramped**.

This epic is the **first deliberate visual refinement pass**: spacing rhythm, hierarchy, panel presence, table scanability, and secondary/advanced treatment — **without** changing behavior, routes, handlers, APIs, or state semantics.

## Visual goals

| Goal | Approach |
|------|----------|
| **Spacing rhythm** | Increase `ui-page-stack` gap; section padding 1rem+; header bottom border |
| **Layer distinction** | Summary sections slightly emphasized (`data-ui-layer="Summary"`); advanced sections dashed/muted |
| **Readability** | Metadata items in subtle cells; table header row styling; pill rounding |
| **Action affordance** | Toolbar buttons with border + light shadow; section action spacing |
| **Feedback visibility** | `ui-feedback-banner` / `ui-state-panel` padding on PageMessage shells |
| **Operational density** | Improve scanability without removing information |

## Shared primitives refined

### `management-ui-primitives.css` (primary)

- **Page:** `ui-page-header` (+ eyebrow/title/subtitle), increased `ui-page-stack` gap
- **Sections:** `ui-section-panel` padding, shadow, summary-layer border tint, `__header` / `__body` separator
- **Summary:** card shadow, hover lift, grid gap, title sizing
- **Advanced:** dashed border, muted summary, open-state title emphasis
- **Toolbar:** border, font-weight, danger variant border
- **Metadata:** `ui-metadata-item` cells, uppercase labels, toned monospace values
- **Tables:** `ui-data-table` + `__table`, thead uppercase, row hover, cell padding
- **Feedback:** `ui-feedback-banner`, `ui-state-panel`
- **Detail blocks:** `ui-detail-block` for simple titled panels (Module Detail)
- **Pills:** pill shape (rounded-full), slightly larger padding

### Components (minimal markup hooks)

| Component | Change |
|-----------|--------|
| `PageHeader.razor` | `ui-page-header*` classes |
| `ManagementSection.razor` | `ui-section-panel__header/body/title/help` |
| `MetadataItem.razor` | `ui-metadata-item` wrapper |
| `PageMessage.razor` | optional `Class` parameter |
| `ActionFeedback` | `ui-feedback-banner` |
| State wrappers | `ui-state-panel` |
| `EntityRecentActivityPanel` | `ui-data-table` |

## Pages reviewed / touched

| Page | Adjustment |
|------|------------|
| Integration Detail | CSS-only (uses stack, sections, summary band, activity panel) |
| Run Detail | CSS-only |
| Control Center | `ui-data-table` on activity table |
| Schedules list/detail/create | List: `ui-data-table`; detail/create CSS via stack |
| Notification definition detail | `ui-data-table` on subscription + recent instance tables |
| Notification definition edit/create | CSS-only (stack) |
| Module Detail | `ui-page-stack`, `ui-page-header`, `ui-detail-block`; removed duplicate section border class |
| Blocked Work | `ui-page-stack`, `ui-data-table` |

Non-pilot pages inherit CSS where they already use primitives (e.g. `ui-toolbar-btn`).

## Behavior preservation

- No route, handler, permission, API, validation, query, toast, or ActionFeedback persistence changes.
- Table row markup simplified only where `ui-data-table` CSS replaces redundant utility classes (hover/border).

## Deferred visual work

- Full shell / nav / sidebar redesign
- Chart/stat widgets and dashboard composition
- Mobile-specific layout pass
- Broad polish on non-pilot pages
- Product copy refinement
- Removing toast + ActionFeedback duplication on Integration Detail
- Custom illustration/iconography

## Corrective patch (toolbar affordance)

Post-release QA found Run Detail toolbar actions looked like plain text and some clicks failed. See **[Epic 97 toolbar affordance corrective patch](Epic97-Toolbar-Affordance-Corrective-Patch.md)** — stronger `button.ui-toolbar-btn` / `a.ui-toolbar-btn` selectors, restored header margin, `type="button"` on pilot cooperative toolbar commands. No handler or permission changes.

## Validation

- `dotnet build` `Apperture.Management.Ui.Client`
- `Epic93`–`Epic97` shared UI tests
- Manual: light + dark theme on Integration Detail, Control Center, Schedules, Module Detail, Blocked Work

### Manual QA recommendations

1. **Integration Detail** — section rhythm, summary band, recent activity table, ActionFeedback banner spacing.
2. **Run Detail** — summary vs detail vs advanced collapse styling.
3. **Control Center** — summary cards + activity table header row.
4. **Schedules** — list table readability; detail metadata grid cells.
5. **Notification definition detail** — embedded tables in authoring section.
6. **Module Detail** — page header rule, detail blocks, no double borders on recent activity.
7. **Blocked Work** — table scanability.
8. Toggle **dark mode** — cards, tables, metadata cells, advanced dashed borders.

## References

- [Epic 85 — Pilot consolidation](Epic85-Management-UI-Pilot-Consolidation.md)
- [Epic 93 — Interaction primitives](Epic93-Management-UI-Interaction-Primitive-Hardening.md)
- [Management UI design system](../01-architecture/management-ui-design-system.md)
