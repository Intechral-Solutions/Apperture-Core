# S8D — Epic 69 Email Parity / Legacy Attention Email Alignment

Epic S8D aligns the legacy Epic 69 attention email path with the platform S8B/S8C email pipeline when the legacy path is still active.

## v1 path choice

| Path | When active | Email behavior |
|------|-------------|----------------|
| **Platform notification pipeline** (recommended for cutover) | `Notifications:UsePlatformPipeline:Enabled=true` plus evaluation/instances/delivery flags | Instances, outbox, subscriptions (S9), diagnostics (S10) |
| **Epic 69 legacy orchestrator** (when cutover off) | `Notifications:UsePlatformPipeline:Enabled=false` (platform-setting definition default) | `AttentionIncidentNotificationOrchestrator` + ledger; **uses shared composer** |

**Stabilization host baseline:** Management.Host `appsettings.json` sets `UsePlatformPipeline=true` (platform preferred; Epic 69 bypassed). Legacy remains available when that flag is false. It is **bypassed** when `Notifications:UsePlatformPipeline:Enabled=true` (`PlatformNotificationCutoverGate`).

The legacy path is **retained temporarily** for escalation/parity until removal criteria in S12 are met — not a long-term dual-default product story.

## Shared email behavior (S8D)

When Epic 69 sends email:

- SMTP uses `Notifications:Email` (`EmailNotificationOptions`) via `SmtpAttentionNotificationSender`
- Password resolves through `IEmailSmtpCredentialResolver` (secret ref supported)
- Message body uses `NotificationEmailMessageComposer` (HTML + plain multipart)
- Action link targets `/integrations/{integrationId}/schedule-diagnostics?incidentId={id}`
- Absolute links when `ShellUi:ManagementHostUrl` is configured; relative fallback otherwise (same as S8C)

Legacy `AttentionNotifications:ExpectedRunMissing` SMTP fields are merged into `Notifications:Email` by `EmailNotificationOptionsLegacyMergePostConfigure` when the new section is empty (S8B).

## Recommended configuration

**Production cutover (preferred):**

1. Configure `Notifications:Email` and `ShellUi:ManagementHostUrl`
2. Enable platform pipeline flags per [epic77 cutover runbook](../03-operations/epic77-expected-run-missing-notification-cutover-runbook.md)
3. Set `Notifications:UsePlatformPipeline:Enabled=true` to stop dual-send from Epic 69

**Pre-cutover / dual-path:**

- Legacy orchestrator sends aligned HTML email when incidents fire
- Platform path can be enabled in shadow mode (instances without delivery) for parity review

## Deferred

- Digest/journal, provider plugins, native template editor, normalized Priority
- Migrating Epic 69 reminder/escalation semantics into platform definitions (separate work)

## Tests

- `AttentionIncidentNotificationEmailComposerTests`
- `SmtpAttentionNotificationSenderTests`
- `AttentionIncidentNotificationOrchestratorTests` (HTML + absolute link)
- Existing S8B/S8C platform dispatch tests unchanged

See also: [S8C-html-email-absolute-operator-links.md](S8C-html-email-absolute-operator-links.md), [S10-notification-delivery-diagnostics-operator-transparency.md](S10-notification-delivery-diagnostics-operator-transparency.md)
