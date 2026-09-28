# ADR-0042: Notification-to-Toast channel model

## Status

Accepted (delivered as Toast-B, 2026-09-22). Builds on [ADR-0037](./ADR-0037-platform-signals-events-and-notification-system.md)
(notification definitions, subscriptions, delivery) and consumes the Toast presentation surface
[Toast-A](../audits/toast-a-toast-ux-foundation.md) built (`IToastService`, `ToastPolicy`). Supersedes ADR-0037's
`NotificationUserPreferences` sketch — see the addendum added to that ADR alongside this one. Full phase-by-phase
account, test evidence and known gaps: [`toast-b-notification-toast-integration.md`](../audits/toast-b-notification-toast-integration.md).

## Context

ADR-0037 shipped a durable, multi-channel notification pipeline (definitions, staged rules, subscriptions,
async delivery) with Email as its only real delivery channel, plus a persistent in-app inbox that is always
populated for anyone routed to a notification. ADR-0037 itself only sketched a future `NotificationUserPreferences`
concept (quiet hours, severity minimum, digest) and never specified how — or whether — a Notification should ever
interrupt an operator's current screen the way a feature-code Toast does.

Separately, Toast-A rebuilt the presentation layer of the platform's existing Toast system (stacking, motion,
gestures, accessibility) but deliberately implemented none of the Notification side: no definition-level Toast
default, no per-user preference, no polling/push mechanism, and no Notification-aware dedupe. Its own closing
section ("Notification → Toast readiness") named exactly the open questions this ADR answers: a dedupe key
richer than content signature, where Notification severity maps onto `ToastSeverity`, and how a Notification's
existence becomes a Toast the operator sees while a session is active.

This ADR records what actually shipped to connect the two: incoming platform Notifications optionally surfacing
as Toasts, gated by an explicit per-user preference, without becoming a second, parallel delivery channel.

## Decision

### Toast is a presentation of the inbox, not a new delivery channel

A Notification Toast never exists independently of the persistent in-app inbox row it is presented for. Every
Toasted item is, and remains, an ordinary `NotificationUserInboxItem` whether or not a Toast was ever shown for
it — Toasting a notification does not change how or whether it is delivered to the inbox, and suppressing a
Toast (preference off, session not open, missed poll) never removes or alters the underlying inbox row. This
mirrors the in-app channel's own existing "always-on" status: in-app delivery was never gated by opt-out before
this body and still is not.

### Three channels, two preference tokens

The notification pipeline recognizes three presentations of a delivered notification: `email` (SMTP, opt-out
per user), the persistent in-app inbox (always-on, no opt-out in v1), and `toast` (an ephemeral, session-scoped
presentation of an inbox item, opt-in-by-default-choice per user). `NotificationChannels`
(`Apperture.Core.Domain.Notifications`) defines exactly two channel token constants — `"email"` and `"toast"` —
deliberately **not** `"inApp"`: there is nothing for an in-app preference row to mean yet, since in-app delivery
has no opt-out in v1 and a preference row with no consulting reader would be dead data. If in-app delivery ever
becomes optional, a third channel token is an additive schema change, not a rework of this model.

### `NotificationUserPreferences` schema (as shipped, not ADR-0037's sketch)

Added by migration `20260922075738_TstB1_NotificationTogglePreferences`:

| Column | Type | Notes |
|---|---|---|
| `Id` | `nvarchar(64)` | `nupref_` + 32-lowercase-hex-digit GUID, this repo's standard SQL-generated ID convention |
| `UserId` | `nvarchar(64)` | |
| `NotificationDefinitionId` | `nvarchar(64)` | |
| `Channel` | `nvarchar(16)` | `"email"` or `"toast"` (`NotificationChannels`) |
| `IsEnabled` | `bit` | |
| `CreatedAtUtc` / `UpdatedAtUtc` | `datetimeoffset` | |
| `UpdatedByActorId` | `nvarchar(64)`, nullable | |

Unique index on `(UserId, NotificationDefinitionId, Channel)` (`IX_NotificationUserPreferences_UserId_NotificationDefinitionId_Channel`).
This is a flat tri-state toggle per `(user, definition, channel)` — not ADR-0037's sketch of an `optOut: true`
flag with `definitionPattern`, quiet hours, and severity minimum (see the ADR-0037 addendum).

### Resolve-at-read, no-row-means-inherit-default — never copy-on-subscribe

`NotificationTogglePreferenceResolver.ResolveEffectiveToastEnabledAsync` (`Apperture.ControlPlane.Security`) is
the shipped semantic, resolved fresh on every call, never cached or copied at subscribe time:

```csharp
effectiveToast(user, definition) =
    the user's explicit "toast" preference row's IsEnabled, if one exists,
    otherwise definition.ToastEnabledByDefault
```

The definition's own existence is checked **first**: a missing/deleted definition resolves to "not
toast-eligible" unconditionally, even if a stale preference row for it still exists, so this method and the
delta-endpoint's SQL-level eligibility check (below) agree on every input combination rather than only the ones
reachable today. The identical rule is duplicated at the SQL level in
`SqlNotificationUserInboxStore.BuildToastDeltaScanQuery`/`ListToastDeltaAsync` for the polled endpoint, rather
than calling the resolver row-by-row on a 30-second, potentially-50-row poll.

### `ToastEnabledByDefault` and the built-in seeds

`NotificationDefinition.ToastEnabledByDefault` (added by the same migration, default `false`) lets an
administrator opt a definition into Toast-by-default so new subscribers don't have to discover and flip a
personal preference to see it. The migration seeds it `true` for exactly four built-in definitions:
`platform.schedule.expected_run_missing`, `module.sample.helloworld.demo.completed`,
`module.sample.fileimport.import.completed_with_errors`, `module.sample.apipolling.sync.completed_with_errors`.
Every other definition defaults to Toast-off; an operator or the notification's owner can still turn Toast on
per-definition through the authoring form (round-tripped end-to-end as of commit `49f4df5`) or per-user through
Account → Notifications (`cbf1bda`).

### Legacy conversion and the channel-independence fix (P0–P2)

Toast-B started by characterizing (`14fa061`) and then closing a pre-existing coupling: before this body,
in-app inbox materialization for a routed user depended on that user having at least one eligible **email**
recipient — a user with no email address (or email disabled) was silently dropped from the in-app inbox
entirely, not merely from email delivery. `3daa315` added a channel-independent `InApp` eligible-recipient list
alongside the existing email `Deliverable`/`Skipped` lists in `SqlNotificationRecipientResolver`, threaded it
through the recipient snapshot JSON, and fixed the inbox extractor and instance-suppression gate to consult it —
a routed user with no email now gets an inbox row instead of being silently dropped.

Because historical self-service "Email off" choices (`NotificationSubscriptionUserOverrides` rows with
`OperationalTeamId IS NULL AND CreatedByActorId = UserId` — the self-service predicate, as opposed to
team-scoped or admin-written global opt-outs) used to also suppress in-app delivery under the old coupling, the
same migration converts each qualifying row into **both** an explicit `email=false` **and** `toast=false`
`NotificationUserPreferences` row, so nobody who had opted out of email was surprised by a new, interruptive
Toast after upgrade. The original override rows were deliberately left in place at that point (nothing yet
consulted the new preference table), and were only retired later, once both halves of the real wiring existed:
`e93706a` (P2 Task 2) made `SqlNotificationRecipientResolver`'s recipient-eligibility check consult the per-user
`email` preference channel, `afeea56` (P2 Task 3) rewrote the self-service Email switch
(`OperatorNotificationSubscriptionService.SetEmailEnabledAsync`) to write only that preference row — never the
legacy override table, and never touching the user's subscription/route — and `0de007e` (migration
`20260922102920_TstB2_RetireLegacyEmailOptOutOverrides`) then deleted the now-redundant override rows, using the
exact same predicate the first migration used to select conversion candidates. Admin-written global opt-outs and
all team-scoped opt-outs were never conversion candidates and were untouched throughout. A read-only count
against the reachable integration-test SQL Server found 0 matching rows (a throwaway/reset database); the
original implementer found 3 qualifying rows against a populated dev database not reachable from this
environment — see `docs/03-operations/notification-toggle-preference-migration-runbook.md` for the full
deployment notes and the post-deploy verification query.

**Incidental security fix.** Before P2, the Email-enable path called `RemoveUserOptOutAsync`, which deleted any
matching `(UserId, DefinitionId, OperationalTeamId: null)` override row with no check on who created it — so a
user clicking "Email on" for themselves could delete an *admin-written* global opt-out targeting them. P2's
channel-specific rewrite eliminated that call entirely as part of making the Email switch write only the new
preference row. A real, if incidental, privilege-hole closure, not something P2 set out to fix. A side effect:
`OperatorEmailSubscriptionPreference.HasGlobalEmailOptOut` now means exactly what its name says (admin-only);
before P2 it could also be `true` because of a user's own self-service Email-off, which wrote to the same
override table.

### Delta endpoint

`GET api/me/notifications/toast-delta?afterCreatedAtUtc=&afterId=` (`MeNotificationsController`, added `3982473`,
hardened `da26bb2`/`c7fe3c9`). Both query parameters are optional but must be both-or-neither. The HTTP action
resolves the pair from query-string presence (a missing `afterCreatedAtUtc` is a
bootstrap request even if model binding supplies a `DateTimeOffset`) and returns
HTTP 400 ProblemDetails with a fixed detail when only one component is present.
The store still rejects a mismatched pair as `ArgumentException` so programmatic
callers cannot silently paginate incorrectly. No user-identifying parameter — the caller is resolved server-side like every
other `api/me/*` action. `INotificationUserInboxStore` is injected directly into the controller for this one
endpoint (bypassing `INotificationUserInboxService`) specifically to avoid the service layer's expensive
repair/backfill scan on a 30-second polling path.

**Response** (`NotificationToastDeltaResponseDto`): `Items` (`NotificationToastDeltaItemDto[]` — `Id`, `Subject`,
`Summary`, `Severity` (raw token, fed straight into `NotificationToastSeverityMapper.Resolve`), `NotificationKind`;
deliberately no action/url/link field — see *Deferred*, below), `NextAfterCreatedAtUtc`/`NextAfterId` (always
advance past everything scanned, including non-toast-eligible rows; fall back to the incoming cursor, never
`null`, when nothing new was scanned, so a quiet poll never looks like "start over"), `UnreadCount` (from the
cheap, no-repair `CountUnreadAsync` scan — **not** the same number `GET api/me/notifications/unread-count`
returns, which can materialize additional backlog rows before counting; the watcher's badge intentionally reads
this cheaper number rather than mixing the two), and `IsBootstrap` (`true` exactly when the request carried no
cursor — computed by the controller from what the caller sent, not by the store). Hard cap of 50 items per
response, not configurable from the client.

**Cursor design — no historical replay.** The cursor is a composite `(CreatedAtUtc, Id)` keyset pair. Rows
materialized by the repair/backfill path, or otherwise inserted out of wall-clock order, stamp `CreatedAtUtc` to
the historical instance's creation time rather than the row's actual insert time, so such a row can land behind
an already-advanced cursor. **This is an accepted, intentional consequence, not a bug**: such a row still
increments `UnreadCount`, but can never appear in `Items` via this feed. An operator can see the badge count
rise with no corresponding Toast; that is this design working as intended, not a missed delivery. See the SQL
comment at the query's cursor `Where` clause in `SqlNotificationUserInboxStore.ListToastDeltaAsync`.

### Watcher: dedupe, bootstrap suppression, and the `IsBootstrap` bug

`NotificationToastWatcher` (`Apperture.Management.Ui.Client.Services`, `ed2efa3`, fixed `67003f2`/`8a9599b`)
polls the delta endpoint on a 30 s cadence while the session is authenticated, maintains a bounded (256-entry)
in-memory FIFO set of already-delivered inbox item IDs so a re-fetched item is never Toasted twice in one
session, caps at most 5 Toasts shown per poll, backs off transient failures (30 s → 60 s → 120 s → 240 s → 300 s
cap, never surfacing an error Toast), and calls `IToastService.Show(severity, message, title, duration,
dedupeKey)` with the inbox item's `Id` as `dedupeKey` and severity/duration resolved by
`NotificationToastSeverityMapper.Resolve`.

**A real bug, found by review, not by any test.** The watcher's original design used the server's `IsBootstrap`
response flag as a proxy for "is this the first poll of my session" — gating Toast suppression on it directly.
This was wrong: a user whose inbox is genuinely *empty* at watcher start gets an `IsBootstrap: true` response
with `NextAfterCreatedAtUtc`/`NextAfterId` echoed back as the incoming (`null`) cursor (there being no last row
to advance past — `NextAfterCreatedAtUtc = last?.CreatedAtUtc ?? afterCreatedAtUtc`). The watcher's *second*
poll then still sends no cursor, so the server computes `IsBootstrap = afterCreatedAtUtc is null` and marks that
poll bootstrap too — purely from what the caller sent, with no idea this is actually the second poll of the
session. Gating suppression on the server's flag would have silently and permanently dropped that second poll's
real content, for exactly the operator most likely to hit it: a brand-new user or a fresh single-tenant Instance
on day one, with an empty inbox. **Fix:** the watcher tracks its own `_baselineEstablished` flag, set only after
its first successful poll of the current polling epoch (`Start()` / resume-from-session-change), and gates
Toast suppression on that instead of `response.IsBootstrap`. The two flags are deliberately not equivalent —
`IsBootstrap` still exists on the wire and is documented as *not* a safe bootstrap-detection proxy for exactly
this reason. Closed with a regression test (`bfba5a7`'s sibling coverage plus `NotificationToastWatcherTests`);
found via deep code review during this phase's own mandatory review gate, not by any automated check —
recorded here because the review process working as intended, on a subtle off-by-one-poll bug with a real user
impact, is worth a permanent record for anyone maintaining this watcher later.

### Severity mapping

`NotificationToastSeverityMapper.Resolve(string? severityToken)` (`Apperture.Management.Ui.Shared`, `bc1951e`)
maps a raw Notification severity token onto `NotificationToastPresentation(ToastSeverity Severity, TimeSpan?
Duration)`: `critical`/`error` → `Error` with an explicit 10 s duration (so a Notification-originated error
Toast auto-dismisses, unlike an ordinary feature-code error Toast, which persists until dismissed);
`warning`/`info` → `Warning`/`Info` with no explicit duration (the normal `ToastPolicy` default applies);
anything null/empty/unrecognized → `Info`. **Never** produces `ToastSeverity.Success` — a Notification `kind`
like `"resolved"` is orthogonal to severity and is never allowed to influence Toast presentation. The mapper is
protected by a two-layer exhaustiveness guard against a future severity being added to the internal enum without
a corresponding switch arm: `CS8509` is elevated to a build error scoped to
`Apperture.Management.Ui.Shared.csproj` only (not solution-wide), and the language's own
`SwitchExpressionException` is the runtime backstop if the compile-time guard is ever bypassed. A separate drift
test (`NotificationToastSeverityMapperDriftTests`, relocated out of the UI test project in `ec695c4`) guards the
mapper's literal token strings against the platform's actual severity constants, since the mapper deliberately
does not take a cross-project dependency just to reuse four string constants (see the class's own remarks).

### Dedupe key

`IToastService.Show` gained an optional fifth parameter: `void Show(ToastSeverity severity, string message,
string? title = null, TimeSpan? duration = null, string? dedupeKey = null)` (`eff8bfa`). A blank/whitespace
`dedupeKey` normalizes to "no key" (falls back to the pre-existing content-signature matching); the watcher
always passes the real inbox item ID, never an empty-string sentinel. A same-key re-arm only refreshes an
already-**Visible** toast's dwell countdown — it does not update the displayed severity/message/title and does
not re-announce it, and it does not revive or suppress display for an item whose toast has already fully
dismissed. That "already shown this session" tracking is the watcher's own bounded dedupe set, described above
— a deliberately separate concept from `dedupeKey` re-arming, which only ever concerns a currently-displayed
toast.

## Deferred / explicitly out of scope

- **No Toast action/click-through in v1.** `NotificationToastDeltaItemDto` carries no action/url/link field by
  design; a Notification Toast is not clickable to a destination.
- **No self-service "unsubscribe from all Toasts."** Preferences are per-notification-definition only.
- **Multi-tab Toasts are accepted without coordination.** Each open tab runs its own watcher instance and polls
  independently; an operator with several tabs open can see the same Notification Toasted more than once, once
  per tab. Not addressed by this body.
- **Backfilled rows are invisible to the delta feed** (see *Cursor design*, above) — an accepted, permanent
  consequence of "no historical replay," not a gap to close later without a design change.
- **Dual unread-count sources remain** (the delta endpoint's cheap `UnreadCount` vs. `GET
  api/me/notifications/unread-count`'s repair-triggering count) — the badge deliberately picks the cheap one;
  the two are not reconciled into one source.
- **Reminder/Resolved notifications remain email-gated for no-email-only routes** (a P0 finding, not
  introduced by this body and not fixed by it): `NotificationInstanceCoordinator`'s reminder gate and
  resolved-suppression check both still require a succeeded email outbox row before firing, so an inbox-only
  user gets an Activation row but may never get a matching Resolved row for the same incident. Left as a
  parked, explicit finding pending a product decision, not a silent side-fix.

## Consequences

### Positive

- A routed user with no email address (or email disabled) is no longer silently dropped from the in-app
  inbox — a real, if incidental, bug fix that predates and is independent of Toast presentation itself.
- Toast is additive to the existing inbox and Email channels: no existing behavior changes for an operator who
  never sets a Toast preference (default is off unless the definition opts in).
- The resolve-at-read, no-row-means-inherit-default model means a later change to a definition's
  `ToastEnabledByDefault` retroactively affects every user who has not made an explicit choice, with no
  migration or backfill needed.
- Closed a genuine, if narrow, privilege hole in the Email-enable path as a side effect of the channel-specific
  rewrite.
- The watcher's bootstrap-suppression bug was caught by review before shipping to any real user with an empty
  inbox, and is now covered by a regression test tied to the watcher's own state, not the server's flag.

### Negative

- Two unread-count code paths exist side by side (cheap delta-scan count vs. repair-triggering count) rather
  than one reconciled source — an accepted complexity cost of keeping the 30-second poll cheap.
- Backfilled/out-of-order inbox rows are permanently invisible to the delta feed; an operator can see their
  badge count rise with no explanation from a Toast. Mitigated only by this being documented, not by any UI
  affordance today.
- Multi-tab operators can see a duplicate Toast for the same notification, once per open tab.

### Neutral

- The two-channel-token design (`email`, `toast`, no `inApp`) means adding an in-app opt-out later requires a
  new channel token and a new set of readers, not a schema change to the existing two.
- ADR-0037's own `NotificationUserPreferences` sketch is superseded, not deleted — see its addendum.
