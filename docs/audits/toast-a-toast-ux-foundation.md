# Toast-A — Toast UX foundation

**Status:** in progress (implementation session). Notification → Toast integration is a **separate,
later** roadmap body and is not started here.

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

_Filled in as the work lands (see sections below)._
