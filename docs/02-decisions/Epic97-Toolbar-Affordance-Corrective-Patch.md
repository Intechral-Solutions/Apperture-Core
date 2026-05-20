# Epic 97 — Toolbar affordance corrective patch

## Status

Accepted — May 2026 (corrective follow-up to Epic 97).

## Problem

Post–Epic 97 QA on Run Detail and other pilot pages reported a **blocking regression**:

- PageHeader toolbar actions (Park, Resume, Refresh, Cancel, Retry, etc.) appeared as plain text.
- Some controls did not respond to clicks.
- Toolbar affordance (border, background, hover/focus) was weaker than intended.

Epic 97 changed `PageHeader` markup/classes and refined `ui-toolbar-btn` / `ui-page-header` CSS without intending to change handlers, permissions, or action semantics.

## Root cause

1. **Tailwind preflight** (`app.css` `@layer base`) resets `button` to `background-color: transparent` and `color: inherit`. Epic 97 styled `.ui-toolbar-btn` as a single class selector; using explicit `button.ui-toolbar-btn` / `a.ui-toolbar-btn` restores predictable control chrome above preflight.
2. **Page header spacing** — Replacing `mb-4` on the header wrapper with `ui-page-header { margin-bottom: 0.125rem }` collapsed vertical rhythm and made the toolbar row easier to misread against the new bottom border.
3. **Missing `type="button"`** on several pilot toolbar commands — Without an explicit type, buttons inside (or adjacent to) edit surfaces can behave as implicit submit controls; Run Detail and Schedule Detail toolbars were updated to use `type="button"` for cooperative actions.

No handler, permission, route, API, polling, or ActionFeedback logic was changed.

## Fix

| Area | Change |
|------|--------|
| `management-ui-primitives.css` | `button.ui-toolbar-btn` / `a.ui-toolbar-btn` with appearance reset, `pointer-events: auto`, focus-visible ring, disabled state keeps muted fill; toolbar `flex-shrink: 0` + `z-index: 1`; `ui-page-header` margin-bottom restored to `1rem` |
| `PageHeader.razor` | `shrink-0` on toolbar slot |
| Pilot pages | `type="button"` on toolbar/lifecycle `ui-toolbar-btn` commands (Run, Schedule, Module, Blocked Work, Control Center, Schedules list; Schedule edit Save/Cancel) |

## Behavior preserved

- All existing `@onclick` handlers, `disabled`/`title` eligibility, navigation `<a href>`, and danger styling unchanged in meaning.
- Integration Detail and Notification Definition Detail links unchanged (already correct element types).

## Validation

- Build `Apperture.Management.Ui.Client`
- `Epic93UiInteractionPrimitiveTests`, `Epic97UiVisualPolishTests`, plus new toolbar markup tests
- Manual QA: Run Detail Park/Resume/Refresh/Cancel/Retry; Integration/Schedule/Module/Notification definition toolbars; light/dark theme

## Related

- [Epic 97 — Visual density and polish pass](Epic97-Management-UI-Visual-Density-and-Polish-Pass.md) — parent epic; corrective note cross-linked there.
