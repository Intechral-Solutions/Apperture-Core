# Escalation semantics (from current production code)

Evidence: `AttentionIncidentNotificationOrchestrator.TryEscalationAsync` and `SqlAttentionIncidentNotificationLedger.HasNonSuppressedEscalationAttemptAsync`. Not from stale docs.

## When it runs

Only from `HandleStillActiveAsync` after reminder handling, on `ObservedStillActive`. Not on Opened, Resolved, or NoChange.

If routing `Enabled` is false, `HandleStillActiveAsync` returns before escalation.

## Interval

`now - incident.StartedAtUtc >= route.EscalationAfter`.

- Anchor is **incident start** (`AttentionIncident.StartedAtUtc`), not last activation/reminder send.
- `EscalationAfter` comes from Attention routing (integration override if present, else instance defaults).
- Null or `<= TimeSpan.Zero`: no escalation (silent return). Reason constant exists (`escalation_suppressed_no_threshold`) but the orchestrator does **not** write a ledger row.

## Acknowledgement

If `SuppressEscalationWhenAcknowledged` and `incident.AcknowledgedAtUtc` is set: silent return (no ledger row). Constant `escalation_suppressed_acknowledged` exists but is unused on this path.

## Primary vs escalation routing

Uses `route.EscalationRecipients` / `EscalationSource` / `EscalationOperationalTeamId` only. Primary team is not used.

Recipients are already-expanded email strings from `IAttentionRecipientResolver` (operational-team members: principals, managed contacts, external emails).

## No escalation team

`EscalationRecipients.Count == 0`: silent return. No suppressed ledger row.

## Once vs repeat

**Once per incident** while it remains active. Repeat observations do not send again.

Duplicate guard: `HasNonSuppressedEscalationAttemptAsync` — any ledger row with kind Escalation and status **not** Suppressed (Succeeded **or** Failed).

## Failed email

One SMTP attempt per recipient is recorded. A Failed row still counts as a non-suppressed attempt, so the **occurrence** is not retried. There is no outbox.

## After resolve

Escalation is not invoked on Resolved. No cancellation of prior escalation rows.

## Platform-path improvements (intentional)

| Legacy | Platform (this cutover) |
|--------|-------------------------|
| Silent skip when not due / no team / ack | Not-due: skip without instance (same as reminders). Ack / no team when due: **suppressed instance** with a reason. |
| Failed SMTP blocks another occurrence | One **instance** per incident; `NotificationDeliveryOutbox` retries recipient delivery. |
| Recipients = escalation emails only | Same: escalation team, not primary subscriptions. |
