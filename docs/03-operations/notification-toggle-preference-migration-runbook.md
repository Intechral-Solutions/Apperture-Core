# Notification toggle preference migration — deployment notes

Deployment notes for SQL migration `20260922075738_TstB1_NotificationTogglePreferences`, and its
deferred follow-up `20260922102920_TstB2_RetireLegacyEmailOptOutOverrides`.

## Update: the deferred deletion has now landed (TstB2)

As of `20260922102920_TstB2_RetireLegacyEmailOptOutOverrides`, the "Legacy override rows are
intentionally left in place" section below is historical: it describes the state between TstB1 and
TstB2, not the current one. TstB2 deletes the `NotificationSubscriptionUserOverrides` rows matching
the exact same predicate TstB1 used to select conversion candidates
(`OperationalTeamId IS NULL AND CreatedByActorId = UserId`), and is safe to apply now because both
halves of the read/write wiring this section describes as "planned for a later phase" have since
landed:

- `SqlNotificationRecipientResolver`'s recipient-eligibility check consults the per-user `email`
  preference channel this migration writes, instead of only the legacy override table.
- The self-service Email switch writes only that preference, never the legacy override table.

After TstB2, a person's self-service email opt-out is represented solely by their
`NotificationUserPreferences` row -- the `NotificationSubscriptionUserOverrides` row that TstB1
converted from is gone. Admin-written global opt-outs (`CreatedByActorId != UserId`) and all
team-scoped opt-outs (`OperationalTeamId` set) are untouched: they were never conversion candidates,
are not touched by TstB2's predicate, and remain the mechanism that suppresses delivery on
team/attention-routed paths, exactly as before. TstB2's deletion is naturally idempotent (a second run
matches and deletes zero rows), and, being data-only, its `Down()` cannot restore the deleted rows any
more than TstB1's `Down()` could restore the preference rows it created -- take a backup before
applying it, the same as any migration that deletes data.

**Deployment sequencing: TstB1 and TstB2 must be applied together, in the same deployment, with no
window in which pre-P2 application code runs against a database that has TstB1 applied but not
TstB2.** If that window existed, pre-P2 code (which still only writes the legacy override table, not
the new preference table) would keep writing new predicate-matching override rows that TstB1 never
converted, and TstB2 would then delete them with no replacement preference row -- silently resuming
email delivery to a person who had opted out. This is not a live risk on this repo's actual deployment
path: both migrations ship together as part of this same body of work and are never applied
independently. It is worth stating explicitly, though, for anyone who might cherry-pick or split this
deployment later. A one-line post-deploy check confirms no such gap occurred -- run this after both
migrations have applied and expect `0`:

```sql
SELECT COUNT(*) FROM [NotificationSubscriptionUserOverrides]
WHERE [OperationalTeamId] IS NULL AND [CreatedByActorId] = [UserId]
```

## What this migration does

This migration adds the storage a person needs to turn individual notifications on or off for
themselves, per delivery channel (email, in-app toast), instead of only getting the platform's
default behavior. It adds a new `ToastEnabledByDefault` column to notification definitions (turned
on for four built-in notifications — for example, a schedule's expected run going missing, or a
sample import completing with errors — and left off for everything else), and creates a new
`NotificationUserPreferences` table to hold each person's explicit per-notification, per-channel
choice.

It also does a one-time, best-effort data conversion: historical self-service "email off" choices
recorded in the older `NotificationSubscriptionUserOverrides` table are copied forward into the new
table as explicit `email=false` and `toast=false` preference rows, so a person who previously opted
out of email does not suddenly start receiving interruptive toast notifications after this upgrade.
The conversion only touches a person's own self-service override (never an admin-set or
team-scoped one), and it is safe to re-run — it skips any row that already has a matching preference
row.

## Take a backup before applying this migration

**Take a full backup of the database before applying this migration.** This is a general precaution
for any schema-changing production migration, and it matters more than usual here because of the
one-time data conversion described above: if the database is ever rolled back to before this
migration (`Down()`), the new `NotificationUserPreferences` table — including both the converted
legacy rows and any real choices people made after upgrading — is dropped and cannot be
reconstructed from schema alone. Do not rely on `Down()` as a way to recover preference data; rely on
a backup taken before `Up()` runs.

(The historical `NotificationSubscriptionUserOverrides` rows this migration reads from are *not*
deleted or otherwise put at risk by this migration — see the next section — but the new preference
data this migration creates only exists once, in the new table, and a backup is the only way to get
it back if something goes wrong.)

## (Historical — as of phase P1, before TstB2) Legacy override rows are intentionally left in place

As of this migration, the legacy `NotificationSubscriptionUserOverrides` rows that match the
self-service conversion predicate (`OperationalTeamId IS NULL AND CreatedByActorId = UserId`) are
**intentionally left in place, not deleted**. `SqlNotificationRecipientResolver.IsOptedOut` still
reads these rows directly to suppress delivery on team- and attention-routed paths, and nothing in
the codebase yet consults the new `email` preference channel this migration writes — that wiring is
planned for a later phase. Deleting the override rows now, before anything reads their replacement,
would silently resume email delivery for anyone who had opted out this way.

**This means applying this migration, by itself, does not change any user's actual email delivery
behavior.** It only prepares the new preference data so a later phase can safely switch recipient
resolution over to it (and, at that point, retire the now-redundant override rows in its own
migration).

## Reachable dev/test data at the time this doc was written

A read-only count against the reachable dedicated integration-test SQL Server in this environment
(`127.0.0.1,1433`, catalog `Apperture_IntegrationTests`, 2026-09-22) found **0** rows matching the
conversion predicate (`OperationalTeamId IS NULL AND CreatedByActorId = UserId`) in
`NotificationSubscriptionUserOverrides`. That catalog is a throwaway/reset integration-test database,
not a populated development environment, so a count of zero there is expected and does not indicate
anything about a real deployment target — it should be re-checked against the actual target database
before any real deployment, the same way this migration's original implementer found 3 qualifying
rows against a populated dev database that is not the one reachable from this environment.
