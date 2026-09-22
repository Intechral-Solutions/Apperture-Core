# Toast-A — Toast UX foundation

**Status:** implemented and automatically validated, but **not merge-ready**: the first real-app smoke failed
(see *Real-app smoke failure*), the cause is fixed and guarded, and a short operator re-smoke on a **restarted** app is
still required. Notification → Toast integration is a **separate, later** roadmap body and is **not started**.

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
- **`ToastHost`** renders one keyed stack; announce-once logic is the unit-tested `ToastAnnouncer`. It holds no
  timers except the announcement-clear delay.
- **`js/toast-host.js`** is the only place pointer/touch/scroll/focus behaviour lives. It owns no toast state
  and writes only CSS custom properties and `data-*` attributes Blazor never renders, so re-renders cannot
  fight a gesture.
- **`ToastPolicy`** (shared) gained `ExitDuration`, `MaxToasts`, `AnnouncementLifetime`,
  `ResolveSeverityLabel`, `BuildAnnouncement`, `MaxDuration` (24 h clamp on explicit durations). Existing default durations
  and `ResolveAriaLive` are **unchanged**.

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

## Visual polish pass (second real-app re-smoke)

After the interaction fixes, the operator's re-smoke was functionally successful (position, close, drag/snap-back,
swipe all worked) but flagged two visual issues:

1. **Shadow too intense/broad — a "luminous halo" in dark theme.** `--ui-toast-shadow` was a bespoke shadow tinted
   with `color-mix(in oklch, var(--color-text-heading) 45%, transparent)`. `--color-text-heading` is a *light* colour
   in dark theme (it is the heading-text colour, made light-on-dark for contrast), so a shadow built from it at 45%
   opacity is a light/white glow around every card, not a shadow. **Fix:** the card now uses `var(--shadow-elevated)`
   — the same token `.ui-dialog` (the modal) uses — instead of a toast-only shadow. It is already tuned per theme: a
   restrained dark shadow in light theme, a tighter black shadow plus a 4%-white inset top highlight in dark theme
   (no light-coloured glow). The bespoke `--ui-toast-shadow` token was removed.
2. **The bottom toast's shadow was visibly clipped flat.** Root cause: `.ui-toast-slot__inner` (the wrapper each
   card sits in, needed so a slot can animate its grid row to `0fr` on exit) has `overflow-y: clip`, and in the
   steady state its box is sized exactly to the card's own border box — so the downward box-shadow is clipped flush
   against the card's own edge for *every* toast. For a middle toast this is invisible (the very next card sits
   right there, ~0.5rem below); for the bottom-most toast there is nothing below it, so the flat cut is obvious.
   **Fix:** `.ui-toast-slot:last-child .ui-toast-slot__inner` (the newest, bottom-most toast — DOM order is oldest
   → newest, confirmed by the existing ordering test) gets `padding-bottom: var(--ui-toast-shadow-room)` (1.25rem),
   scoped to that one slot only so the gap between *other* stacked cards is unaffected. `--ui-toast-bottom` (the
   outer margin from the layer to the viewport edge) was reduced from 1rem to 0.5rem to compensate, since that
   outer margin was never actually reachable by the shadow (it was clipped before ever getting there) — the fix
   moves the "breathing room" to where the shadow can use it instead of adding it on top.

Both fixes are pure CSS/layout: no change to `ToastService`, `toast-host.js`'s interop, ARIA/announcement
architecture, reduced-motion logic, responsive width rules, nav-rail clearance, or overflow/scroll semantics.

**New regression coverage** (`ToastFoundationTests`, structural; harness, geometric — no pixel/screenshot
comparison, per instruction): the card's `box-shadow` is `var(--shadow-elevated)`, matches `.ui-dialog`'s own
shadow, and carries no bespoke `color-mix` shadow; `--ui-toast-shadow-room` exists and is applied only to
`.ui-toast-slot:last-child .ui-toast-slot__inner`, never the base rule (so other gaps are provably unaffected); in
the harness, the newest toast's inner wrapper has ≥15px more room below its card than an earlier toast's (which has
only its normal ~8px inter-card margin), and that room is not itself clipped away by the scroll container's own
bottom edge. The full harness (including every prior check — position, order, overflow scroll, hit-testing, swipe,
exiting-toast click-through, contrast in both themes) was rerun and confirmed unaffected: **64/64**.

---

## Real-app smoke failure (first operator smoke) — root cause

**Reported:** toasts against the very left edge of the viewport; no mouse response; swipe dead; the X dead.

**Root cause: a hybrid of old compiled code and new live-served assets — not a defect in the branch's own markup/CSS/JS.**
Evidence gathered read-only against the operator's running stack (nothing on it was touched or restarted):

| Evidence | Finding |
| --- | --- |
| Served `js/toast-host.js` and `css/management-ui-primitives.css` vs the branch | Byte-identical: static assets are read live from disk, so the **new** CSS/JS were being served. |
| Rendered DOM of the running app's toast host | The **old** markup: `.ui-toast-layer > .ui-toast-region(aria-live) > .ui-toast`, no `.ui-toast-stack`/slot/live regions. |
| Served HTML of the running app | No `toast-host.js` tag: `_Layout.cshtml` is compiled into the Server binary, which was old. |
| Build timestamps | The served client `.wasm` and `Ui.Server.dll` were built **14:01**; the process started 14:02; Toast-A's first commit is 15:42. |

Why every symptom follows: the new `.ui-toast-layer` is `left/right: 0; display: flex; pointer-events: none`. The old
markup's cards sit directly inside it with **no slot restoring `pointer-events: auto`** (the old `.ui-toast` rule that
did was replaced), so nothing centred them (left edge) and they inherited `pointer-events: none` — inert, X
included — and the script was not loaded at all.

**Proof the new build is correct in a real Blazor runtime:** the freshly built client, run standalone with a temporary
(uncommitted, since reverted) probe that fires real `IToastService.Show` calls, passed **29/29** end-to-end checks driven with
real mouse events: real ToastHost markup, centred (x=384, w=672 at 1440), layer click-through but slot and card
`pointer-events: auto`, card and close button hit-testable, real X click → `Exiting` → removed through Blazor and the
service, short-drag snap-back, long-drag dismissal through the real .NET `Swipe`, real hover pause (a toast held to 9 s,
then gone 3.0 s after leaving — remaining time, not a fresh 6 s), assertive/polite announcements, keyboard Enter on the
close button, burst overflow (height-capped, pinned to newest, wheel-scrollable) and 390 px width. With `toast-host.js`
**blocked**, a further **5/5**: still centred, X still dismisses through Blazor alone, auto-dismiss still works — the
close button does not depend on the gesture script.

**Why the first harness passed while the real app failed:** it built its toast DOM by hand in a `<script>`, so it always
had the *new* structure regardless of what the compiled app rendered, and it never used the app's real `_Layout`. It could
not detect markup/CSS/script disagreement, which is exactly this bug class.

**Fixes (narrow):**
1. `.ui-toast` now also sets `pointer-events: auto` (defence in depth: the click-through layer must not depend on a
   wrapper to make the card interactive).
2. The test gap: `ToastHostRenderTests` renders the **real** `ToastHost` (Blazor `HtmlRenderer`, real `ToastService`) and
   asserts the layer → stack → slot → inner → card → button hierarchy, ids/severities/order, live regions, the nav-rail class and
   no legacy markup — plus **two-way coupling**: every `.ui-toast*` class in the stylesheet must be rendered by the host, and
   every class/attribute `toast-host.js` selects must exist in the rendered markup.
3. `browser/toast-browser-harness.mjs` now consumes markup **exported from that real render** (real slots are cloned to make
   more toasts; nothing is hand-written) and checks centring, `pointer-events` through the hierarchy, hit-testing of every
   card and close button, pointerdown, drag transform, snap-back, threshold dismissal, a real mouse click on the close button,
   fade-mask hit-testing and nav-rail clearance (with the rail control still clickable), plus the earlier scenarios: **58/58**.
   Its own failure cases were demonstrated: legacy markup under the new CSS fails on the first check; removing
   `pointer-events: auto` fails hit-testing, pointerdown, drag and close; removing slot centring fails with `x=16` (the
   reported left-edge symptom).

**Runtime caveat:** static CSS/JS refresh live but compiled Razor and `_Layout.cshtml` do not. After taking this branch the
Aspire stack (Ui.Server and its client build) must be **rebuilt and restarted**, then the browser hard-refreshed. A partial
refresh reproduces the reported symptoms.

---

## Follow-up review (before the operator re-smoke)

An independent focused review of the fix above found no Critical issues and confirmed positioning, hit-testing
through the layer/stack/slot/card hierarchy, swipe, and nav-rail clearance were otherwise correct. Two Important
findings were real defects, both introduced by the `pointer-events: auto` hardening itself, and are now fixed and
proven by new harness checks (**62/62**, up from 58):

1. **A leaving toast stayed interactive.** `.ui-toast-slot[data-phase="exiting"]` relied on `pointer-events: none`
   reaching the card by inheritance; once the card set `auto` on itself, a fading/collapsing ghost (~380 ms) kept
   taking clicks and hover. Fixed by also setting `pointer-events: none` on
   `.ui-toast-slot[data-phase="exiting"] .ui-toast`.
2. **A mouse dismiss could pause every remaining toast.** The keyboard-focus hand-off after dismissing a focused
   toast used `document.activeElement === button`, but Chrome focuses a button on mousedown too, so a plain mouse
   click on X also triggered it — focusing a neighbour's close button, which reported a Focus hold and paused the
   stack's countdowns until the operator clicked elsewhere. Fixed by gating the hand-off on `e.detail === 0`
   (a keyboard-activated click), which a real mouse/touch click never has.

Also added on review: a warning log when `appertureToast.attach` fails (mixed-version state was silently
invisible), a test pinning the `@ref="_stack"` target (unverifiable by `HtmlRenderer`, which never runs
`OnAfterRenderAsync`), a test asserting the JS↔`ToastHost` interop contract (`[JSInvokable]` names, the
`appertureToast.attach`/`detach` exports, and that the script's reported hold names are exactly `ToastHold`'s),
tightened class/attribute coupling checks (token-exact instead of substring), harness checks for exiting-card
click-through and coarse-pointer close-button sizing, and a harness check that page controls under the
(click-through) stack area stay clickable.

Known, accepted gaps in this layer of testing (a compiled/stale client is exactly the failure mode a same-build
render test *cannot* see by construction — that is what the real-runtime probe above and the operator re-smoke are
for): the render tests use a synthetic `HtmlRenderer` host, not a live circuit, so `OnAfterRenderAsync`/JS interop
itself does not run there; the harness page is a minimal stand-in for the shell (no `TopBar`, dialogs, or real
`NavRail`) and only Chromium is driven.

---

## Verification

**Automated (exact totals are in the completion report):** `ToastServiceTests` (manual-clock lifecycle: order, dwell,
pause/resume with remaining time, overlapping holds, hold-at-enqueue, exit phase, exactly-once removal, idempotent
dismiss, swipe direction, bound + eviction order, disposal); `ToastFoundationTests` (policy, markup, CSS, script
wiring incl. exit-duration parity and nav-rail clearance); node tests for the pure gesture math
(`node --test tests/Apperture.Management.Ui.Client.Tests/js/toast-gesture.test.mjs`, not part of the dotnet gates).

**Real-Chrome harness (fixture from the real component; .NET side stubbed):** `tests/.../browser/toast-browser-harness.mjs`
(usage in its header; not part of the dotnet gates) loads the *production* `app.css`, `management-ui-primitives.css` and
`toast-host.js` with markup exported from the real `ToastHost`, driven through the Chrome DevTools Protocol: placement at 1024/1440/1920 and 390 wide, no horizontal overflow, ordering, height cap, wheel and
touch scrolling, mouse and touch swipe (both directions, threshold, snap-back, opacity, vertical/diagonal intent),
holds (hover, focus, drag, scroll, stuck-hold recovery), focus continuity, exit animation, reduced motion, contrast in
both themes, nav-rail clearance, lost mouse release, and detach. 58/58 checks passed. It found two real defects (below).
It still does **not** exercise Blazor's live renderer, `ToastService` in the browser, or JS interop; those were covered by the
one-off real-runtime probe described above (29/29 + 5/5) and are the subject of the operator re-smoke.

### Operator re-smoke (short; required before merge) — on a rebuilt, restarted app with a hard refresh

1. A toast is bottom-centre on desktop (not at the left edge).
2. The X dismisses it.
3. A short horizontal mouse drag moves it and it snaps back.
4. A long horizontal drag dismisses it.
5. Hovering pauses the timer (it stays past 6 s; leaving resumes the remaining time).
6. Narrow layout still clears the nav rail (its bottom Config button stays clickable).
7. An overflowing stack (trigger many toasts) is capped and scrolls.

### Full operator smoke checklist

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
| **Critical (independent review):** the running app is served by `Ui.Server`'s `_Layout.cshtml`, not `Ui.Client/wwwroot/index.html`; only the latter loaded `toast-host.js`. In the real app `appertureToast` would have been undefined, the host's attach failure is swallowed by design, and swipe, hold-pausing, follow-newest scrolling and focus continuity would all have been silently missing (with the scrollbar hidden, newest toasts could land below the fold). | Script added to `_Layout.cshtml`; a guard now asserts **every** page that hosts the client loads it. |
| Real defect found by the browser harness: pressing on already-selected toast text started a native drag-and-drop that cancelled the swipe. | `dragstart` is prevented inside the stack. |
| Real defect found by the harness while fixing review items: reacting to `lostpointercapture` cancelled touch swipes (re-targeting implicit capture to the card reports a loss on the previous target). | Only the card losing its own capture ends a drag. |
| Review: a mouse release lost outside the window / after alt-tab left the card following the cursor; a second finger lifting released the touch hold early. | Drag ends when a mouse move reports no buttons or capture is lost; touches are tracked per pointer. |
| Review: .NET drops interaction holds when the stack empties, so JS could think a hold was still reported and never re-send it for the next toast. | Interaction holds are re-asserted whenever a toast is added; "never reported" means released. |
| Review: `TimeProvider.CreateTimer` throws for durations beyond ~49 days, which an explicit `duration` could reach mid-enqueue. | `ToastPolicy.MaxDuration` (24 h) clamps explicit durations. |
| Review: host disposal could skip releasing holds / disposing the .NET reference on an unexpected exception. | `try/finally`. |
| Review: an always-present `region` landmark would be empty on most pages; all close buttons shared one name. | `role="group"` instead of a landmark; each close button is described by its own toast text. |
| Review: `Reconcile`'s announce-once logic was only substring-tested in the Razor host. | Extracted into the unit-tested `ToastAnnouncer` (announce once, assertive only for errors, skip leaving/old toasts, forget removed ids). |
| Review: generation-stamped timers were never exercised (the fake removed disposed timers). | `ManualTimeProvider` can replay callbacks of cancelled timers; tests cover pause, re-arm and dismiss races, plus re-arm-while-held, persistent↔timed re-arm, eviction order, `Hidden` surviving an empty stack, and no-`Changed` on hold changes. |
| Known, accepted: a toast shown moments before a layout swap remounts the host may be announced again (or silently) by the new host. | Rare and brief; an app-level announced set would be the fix if it ever matters. |
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
