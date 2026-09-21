# Toast-A — Toast UX foundation

**Status:** implemented and validated; **operator smoke in the real app still to run** (checklist below).
Notification → Toast integration is a **separate, later** roadmap body and is **not started**.

Scope: redesign and harden the *presentation* of the existing in-app Toast system. No persistent
platform Notification is connected to Toasts by this body.

---

## Task 0 — implementation audit (pre-change state, master `e6390c0`)

### A. Centralization

Toast presentation was already centralized behind one service and one host:

```text
caller ──Show(severity, message, title?)──▶ IToastService (singleton ToastService)
                                                │  Changed event
                                                ▼
                                       ToastHost (mounted in MainLayout and AuthLayout)
```

- ~95 `Toasts.Show(...)` calls across 23 files (pages, `AvatarPicker`, `ResourceContextViewNotices`).
- **No caller** passes `duration`, calls `Dismiss`, reads `Toasts`, or subscribes to `Changed`. The only
  surface callers touch is `Show(severity, message, title?)`.
- No caller knows anything about screen position, layout, animation or timers.
- Conclusion: the redesign stays inside the Toast subsystem (`ToastService`, `ToastItem`, `ToastPolicy`,
  `ToastHost`, one CSS block, one small JS file). **Call sites do not change.**

### B. Queue / timer ownership (defects found)

| Question | Finding |
| --- | --- |
| Where are ids assigned? | `ToastService.Show` (`Guid.NewGuid()`); a duplicate signature re-uses the existing id. |
| Where do durations live? | `ToastPolicy.ResolveDuration` (default 6 s, `Error` = persistent, explicit duration wins). |
| Timer? | One fire-and-forget `Task.Delay` per `Show`, started **at enqueue** (not render). Not cancellable. |
| Manual dismiss vs timer? | `Dismiss` is `RemoveAll`; the stale timer later finds no item and no-ops. Safe, but `Changed` is raised even when nothing was removed. |
| Re-arm | Duplicate signature bumps `ExpiresAt`; stale timers are recognised by comparing `ExpiresAt` against `now + 250 ms` (heuristic). |
| Pause / resume | **None.** A wall-clock expiry cannot represent "remaining time". |
| Survives in-shell navigation? | Yes — singleton service; the host re-snapshots on `Changed`. A layout swap (AuthLayout ↔ MainLayout) remounts the host, which re-renders the list. |
| Hard bound | **None.** Every non-duplicate `Show` appends. |
| Keyed rendering | Host renders with no `@key`, so removing an earlier toast would patch later DOM nodes in place — unsafe once per-element animation state exists. |

### C. Severity semantics (kept)

`Success`, `Info`, `Warning`, `Error` (`ToastSeverity`). No new taxonomy is needed.

- Default dwell: 6 s for non-errors; **errors persist until dismissed** (unless an explicit duration is
  passed). This is preserved unchanged.
- ARIA: errors were in an `aria-live="assertive"` / `role="alert"` region; everything else in a polite region.
- Iconography: **none** — severity was a 4 px coloured left border only (colour was the only cue).
- Dismiss control: a `×` glyph with `aria-label="Dismiss"`.

### D. Existing tests

- `Uix6InteractionSystemTests` — pure unit tests of `ToastPolicy` (duration, aria-live, signature) plus
  **source-text guards** on `ToastHost.razor`.
- **No** behavioural tests of `ToastService` (queue, timers), no component tests, no browser tests.
- Lifecycle logic lives in `ToastService` (Client project) with no clock abstraction, so it was untestable
  without real sleeps.

---

## Design decisions

### Architecture (preserved, then evolved)

```text
caller ──Show(severity, message, title?)──▶ IToastService / ToastService   (lifecycle owner, TimeProvider-driven)
                                                │ Changed                     ▲ SetHold / Dismiss(id, exit)
                                                ▼                             │ (JSInvokable)
                                          ToastHost (render, keyed, announce) ◀── js/toast-host.js (gestures, holds)
```

The caller → service → queue → host → items shape already existed and was kept. What changed:

- **`ToastService`** now owns *all* lifecycle: per-toast remaining-time countdown, stack-wide holds, the
  `Visible → Exiting → removed` phases, generation-stamped timers, and the hard bound. It takes an optional
  `TimeProvider`, so lifecycle is testable with a manual clock (no sleeps). `ToastItem` gained `Duration`,
  `Phase`, `Exit`, `CreatedAt` (and lost the wall-clock `ExpiresAt`, which cannot express "remaining").
- **`IToastService`** gained `Dismiss(id, exit)` and `SetHold(source, active)` (additive; callers only use `Show`).
- **`ToastHost`** renders one keyed stack and feeds announcements; it holds no timers except the
  announcement-clear delay.
- **`js/toast-host.js`** is the only place pointer/touch/scroll/focus behaviour lives. It owns no toast state
  and writes only CSS custom properties and `data-*` attributes Blazor never renders, so re-renders cannot
  fight a gesture.
- **`ToastPolicy`** (shared) gained `ExitDuration`, `MaxToasts`, `AnnouncementLifetime`,
  `ResolveSeverityLabel`, `BuildAnnouncement`. Existing durations and `ResolveAriaLive` are **unchanged**.

### Placement / responsive

- `position: fixed` at the bottom edge; safe-area bottom inset respected; click-through except the toasts.
- Width `clamp(28rem, 50vw, 42rem)` (672 px max); at ≤ 40rem full width with 0.5 rem margins (left/right
  safe-area aware). The scroll container is deliberately **viewport-wide** so a swiped placard is never
  clipped by an invisible wall; cards are centred inside it.
- **Shell inspection:** the shell has no bottom bar, but the nav rail is always shown and pins a Config button
  at its bottom-left. A full-width phone toast would cover it, so `<ToastHost ClearNavRail="true" />` (main shell
  only) starts the stack beside the rail at ≤ 40rem. The sign-in layout has no rail. On desktop the centred stack
  is clear of the rail; with the control panel open it can briefly overlap the panel's lower area, but the panel
  has no bottom-pinned controls.

### Stack / overflow / bound

- Chronological, newest at the bottom (`margin-top: auto` on the first row — safe when overflowing).
- Cap `min(36vh, 24rem)` (`dvh` where supported). Overflow scrolls by wheel and touch. The **scrollbar is hidden**
  (a scrollbar at the viewport edge, beside the page's own, would be detached and confusing); a top/bottom fade
  signals clipped toasts, and new toasts scroll into view unless the operator is mid-interaction higher up.
- `overscroll-behavior-y: contain`: the stack has no scrollable ancestor to chain to (the page scrolls in its own
  container), so chaining could only reach the viewport, where it causes pull-to-refresh/rubber-banding on touch.
- **Bound:** `ToastPolicy.MaxToasts` = 100 (generous; viewport clipping is a separate concern). Eviction order:
  leaving toasts, then the oldest auto-dismissing toast, then the oldest persistent one. Identical outcomes already
  collapse via the signature, so only a runaway of *distinct* toasts reaches the bound.

### Timing

- Durations unchanged: 6 s default; errors persist; an explicit duration wins.
- **The countdown starts at enqueue** (as before). The 220 ms enter animation is ~3.5% of the default, so the
  operator still gets the intended readable dwell. Chosen over "start after the animation" because it adds no
  animation/timer coupling.
- **Holds are stack-wide** (pointer over the stack or a touch on it, focus inside, swipe in progress, scrolling incl.
  momentum, tab hidden): while the operator interacts with the stack, nothing in it expires or shifts under them.
  Resume continues the *remaining* time. A toast enqueued during a hold keeps its full duration until release.
  Holds that describe the stack (pointer/focus/drag/scroll) are dropped when the stack empties; JS recomputes
  hover/focus from the DOM on every change, so a missed pointer-leave or blur cannot freeze later toasts.
- **Lifecycle:** dismiss request → `Exiting` (exit animation) → removal after `ToastPolicy.ExitDuration` (a test ties
  it to `--ui-toast-exit-duration`). Removal is timer-driven and never waits on animation events; the dwell timer is
  cancelled on dismiss; a stale callback is discarded by its generation; removal happens exactly once; a hold does
  not stall a leaving toast; an identical outcome does not revive a leaving toast.

### Swipe

Pointer Events with pointer capture. Intent is decided after 8 px: horizontal must beat vertical by 1.2× to become a
swipe, otherwise it is left to native vertical scrolling (`touch-action: pan-y`). The card follows the pointer
(`--toast-dx`); opacity is unchanged in a 12% dead zone then falls linearly to 0 at 1.25× the threshold (always in
[0, 1]); the dismissal threshold is 30% of the toast width bounded to 72–220 px; both directions; release before the
threshold snaps back (CSS transition), release past it dismisses and the exit continues that direction.
A `dragstart` guard stops native drag-and-drop of selected text from cancelling a swipe. A horizontal mouse drag is a
swipe, so it no longer selects text (double/triple-click and keyboard selection still work).
Not implemented (deliberately): velocity/flick dismissal (the brief specifies a threshold and snap-back).

### Accessibility

- Toasts are not live regions. Each new toast is announced **once** via persistent polite (`role="status"`) /
  assertive (`role="alert"`, errors only, per the existing `ToastPolicy.ResolveAriaLive`) regions as
  `Severity: Title. Message`, cleared after 5 s. Re-renders, reordering, re-arming and removal are silent; a host
  remount after a layout swap does not re-announce toasts older than the window.
- The host never moves focus. Close button: `aria-label="Dismiss notification"`, native `<button>`, visible
  2 px accent focus outline, 44 px target on coarse pointers. Dismissing a focused toast moves focus to a
  neighbouring close button, or back to where it was before entering the stack.
- Severity is carried by a distinct icon shape and a screen-reader severity word as well as the accent colour.
- Contrast measured in headless Chrome against the production CSS in both themes for all four severities:
  body/title text ≥ 9:1, close icon ≥ 4.7:1, severity icon ≥ 4.2:1 (thresholds 4.5 / 3 / 3).
- Reduced motion: no enter/exit/collapse or snap-back animation; a leaving toast disappears at once; drag still
  follows the pointer; everything keeps working.

### Styling

Existing tokens only (`--color-surface-elevated` at 94%, `--color-border`, semantic `--color-success/warning/error`,
`--color-toast-info-accent`, `--r-md`, `--interaction-*`). `backdrop-filter` is reused (the top bar already uses it)
and readability does not depend on it. The old `toast-strip-*` utilities are no longer used by the host (left defined).

### Caller compatibility

No call site changed. ~95 `Toasts.Show(...)` calls (23 files) compile and behave as before; a guard test fails if
feature code starts touching `SetHold`/`ToastHold`. Layout files: `MainLayout` passes `ClearNavRail="true"` to the host.

---

## Verification

**Automated (exact totals are in the completion report):** `ToastServiceTests` (manual-clock lifecycle: order, dwell,
pause/resume with remaining time, overlapping holds, hold-at-enqueue, exit phase, exactly-once removal, idempotent
dismiss, swipe direction, bound + eviction order, disposal); `ToastFoundationTests` (policy, markup, CSS, script
wiring incl. exit-duration parity and nav-rail clearance); node tests for the pure gesture math
(`node --test tests/Apperture.Management.Ui.Client.Tests/js/toast-gesture.test.mjs`, not part of the dotnet gates).

**Real-Chrome harness (not the real application):** a standalone page loading the *production* `app.css`,
`management-ui-primitives.css` and `toast-host.js` with a stub in place of the .NET side, driven through the Chrome
DevTools Protocol: placement at 1024/1440/1920 and 390 wide, no horizontal overflow, ordering, height cap, wheel and
touch scrolling, mouse and touch swipe (both directions, threshold, snap-back, opacity, vertical/diagonal intent),
holds (hover, focus, drag, scroll, stuck-hold recovery), focus continuity, exit animation, reduced motion, contrast in
both themes, nav-rail clearance, and detach. 81/81 checks passed. It found one real defect (below). It does **not**
exercise Blazor rendering, `ToastService` in the browser, or JS interop.

### Operator smoke checklist (still to run in the real app)

Trigger toasts from a real page (e.g. save something on Account → Preferences; force an error on Connections).

1. **Placement:** one toast appears bottom-centre; sensible width on desktop; sensible cap on a very wide window;
   nothing important obscured. Phone width: near full width, no horizontal scroll, rail's Config button reachable.
2. **Stack:** several quick toasts stack upward with the newest lowest; remaining toasts flow when one leaves; exceed
   the height cap → capped, wheel-scrollable, edge fade shown; page still usable.
3. **Timing:** 6 s dwell; hover pauses and leaving resumes the *remaining* time; focusing the close button pauses;
   an error persists until dismissed.
4. **Dismiss:** close button (smooth exit, focus stays sane); short drag snaps back with opacity following; past the
   threshold dismisses right and left; on touch, vertical scroll never dismisses; diagonal drags behave.
5. **Motion:** enter rises/fades in; auto-exit fades up; reduced-motion emulation removes movement but keeps function.
6. **Severity/contrast:** success, info, warning, error distinguishable without colour; check both themes.
7. **A11y:** tab reaches close buttons with a visible ring; a new toast does not steal focus; accessibility tree shows a
   polite status region and an alert region only for errors.
8. **Lifecycle:** navigate in the app with a toast up (no duplicate/reset); change theme with several toasts up.

---

## Findings and deviations

| Finding | Resolution |
| --- | --- |
| Nav rail pins a bottom-left control that a full-width phone toast would cover. | `ClearNavRail` on the main-shell host; guard test ties the CSS token to the rail width. |
| Real defect found by the browser harness: pressing on already-selected toast text started a native drag-and-drop that cancelled the swipe. | `dragstart` is prevented inside the stack. |
| Old design split toasts into an assertive and a polite *container*, which cannot be interleaved chronologically. | Replaced by per-toast one-shot announcements through persistent regions; toasts themselves are not live. |
| Old service compared wall-clock expiry with a 250 ms fudge to recognise stale timers and raised `Changed` on no-op dismisses. | Generation-stamped timers; no-op dismisses are silent. |
| Old host rendered unkeyed items, unsafe once elements carry animation state. | Keyed by toast id. |
| Wheel/touch over the stack does not scroll the page beneath it. | Inherent to any overlay (as before); the stack has no scrollable ancestor to chain to. |
| A horizontal mouse drag no longer selects toast text. | Accepted trade-off for mouse swipe; other selection routes remain. |
| Duplicate outcomes are not re-announced to screen readers (they re-arm silently). | Existing UIX-6 intent, now explicit. |

## Notification → Toast readiness

Nothing blocks starting it. The presentation API it can consume is `Show(severity, message, title?, duration?)`,
`Dismiss`, and the severity/duration policy. Things that body will likely want to decide (not built here): a
per-toast dedupe key richer than the message signature, an optional action/link on a toast (toasts currently have no
click target), and where a Notification's severity maps onto `ToastSeverity`.
