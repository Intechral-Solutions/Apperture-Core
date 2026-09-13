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

**Primary (browser QA path):** Dev and CI browser sessions use **`Apperture.Management.Ui.Server`** (`http://localhost:9001`, `scripts/dev/up.ps1`), whose shell is `Pages/_Layout.cshtml`. That layout linked **`css/app.css` only** — not `css/management-ui-primitives.css`. Epic 79+ toolbar/section primitives live in the latter file; `wwwroot/index.html` in the WASM client had the link, but the **Server host document never did**, so toolbar controls rendered as unstyled Tailwind-preflight text (transparent `button` background, inherited color).

**Secondary (markup hardening, still valid):**

1. Explicit `button.ui-toolbar-btn` / `a.ui-toolbar-btn` selectors in `management-ui-primitives.css`.
2. `type="button"` on cooperative toolbar commands so implicit submit cannot interfere near edit surfaces.
3. Page header spacing/toolbar stacking tweaks.

Cooperative actions that are **disabled** (`CanInvoke` false or `_loading`) do not fire `@onclick` by design; without primitives CSS they can look like plain labels rather than disabled buttons.

No handler, permission, route, API, polling, or ActionFeedback logic was changed.

## Fix

| Area | Change |
|------|--------|
| **`Apperture.Management.Ui.Server/Pages/_Layout.cshtml`** | Link `css/management-ui-primitives.css` immediately after `css/app.css` (canonical dev host shell) |
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
