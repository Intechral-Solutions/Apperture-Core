# NTF-C closure report

Date: 2026-09-10  
Status: **Complete.** One canonical production notification delivery pipeline. The legacy Attention delivery path is gone.

## 1. Root cause of the blocker

The platform pipeline already replaced legacy delivery for activation, reminders, resolved, routing, inbox, instance dedupe, SMTP, and outbox retries. Escalation existed only in `AttentionIncidentNotificationOrchestrator.TryEscalationAsync`.

Committed Management Host `appsettings.json` already set `Notifications:UsePlatformPipeline=true`, so the Aspire/stock host skipped the orchestrator. Escalation was therefore already missing from the canonical development path. Removing the legacy path without adding platform escalation would have deleted required behavior.

Evidence: [blocker-report.md](blocker-report.md), [escalation-semantics.md](escalation-semantics.md).

## 2. Escalation semantics implemented

Preserved from production `TryEscalationAsync`:

| Topic | Behavior |
|-------|----------|
| When | `ObservedStillActive` only (not platform-event append, not Opened/Resolved) |
| Interval | `observedAt - incident.StartedAtUtc >= EscalationAfter` |
| Not configured / not due / routing disabled | No instance |
| Acknowledgement | When `SuppressEscalationWhenAcknowledged` and `AcknowledgedAtUtc` is set: **suppressed** instance (`escalation_suppressed_acknowledged`) |
| Recipients | `route.EscalationRecipients` only (not primary team / subscriptions) |
| No escalation team | **Suppressed** instance (`no_route_configured`) when due |
| Occurrence | **One escalation notification instance per incident** |
| Failed email | Same instance; `NotificationDeliveryOutbox` retries |
| After resolve | Escalation is not evaluated on Resolved |

Intentional improvements vs legacy: explainable suppressed instances for ack / missing escalation team; delivery retries are outbox retries, not a new occurrence.

## 3. How escalation flows through the platform pipeline

Attention observation (`ObservedStillActive`)
→ `INotificationEvaluateOnlyCoordinator.EvaluateIncidentAsync`
→ rule evaluation emits `reminder` and `escalation` matches
→ `NotificationInstanceCoordinator` applies routing gates
→ `NotificationInstances` (`kind=escalation`, dedupe `{definitionId}|{incidentId}|escalation`)
→ recipient snapshot from the escalation team
→ `NotificationDeliveryOutbox` + personal inbox where applicable
→ SMTP dispatch

No second direct-email path.

## 4. Dedupe and delivery-retry behavior

- Occurrence boundary: one platform notification instance per incident for escalation (`NotificationInstanceDedupeKeyBuilder`).
- Duplicate still-active observations reuse that instance (`TryCreateInstanceAsync`).
- Failed recipient rows stay on the same instance and retry through the outbox. A failed send does **not** create another escalation instance.

## 5. Legacy services / code / configuration removed

Removed (transition-only):

- `AttentionIncidentNotificationOrchestrator`
- `SmtpAttentionNotificationSender` / `IAttentionNotificationSender`
- `AttentionIncidentNotificationEmailComposer`
- `PlatformNotificationCutoverGate`
- `Notifications:UsePlatformPipeline` / `NotificationPipelineOptions` / setting `Notifications.UsePlatformPipeline.Enabled`
- `AttentionNotifications:ExpectedRunMissing` options and SMTP merge
- Cutover preflight, operating-mode classifier, sign-off/validation services and APIs
- Epic 69 vs platform parity comparer and UI
- Dual-path invocation in `AttentionIncidentObservationHostedService`
- Legacy ledger reads from schedule diagnostics and notification diagnostics

Retained (independent product controls):

- Attention incidents, routing, operational teams, managed contacts, acknowledgement
- `NotificationEvaluationOptions`, `NotificationInstanceOptions`, `NotificationDeliveryOptions`
- Platform events, definitions, subscriptions, instances, outbox, inbox, SMTP diagnostics

## 6. Legacy persistence removed

New forward migrations (historical migrations unchanged):

- `20260910160000_NtfC_ExpectedRunMissingEscalationKind` — updates built-in rule/template JSON
- `20260910170000_NtfC_DropLegacyAttentionNotificationTables` — drops `AttentionIncidentNotifications` and `ExpectedRunMissingCutoverSignOffs`

EF model snapshot updated. Legacy ledger rows were **not** migrated. Operational evidence remains in `NotificationInstances`, `NotificationDeliveryOutbox`, Attention incidents, platform events, and audit records.

## 7. Diagnostics / UI / docs simplified

- Integration notifications page no longer has cutover readiness, parity, or sign-off
- Config diagnostics no longer expose `UsePlatformPipeline` or dual-send warnings
- Schedule diagnostics no longer list Epic 69 ledger rows
- `ScheduleExpectationPlatformEventDualEmitter` renamed to `ScheduleExpectationPlatformEventEmitter`
- Current architecture docs describe one production path (signal catalog, template schema, settings registry, management configuration, application-context inventory)
- Historical ADRs/runbook/audits kept; marked completed rather than rewritten (S8C, S8D, S12, Epic 77C, ADR-0037, Epic 77 runbook)
- Production Windows/Azure appsettings examples leave Evaluation/Instances/Delivery off as operational SMTP gating, with comments that this is not a leftover cutover switch

## 8. Tests and builds

Executed 2026-09-10 (this continuation; `--no-dependencies` / `--no-build` to avoid locked host binaries):

| Suite | Result |
|-------|--------|
| UnitTests filter Notifications / ExpectedRunMissing / NotificationConfiguration / ScheduleExpectationPlatformEventEmitter / PlatformSettings / RuntimeVisibleDevMetadata / IntegrationScheduleDiagnostics | **185 passed** |
| Same project, broader filter adding Inbox / Outbox / Recipient / Attention | **236 passed** |
| `Apperture.Management.Ui.Shared.Tests` (full) | **716 passed** |
| `Apperture.ArchTests` (full) | **38 passed** |
| Builds: ControlPlane.Security, Persistence.SqlServer, Management.Api, UnitTests, Ui.Shared.Tests, ArchTests | **succeeded** |
| Management Host / WorkerHost output copy | **file-lock** from running `Apperture.Management.Host` (pid 7828) and `Apperture.RuntimeHost.Windows.WorkerHost` (pid 60204). Library compile succeeded; host/worker DLL copy failed (MSB3027). |

Targeted coverage includes activation, reminders, escalation threshold/routing/ack/dedupe, resolved per-recipient outbox, outbox failure without a second escalation instance, still-active → reminder+escalation evaluation, platform-event append not emitting escalation, inbox, recipient resolution, and configuration safety.

## 9. Notification debt intentionally left open

- Digest delivery (schema/UI constants exist; no digest dispatcher)
- Retry-now / send-now operator actions
- Non-email channels
- Repeat / multi-tier escalation policies beyond one occurrence per incident
- Log-source / telemetry rule stages
- Orphan `PlatformSettingValues` rows for the deleted `Notifications.UsePlatformPipeline.Enabled` key (harmless; no longer in the registry)

## 10. Confirmation

**There is now one canonical production notification delivery pipeline. The legacy Attention delivery path is gone.**

## 11. Addendum (2026-09-22): Toast presentation added by a later body (Toast-B)

Not part of NTF-C's own scope or timeline — recorded here because this is the closest current-state doc
describing the notification pipeline's overall shape, and it should not go stale as the pipeline gains a new
presentation. A later, separate body ("Toast-B") added a third presentation of a delivered notification
alongside email and the always-on in-app inbox this report describes: an ephemeral, session-scoped **Toast**,
gated by an explicit per-user preference (`NotificationUserPreferences`, channel `toast`) that inherits a
per-definition default (`NotificationDefinition.ToastEnabledByDefault`) when the user has not chosen explicitly,
resolved fresh on every read rather than copied at subscribe time. A client-side watcher polls a new endpoint,
`GET api/me/notifications/toast-delta`, roughly every 30 seconds while a session is open, and turns
newly-delivered, Toast-eligible inbox items into Toasts via the existing `IToastService`, de-duplicated by inbox
item identity. Toast is additive: it changes nothing about how or whether a notification reaches the email or
in-app channels this report covers, and every Toasted item remains an ordinary, permanently readable inbox row
regardless of whether a Toast was ever shown for it. Full detail: [ADR-0042](../../../02-decisions/ADR-0042-notification-to-toast-channel-model.md);
phase-by-phase account and known gaps: [`toast-b-notification-toast-integration.md`](../../../audits/toast-b-notification-toast-integration.md).
