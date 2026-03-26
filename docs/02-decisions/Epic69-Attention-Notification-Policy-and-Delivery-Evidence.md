# Epic 69 — Attention notification policy and delivery evidence

## Status

Accepted

## Context

Epics 64–68 made schedule attention truthful and stateful: incidents persist, transition, and can be acknowledged. Without durable notification evidence, operators still had to infer whether anyone was alerted. Raw evaluator output on every observation pass is the wrong trigger for outward signaling; it would duplicate noise and conflate detection with communication.

## Decision

Introduce a small **incident-driven** notification layer for `expected_run_missing` only:

1. **Incident truth** remains in `IAttentionIncidentTracker` / `AttentionIncidentEntity` (unchanged responsibility).
2. **Policy** is instance configuration (`AttentionNotificationOptions` under `AttentionNotifications:ExpectedRunMissing`): enabled flag, email recipients, optional reminder interval, whether resolved emails are sent, and whether acknowledgment suppresses reminders.
3. **Delivery evidence** is append-only rows in `AttentionIncidentNotifications` via `IAttentionIncidentNotificationLedger`, with kinds `Activated`, `Reminder`, `Resolved` and statuses `Succeeded`, `Failed`, `Suppressed` (plus honest failure summaries; no fake success).

`AttentionIncidentNotificationOrchestrator` runs **after** `ObserveScheduleExpectationAsync` returns an `AttentionIncidentObserveResult`:

- **Opened**: activation emails (or a single suppressed row for policy/recipient/SMTP preconditions); idempotent guard via non-suppressed activation attempts.
- **ObservedStillActive**: optional reminders when `ReminderInterval` elapsed since last successful activation/reminder, unless suppressed by acknowledgment (when configured).
- **Resolved**: optional resolved email when `SendResolvedNotification` is true and a **succeeded** activation existed; otherwise a suppressed row documents why (disabled, no prior success, no recipients).

Email delivery uses `System.Net.Mail.SmtpClient` behind `IAttentionNotificationSender` (`SmtpAttentionNotificationSender`). Missing host/from yields failed send results with explicit reasons; SMTP errors persist as `Failed` rows.

Schedule diagnostics API/UI include a bounded list `RecentAttentionIncidentNotifications` for recent incidents on the integration.

## Consequences

- Operators can answer who was notified, when, outcome, and suppression reason from SQL and diagnostics.
- New EF migration `Epic69_AttentionIncidentNotifications` adds the table and indexes on `(AttentionIncidentId, NotificationKind)` and `(AttentionIncidentId, CreatedAtUtc)`.
- Future work: more channels, per-integration overrides, templates, escalation, and richer preference UI without changing the separation between incident truth, policy, and evidence.

## Limitations

- Single condition (`expected_run_missing`) and single channel (email).
- Recipients are instance-scoped list only (no on-call routing).
- Resolved notifications require a prior **successful** activation delivery (failed activation does not trigger resolved email).
