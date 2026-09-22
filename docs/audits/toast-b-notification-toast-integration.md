# Toast-B — Notification → Toast integration

**Status:** implemented and complete (2026-09-22, `b3d4d1c`). Six phases (P0–P5) delivered channel-independent
in-app notification materialization, a per-user/per-definition Toast preference, a delta-polling watcher, and
the account/authoring UI to control it. Decision record: [ADR-0042](../02-decisions/ADR-0042-notification-to-toast-channel-model.md)
(full schema, endpoint contract, deferred/out-of-scope items). Builds on the Toast presentation layer
[Toast-A](toast-a-toast-ux-foundation.md) shipped and the notification pipeline [ADR-0037](../02-decisions/ADR-0037-platform-signals-events-and-notification-system.md)
established.

Scope: connect the existing persistent in-app notification inbox to the existing Toast presentation system.
Toast is a **presentation** of an inbox item, not a new delivery channel — every Toasted item is, and remains,
an ordinary readable inbox row whether or not a Toast was ever shown for it.

---

## What shipped, by phase

### P0 — characterize and fix channel independence

Started by characterizing the existing email-recipient-resolution baseline with tests (`14fa061`), then closed
a real, pre-existing bug: in-app inbox materialization for a routed user depended on that user having at least
one eligible **email** recipient. A user with no email address (or email disabled) was silently dropped from
the in-app inbox entirely, not merely from email delivery. `3daa315` added a channel-independent `InApp`
eligible-recipient list alongside the existing email `Deliverable`/`Skipped` lists in
`SqlNotificationRecipientResolver`, threaded it through the recipient snapshot JSON, and fixed the inbox
extractor and instance-suppression gate to consult it. `91eb2a3` closed final-review gaps (a JSON round-trip
test, `InApp`/`ManagedContactId` fidelity). P0 also left two facts parked for later phases, not silently
fixed: Reminder/Resolved notifications remain email-gated for no-email-only routes (still true today — see
*Known accepted gaps*), and widening the suppression gate to `Deliverable == 0 && InApp == 0` closed a
re-activation remediation path that existed before this body (accepted as correct, but a real behavior delta
worth naming).

### P1 — schema and resolver

`4edf99d` added the `NotificationUserPreferences` table and `NotificationDefinition.ToastEnabledByDefault`
column via migration `TstB1_NotificationTogglePreferences`, seeded Toast-by-default for four built-in
definitions, and converted historical self-service "Email off" overrides into explicit `email=false`/
`toast=false` preference rows (deliberately leaving the legacy override rows in place at this point — nothing
yet consulted the new table). `ed2d285` added the preference store and
`NotificationTogglePreferenceResolver`'s resolve-at-read, no-row-means-inherit-default semantic. `ed65d01`
round-tripped the Toast default through the Definition create/update API. `e7ba5d0` closed final-review gaps:
deferred the legacy override deletion to P2 (the read side wasn't wired up yet), added a backup runbook note,
and hardened tests.

### P2 — wire the email channel to the new preference table, retire the legacy path

`19a9520` added a batch per-definition read to the preference store. `e93706a` made email recipient eligibility
consult the per-user `email` preference channel — the first real reader of the new table. `afeea56` rewrote the
self-service Email switch to be channel-specific: it now writes only the `email` preference row, never touches
the user's subscription/route, and — as an incidental but real security fix — no longer calls
`RemoveUserOptOutAsync`, which previously let a user's own "Email on" click delete an admin-written global
opt-out targeting them (no actor check on that deletion). `0de007e` then retired the now-redundant legacy
override rows (migration `TstB2_RetireLegacyEmailOptOutOverrides`), using the exact same predicate the first
migration used to select conversion candidates. `7b9dbb9` closed the phase's mandatory-review findings: fixed
an upsert race in `SqlNotificationUserPreferenceStore.SetAsync` (catch the unique-index conflict, re-query,
update instead of insert), added a runbook deployment-sequencing note, and added a migration handoff test.

### P3 — Toast dedupe key and severity mapping

`eff8bfa` added an optional `dedupeKey` parameter to `IToastService.Show` (5-parameter form), re-arming a
currently-visible toast by identity instead of content signature when present. `bc1951e` added
`NotificationToastSeverityMapper.Resolve`, mapping a raw severity token onto `ToastSeverity` plus an optional
explicit duration (Critical/Error get 10s; Warning/Info use the default; never `Success`), protected by a
two-layer exhaustiveness guard (a project-scoped `CS8509`-as-error plus the language's own
`SwitchExpressionException` backstop). `cf13511` closed final-review gaps: accurate guard comments, dedupeKey
documentation and normalization (blank/whitespace treated as "no key"), and a drift test tying the mapper's
literal tokens to the platform's real severity constants. `ec695c4` relocated that drift test out of the UI
test project to where it belongs.

### P4 — the delta endpoint

`9071289` added a composite-cursor (`CreatedAtUtc`, `Id`) Toast delta query to the inbox store — keyset
pagination, ascending on incremental polls, descending-then-reversed for the bootstrap/no-cursor case.
`da26bb2` guarded the query's SQL translation (a regression test calling the exact production `IQueryable`,
not a hand-copied one) and validated cursor-pair well-formedness. `3982473` added
`GET api/me/notifications/toast-delta`, injecting `INotificationUserInboxStore` directly into
`MeNotificationsController` (bypassing the service layer's expensive repair scan, deliberately, for this
30-second polled path). `c7fe3c9` closed mandatory-review findings: an index-safe unread count, a slim
projection (avoiding hydrating large JSON/body columns EF would otherwise load per row per poll), resolver/SQL
eligibility-logic alignment, severity assertions, and a bootstrap flag.

### P5 — the watcher and the UI

`7c802d0` added the Toast preference service and a nullable dual-channel preference request shape.
`bfba5a7` added a missing email-to-toast channel isolation regression test (writing one channel's preference
must never touch the other's row or its reported state — see *Channel-independence guarantees*, below).
`cbf1bda` added the Toast toggle to Account → Notifications, alongside the existing Email toggle, with a
composite busy-key so the two channel controls in one row don't co-disable each other. `d603986` corrected
now-inaccurate "not available yet" copy that predated this body's own Toast delivery; `baf4828` fixed an S10
test assertion the copy fix broke. `49f4df5` round-tripped the Toast default through the Definition authoring
form. `ed2efa3` added the notification toast watcher service; `67003f2` fixed a real watcher bootstrap-loss bug
found by review (see below) and closed other review findings; `8a9599b` fixed a shared timer test-harness bug
and closed the final watcher review gaps. `b3d4d1c` wired badge liveness (the bell menu's unread indicator now
reflects the watcher's own cheap `UnreadCount`) and added a notification settings link to the bell dropdown
menu, mirroring the existing link pattern on the Notifications page.

**The watcher's `IsBootstrap` bug — found by review, not by any test.** The watcher's original design used the
server's `IsBootstrap` response flag directly as its "is this my first poll" signal. This was wrong: an
operator with a genuinely **empty** inbox at watcher start gets `IsBootstrap: true` with the cursor echoed back
unchanged (there being no last row to advance past), so the watcher's *second* poll still sends no cursor and
the server — which computes `IsBootstrap` purely from what the caller sent, with no memory of prior polls —
marks that poll bootstrap too. Gating suppression on the server's flag would have silently and permanently
dropped that second poll's real content, for exactly the user most likely to hit it: a brand-new operator, or a
fresh single-tenant Instance, on day one. **Fix:** the watcher now tracks its own `_baselineEstablished` flag,
set only after its own first successful poll of the current session/epoch, and never consults
`response.IsBootstrap` for suppression. Closed with a regression test. This is recorded here in full because
it is exactly the kind of subtle, real-user-impacting bug this body's own review process exists to catch, and
because it is useful context for anyone touching this watcher later — see
[ADR-0042](../02-decisions/ADR-0042-notification-to-toast-channel-model.md#watcher-dedupe-bootstrap-suppression-and-the-isbootstrap-bug)
for the full mechanism.

---

## Channel-independence guarantees, with evidence

The central guarantee this body had to prove: toggling one delivery channel (Email, Toast) for a notification
must never move, silently change, or be silently changed by another channel's state, and the always-on in-app
inbox must never depend on any channel-specific state. Evidence, all re-run and passing on this session's final
build of `toast-b-notification-integration` (branch tip `b3d4d1c`):

| Guarantee | Evidence |
|---|---|
| Writing the `email` preference never creates, deletes, or changes the `toast` preference row (and vice versa); each API response reports both channels' current state regardless of which one was written | `ManagementHostAccountProfileHttpTests.An_email_preference_round_trips_and_is_reflected_on_reload` (real HTTP host, real SQL Server) — extended in `bfba5a7` specifically to assert this after finding the coverage gap; mirrored at the service layer in `AccountProfileServiceTests` |
| A missing/deleted notification definition resolves to "not Toast-eligible" unconditionally, even if a stale preference row exists, and the resolver and the delta endpoint's SQL-level eligibility check agree on every input | `OperatorNotificationToastPreferenceServiceTests`, `SqlNotificationUserPreferenceStoreTests`, `SqlNotificationUserInboxStoreToastDeltaTests`, `SqlNotificationUserInboxStoreToastDeltaSqlTranslationTests` |
| In-app inbox materialization does not depend on email eligibility | `NotificationInstanceCoordinatorTests` (channel-independent `InApp` recipient list, P0) |
| The delta endpoint's cursor, bootstrap flag, and eligibility projection behave correctly, including the malformed-cursor-pair guard | `ManagementHostNotificationToastDeltaHttpTests` (real HTTP host) |
| The watcher never toasts on its own first poll of a session, correctly distinguishes that from the server's `IsBootstrap` flag, dedupes by inbox item id (bounded 256-entry set), and backs off failures without ever surfacing an error toast | `NotificationToastWatcherTests` |
| The severity mapper never produces `Success`, and its literal severity tokens haven't drifted from the platform's real severity constants | `NotificationToastSeverityMapperDriftTests` |
| The Account UI renders a Toast toggle alongside Email with independent busy-state and no unbuilt-capability claim left over | `S9PersonalNotificationPreferencesUiTests` |

Test run totals from this session's own execution (default build output, not the isolated `--artifacts-path`
used only for the whole-solution sanity build — see *Verification*): `Apperture.Management.Ui.Client.Tests`
filtered to Toast/Notification — **76/76 passed**; `Apperture.Management.Ui.Shared.Tests` filtered to
Toast/Notification — **129/129 passed**; `Apperture.IntegrationTests` filtered to the toast-delta and account
profile HTTP tests — **53/53 passed** (real HTTP host against a real SQL Server); the core channel-independence/
severity/dedupe unit test classes named in the table above (`Apperture.UnitTests`) — **58/58 passed**. A wider
`Apperture.UnitTests` run filtered only on the keywords "Notification"/"Toast" (259/262 passed) additionally
surfaced 3 failures, all in `Epic77UatDocumentationTests` (a pre-existing, unrelated documentation-checklist
test class that locates the repo root via a fixed relative-path walk from the test binary's output directory);
those 3 failures are a confirmed artifact of this session running that one filtered pass under a non-standard
`--artifacts-path` (needed to avoid a file lock from a running Aspire host — see *Verification*), not a code
regression — the same class is untouched by this body and unrelated to Toast-B.

---

## Migration and legacy-conversion summary

Two migrations, meant to be deployed together (see
`docs/03-operations/notification-toggle-preference-migration-runbook.md` for the full sequencing note and
post-deploy verification query):

- **`20260922075738_TstB1_NotificationTogglePreferences`** — adds `NotificationDefinition.ToastEnabledByDefault`
  (seeded `true` for `platform.schedule.expected_run_missing`, `module.sample.helloworld.demo.completed`,
  `module.sample.fileimport.import.completed_with_errors`, `module.sample.apipolling.sync.completed_with_errors`;
  `false` elsewhere), creates `NotificationUserPreferences`, and converts historical self-service "Email off"
  overrides (`NotificationSubscriptionUserOverrides` rows with `OperationalTeamId IS NULL AND
  CreatedByActorId = UserId`) into explicit `email=false` + `toast=false` preference rows, idempotently. The
  source override rows are deliberately left in place by this migration.
- **`20260922102920_TstB2_RetireLegacyEmailOptOutOverrides`** — the deferred second half: deletes those same
  source override rows, once both halves of the real read/write wiring (P2) were confirmed live. Uses the
  identical selection predicate as TstB1. Admin-written global opt-outs and all team-scoped opt-outs were never
  conversion candidates and are untouched by either migration.

**Row counts found during this body's own work** (recorded in the runbook, not re-verified in this closure
pass): a read-only count against the reachable dedicated integration-test SQL Server
(`127.0.0.1,1433`, catalog `Apperture_IntegrationTests`, 2026-09-22) found **0** rows matching the conversion
predicate — expected, since that catalog is a throwaway/reset integration-test database. The migration's
original implementer separately found **3** qualifying rows against a populated dev database not reachable
from this environment. Any real deployment should re-run the predicate count against its own target database
before applying these migrations, exactly as the runbook says.

---

## Known, accepted gaps

These are deliberate, documented trade-offs — not overlooked bugs — recorded here so nobody "fixes" them
without a product decision:

1. **Backfilled/out-of-order inbox rows are invisible to the delta feed.** A row materialized by the
   repair/backfill path stamps `CreatedAtUtc` to the historical instance's creation time, which can land behind
   an already-advanced cursor. Such a row still increments `UnreadCount` but can never appear in a delta
   response — consistent with "no historical replay," by design. An operator can see their badge count rise
   with no corresponding Toast; that is this design working as intended.
2. **Two unread-count sources exist, deliberately not reconciled.** The delta endpoint's `UnreadCount` (cheap,
   no-repair scan) and `GET api/me/notifications/unread-count`'s count (repair-triggering) can differ for a
   user with un-materialized backlog. The watcher's badge uses the cheap source; the bell/inbox continues to
   use the repair-triggering one on open. Mixing them anywhere would cause a visible flicker — this body
   picked one per consumer and documented the split rather than unifying the two endpoints.
3. **Reminder/Resolved notifications remain email-gated for no-email-only routes** — a P0 finding, not
   introduced or fixed by this body. `NotificationInstanceCoordinator`'s reminder gate and resolved-suppression
   check both still require a succeeded email outbox row before firing, so an inbox-only/no-email user can get
   an Activation inbox row with no matching Resolved row ever arriving for the same incident. Left as an
   explicit, parked finding pending a product decision — not a silent side-fix bundled into this body.
4. **No Toast action/click-through, no self-service "unsubscribe from all Toasts," and multi-tab duplicate
   Toasts are accepted without cross-tab coordination.** See [ADR-0042](../02-decisions/ADR-0042-notification-to-toast-channel-model.md#deferred--explicitly-out-of-scope)
   for the full deferred list.

---

## Manual browser-smoke checklist

This session has no access to real browser automation (no CDP harness was run against a live app for Toast-B
specifically). **No claim of a passed browser smoke is made here.** Before this branch is treated as
operator-verified, run the following against a rebuilt, restarted app (the same runtime caveat Toast-A's own
audit documents: static CSS/JS refresh live, but compiled Razor/`_Layout.cshtml` do not):

1. **Preference round-trip.** Account → Notifications: toggle Email and Toast independently for the same
   notification row; reload the page; confirm each channel's state persisted independently (toggling one never
   visibly flips the other).
2. **Definition-level default.** As an administrator, edit a notification Definition's Toast default; confirm
   a user who has never set a personal Toast preference for that definition starts seeing (or stops seeing)
   Toasts for it, matching the new default; confirm a user with an explicit personal preference is unaffected.
3. **Live Toast delivery.** Trigger a Toast-eligible notification for the signed-in operator (e.g. an
   `expected_run_missing` condition, or a sample module's completed-with-errors notification, both seeded
   Toast-on-by-default) while the Management UI is open; confirm a Toast appears within roughly 30 seconds
   without a manual page refresh, with the correct severity styling and duration (Critical/Error auto-dismiss
   around 10s; Warning/Info use the normal default).
4. **Bootstrap suppression.** Sign in as an operator with a genuinely empty notification inbox; confirm no
   Toast appears on the very first poll, and that a notification delivered shortly after sign-in (i.e. on a
   later poll) *does* Toast — this specifically exercises the `IsBootstrap`/`_baselineEstablished` fix above.
5. **No duplicate on re-poll.** Leave the app open across at least two poll cycles (~60s) with no new
   notifications; confirm the same item is not re-Toasted.
6. **Badge liveness.** With the app open and a new Toast-eligible notification delivered, confirm the bell
   menu's unread badge updates without the operator having to open the bell dropdown.
7. **Settings link.** From the bell dropdown menu, follow the new "Notification settings" link and confirm it
   lands on Account → Notifications.
8. **Multi-tab.** Open the app in two tabs signed in as the same operator; trigger one Toast-eligible
   notification; confirm both tabs Toast it independently (accepted behavior, not a bug — see *Known accepted
   gaps* / ADR-0042's deferred list).
9. **Severity styling sanity.** Trigger (or simulate, if a test hook exists) at least one Critical/Error and
   one Warning/Info Toast originating from a Notification; confirm they are visually distinguishable and that
   the error one auto-dismisses rather than persisting like an ordinary feature-code error toast.

---

## Verification

**`dotnet build Apperture.slnx`** — succeeded with 0 errors (46 warnings, all pre-existing/unrelated to this
body) as this task's final sanity check. Run via `--artifacts-path` pointed outside the normal `bin/`/`obj/`
tree, because a locally running Aspire-hosted `Apperture.Management.Host` process held a file lock on its own
`bin/Debug` output for several shared dependency DLLs during this session — a known local-dev build/lock
interaction, not a code issue (the running host process was left untouched, per this repo's own operating
convention of never killing a user's running process to unblock a build). The isolated artifacts directory was
deleted after use.

**Automated test evidence** — see *Channel-independence guarantees*, above, for the full breakdown and the
explanation of the one unrelated, confirmed-harmless test-class failure encountered while using the same
isolated build path for one filtered `Apperture.UnitTests` pass.

**Manual/browser verification** — not performed in this session (no browser automation available); see the
concrete checklist above.
