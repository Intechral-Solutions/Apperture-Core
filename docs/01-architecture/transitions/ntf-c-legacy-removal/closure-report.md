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
- Current architecture docs describe one production path; this transition set is marked complete
- Historical ADRs (S12, Epic 77 plan) kept; status notes added where useful

## 8. Tests and builds

Executed 2026-09-10 (local):

| Suite | Result |
|-------|--------|
| UnitTests filter `Notifications` / `ExpectedRunMissing` / `NotificationConfiguration` / `PlatformSettingsServiceValidation` | **149 passed** |
| `Apperture.Management.Ui.Shared.Tests` | **716 passed** |
| Builds: Core.Application, ControlPlane.Security, Persistence.SqlServer, Management.Api, Management.Ui.Shared, UnitTests (`--no-dependencies`) | **succeeded** |
| Management Host / WorkerHost / ArchTests full rebuild | **file-lock** from running `Apperture.Management.Host` (pid 40476) and `WorkerHost` (pid 28496). Compile of the changed libraries succeeded; host output copy failed. |

Targeted coverage includes activation, reminders, escalation threshold/routing/ack/dedupe, resolved per-recipient outbox, outbox failure without a second escalation instance, still-active → reminder+escalation evaluation, platform-event append not emitting escalation, and configuration safety.

## 9. Notification debt intentionally left open

- Digest delivery (schema/UI constants exist; no digest dispatcher)
- Retry-now / send-now operator actions
- Non-email channels
- Repeat / multi-tier escalation policies beyond one occurrence per incident
- Log-source / telemetry rule stages
- Orphan `PlatformSettingValues` rows for the deleted `Notifications.UsePlatformPipeline.Enabled` key (harmless; no longer in the registry)

## 10. Confirmation

**There is now one canonical production notification delivery pipeline. The legacy Attention delivery path is gone.**
