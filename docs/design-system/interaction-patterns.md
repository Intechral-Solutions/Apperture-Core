# Apperture interaction patterns

**Status:** Authoritative (UIX-6)
**Applies to:** Management UI (`Apperture.Management.Ui.Client`, `Apperture.Management.Ui.Shared`)
**Related:** [UIX-5 audit](../audits/uix5-supporting-surface-pattern-audit.md), [UIX-6 closure](../audits/uix6-interaction-progressive-disclosure-system.md)

This guide decides **where information and decisions live**. It is written to be applied
directly by developers and coding agents. Where a rule and a visual preference disagree,
the rule wins.

---

## 1. Principles

1. **Business information first.** Status, failure reasons, required instructions, record
   identity, conflict recognition, and primary actions stay visible inline.
2. **Progressive disclosure for secondary detail.** Identifiers, metadata, payload summaries,
   and diagnostics start collapsed.
3. **Routable work stays routable.** Filtering, paging, bookmarking, refresh, and independent
   error states belong to a page, not an overlay.
4. **Dialogs are intentional.** Blocking decisions only — never long read-only content.
5. **Local interactions stay local.** Disclosure and dialog state never enter the URL.
6. **Accessibility is part of the component contract**, not later polish.

---

## 2. Choosing a surface

| Situation | Surface | Component |
| --- | --- | --- |
| Essential record facts and actions | Inline | page markup, `ManagementSection` |
| Small/medium secondary content already loaded | Disclosure | `Disclosure` |
| Identifiers, metadata, diagnostics | Technical details | `TechnicalDetails` + `TechnicalDetail` |
| Short, nonessential explanation | Tooltip | `SettingInfoAffordance`, `title=` |
| Transient outcome | Toast | `IToastService` → `ToastHost` |
| Outcome tied to a control or section | Inline feedback | `ActionFeedback` |
| Persistent page-level state or guidance | Page message | `PageMessage` |
| Blocking consequential decision | Modal dialog | `ConfirmActionDialog` / `ManagementDialog` |
| Filtered, paged, shareable investigation | Dedicated route | `@page` |
| Inspect beside underlying context | **Deferred** | see §9 |

### Do not use

| Pattern | Never for |
| --- | --- |
| Disclosure | required operating steps; primary status; unloaded data needing its own request |
| Technical details | operator-required facts that appear nowhere else |
| Tooltip | instructions, error detail, interactive content, long text |
| Toast | errors requiring remediation as the *only* signal; content the operator must re-read |
| Modal dialog | long read-only investigation; multi-step workflows; browsing |
| Dedicated route | a two-field confirmation |
| Accordion | general page layout |

---

## 3. Inline content

Use for the primary summary, current status, key actions, conflict recognition, and any
instruction the operator must follow to complete their task.

If an operator would have to *discover* the information to succeed, it is inline.

---

## 4. Disclosure

```razor
<Disclosure Summary="Delivery attempts" Dense="true">
    ...
</Disclosure>
```

Use when the content is secondary, **already loaded**, short or medium, not a decision, and
not worth its own route.

- Semantic `<details>`/`<summary>`; keyboard operation is native.
- `InitiallyOpen` only when the content is usually needed.
- `Dense` for table rows and compact cards.
- No JavaScript. No URL state.

---

## 5. Technical details

```razor
<TechnicalDetails>
    <TechnicalDetail Label="Run ID" Value="@run.Id" />
    <TechnicalDetail Label="Correlation ID" Value="@correlationId" />
</TechnicalDetails>
```

Use for GUIDs, platform IDs, correlation IDs, raw metadata, payload summaries, and diagnostics
that are not needed for normal operation.

- Collapsed by default; summary defaults to **Technical details**.
- Override `Summary` only when a page shows more than one technical block
  (for example “Group technical details”).
- Values render monospace and select cleanly for copying; set `Monospace="false"` for prose.
- Missing values render `—` rather than an empty row.
- Use `Note` for a short explanation of what the identifiers enable.

Never place an operator-required fact only inside `TechnicalDetails`.

---

## 6. Feedback: toast, inline, page message

| Signal | Use | Behavior |
| --- | --- | --- |
| **Toast** | transient success/info the operator need not revisit | auto-dismiss after 6s (`ToastPolicy.DefaultDuration`), polite announcement |
| **Toast (error)** | failure worth surfacing globally | **persists until dismissed**, assertive announcement |
| **Inline feedback** (`ActionFeedback`) | outcome tied to a control or section; remediation is nearby | stays until the next action |
| **Page message** (`PageMessage`) | persistent page state, scope banners, guidance | remains while the state holds |

Rules:

- Repeating an identical outcome **re-arms** the existing toast instead of stacking
  another announcement (`ToastPolicy.BuildSignature`).
- An error confirmed inside a dialog belongs in the dialog, not a toast.
- Success may use both a toast and inline feedback when the page stays open and the
  section result matters.
- Vocabulary: success / info / warning / error. Do not invent additional severities.

### Toast presentation and interaction

Callers only ever use `IToastService.Show(severity, message, title?, duration?)`. Placement, animation,
gestures, pausing and announcements are the toast subsystem's job; feature pages never manage them.

| Aspect | Behavior |
| --- | --- |
| Placement | One stack, **bottom-centred**, floating above the page. Newest toast at the bottom; older toasts stack upward. Wide screens: `clamp(28rem, 50vw, 42rem)`. At ≤ 40rem: full width with small margins (safe-area aware). In the main shell on phones the stack starts beside the nav rail so the rail's bottom control stays reachable. |
| Height | Capped at `min(36vh, 24rem)` (`dvh` where supported). Overflow scrolls vertically (wheel/touch); the scrollbar is hidden and a top/bottom edge fade signals clipped toasts. New toasts scroll into view unless the operator is mid-interaction higher up the stack. |
| Motion | Enter: fade + rise from below while the row grows. Exit: fade + drift up while the row collapses; a swipe exit continues the way it was dragged. Tokens: `--ui-toast-enter-duration`, `--ui-toast-exit-duration`. |
| Dwell | Starts at enqueue (`ToastPolicy` durations are unchanged: 6 s; errors persist). The countdown tracks **remaining** time. |
| Pause | Stack-wide while the operator interacts: mouse over the stack, a touch on it, keyboard focus inside, a swipe in progress, scrolling (incl. touch momentum), or the tab hidden. Resumes with the remaining time, never a fresh full duration. A toast enqueued during a pause keeps its full duration until release. |
| Dismiss | Explicit **Dismiss notification** button (keyboard reachable, visible focus, 44 px target on coarse pointers) **or** a horizontal swipe with mouse or touch. Swiping needs no more than 30% of the toast width (bounded 72–220 px); opacity falls with distance; releasing earlier snaps back. Vertical intent is left to scrolling (`touch-action: pan-y`). Text can still be selected with double/triple-click or keyboard; a horizontal mouse drag is a swipe. |
| Lifecycle | `Visible` → (dismiss requested) `Exiting` → removed after `ToastPolicy.ExitDuration`. Removal is timer-driven, exactly once, and never waits on an animation event. |
| Bound | At most `ToastPolicy.MaxToasts` (100) in memory. Beyond that the oldest auto-dismissing toast is evicted first; persistent errors last. This is a safety net for runaway callers, not a display limit. |
| Duplicates | An identical outcome (same severity, title, message) re-arms the existing toast; it is not announced again. A toast that is already leaving is not revived. |

Accessibility: toasts are **not** live regions themselves. Each new toast is announced **once** through
a persistent polite region (errors: assertive) as `Severity: Title. Message`, cleared after a few
seconds, so re-renders, reordering and removal are silent. The host never moves focus. Severity is
carried by an icon shape and a screen-reader severity word as well as colour. When a focused toast is
dismissed, focus moves to a neighbouring toast's close button, or back to where it was before entering
the stack.

Reduced motion (`prefers-reduced-motion: reduce`): no enter/exit/collapse animation and no snap-back
animation; a leaving toast disappears at once. Dragging still follows the pointer and every control
keeps working.

Not a Notification delivery channel: incoming platform Notifications do not surface as toasts yet
(a separate roadmap body builds on this API).

---

## 7. Confirmation and consequential actions

```razor
<ConfirmActionDialog IsOpen="@_confirmDelete"
                     Title="Delete integration?"
                     ConfirmLabel="Delete integration"
                     Emphasis="ConfirmActionEmphasis.Destructive"
                     OnConfirm="DeleteAsync"
                     OnConfirmed="() => _confirmDelete = false"
                     OnCancel="() => _confirmDelete = false">
    Deleting <strong>@name</strong> permanently removes its configuration. This cannot be undone.
</ConfirmActionDialog>
```

**Confirm when** the action is destructive, hard to reverse, removes or retires platform
configuration, affects multiple records, or creates a significant operational consequence.

**Do not confirm** ordinary reversible work. Per-item retry creates a child run and leaves the
original item unchanged, so it stays an inline action.

| Emphasis | Use |
| --- | --- |
| `Standard` | reversible change that still deserves a pause |
| `Caution` | consequential but recoverable (retire, clear override) |
| `Destructive` | removes or permanently changes configuration |

Contract:

- The page owns `IsOpen`; the dialog owns busy and error state.
- `OnConfirm` **throws to fail**: the dialog stays open and shows the reason.
- `OnConfirmed` fires only after success — close there.
- The confirm button disables while running; the label gains `…`.
- Caution and destructive dialogs must state the consequence, not just the title.
- Never use `window.confirm`. Never encode dialog state in the URL.

---

## 8. Help and explanation

| Need | Pattern |
| --- | --- |
| Required operating instruction | visible inline copy / `PageMessage` / section `Help` |
| Field definition or short clarification | `SettingInfoAffordance` (has an accessible name) |
| Nonessential tip | `title=` tooltip |
| Deep background | documentation link |

Tooltips must be short, noninteractive, and reachable by keyboard focus. If losing the
tooltip would block the task, it is not a tooltip.

---

## 9. Deferred patterns

### Inspection panel / drawer — deferred

UIX-5 found no justified consumer. Do not add one until **all** hold:

1. At least two real workflows need inspect-beside-context behavior.
2. Comparison against the underlying page is genuinely required.
3. URL, Back/Forward, and refresh semantics are defined.
4. Mobile behavior is full-screen routed detail, not a shrunk overlay.

A future panel reuses the UIX-6 seam: overlay/surface tokens, focus capture and restore
helpers, Escape handling, accessible naming, responsive sizing, reduced-motion rules.

### Popover — deferred

No current consumer needs interactive contextual content with explicit open/close and focus
movement. Tooltips and disclosures cover today's cases.

### Accordion — deferred

Peer-section collapsing has no proven multi-section consumer; `Disclosure` covers current use.
Never use an accordion as page layout.

---

## 10. URL state

| Detail | State home |
| --- | --- |
| Stable record, filtered history, shareable focus | route or query |
| Ephemeral disclosure of loaded metadata | local component state |
| Confirmation dialogs | local only — never the URL |
| Deep-linked focus (affected records) | query, per UIX-3 |

---

## 11. Accessibility requirements

**Disclosure:** semantic summary, keyboard activation, visible focus, expanded state not
conveyed by color alone, content in reading order.

**Dialog:** `role="dialog"`, `aria-modal="true"`, accessible name via `aria-labelledby`,
deterministic initial focus, focus contained while open, focus restored to the opener,
Escape closes (blocked while busy), explicit close control, `aria-busy` during work,
errors announced, no reliance on animation, reduced-motion respected.

**Toast:** polite by default, assertive for errors, announced once (no announcement storms),
never steals focus, named dismiss control, critical errors persist, reduced-motion respected.
See *Toast presentation and interaction* above.

**Tooltip:** keyboard-reachable trigger, available on focus, short, noninteractive.

---

## 12. Agent checklist

Use `TechnicalDetails` when showing GUIDs, platform IDs, correlation IDs, raw metadata, or
non-operational diagnostics.

Use `Disclosure` when content is secondary, already loaded, short or medium, and not a decision.

Use `ConfirmActionDialog` when the action is destructive, hard to reverse, retires configuration,
affects many records, or has significant operational consequence.

Use a toast for transient outcomes; use inline feedback when remediation is nearby.

Do **not**: invent modal styles; call `window.confirm`; hide instructions in tooltips; put
investigation in dialogs; nest overlays; add arbitrary z-index values; mutate Blazor-owned DOM;
put dialog state in the URL; add a panel where a route already works.
