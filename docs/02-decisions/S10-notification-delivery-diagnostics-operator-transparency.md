# S10 — Notification Delivery Diagnostics & Operator Transparency

Epic S10 surfaces why notification email was sent, skipped, suppressed, or failed — without full analytics.

## Where to look

| Surface | Route | What it shows |
|---------|-------|----------------|
| Notification instance detail | `/config/notifications/instances/{id}` | Recipient snapshot (considered / deliverable / skipped with reasons), suppression label, outbox rows with SMTP guidance |
| Delivery health | `/config/notifications/delivery` | Failed / pending / succeeded outbox rows with error guidance |
| Definition / integration preview | Definition detail, integration notifications | Live recipient resolution preview with skip labels |
| Personal preferences | `/me/notifications` | Email opt-in help (email vs in-app) |

Recipient skip reasons are stored on the instance `RecipientSnapshotJson` at creation time. Outbox `LastError` reflects dispatch/SMTP failures.

## Skipped recipient reasons

| Code | Operator label | Typical fix |
|------|----------------|-------------|
| `userOptOut` | User opted out of email for this notification | User reviews My notifications or admin removes global opt-out |
| `emailOptedOut` | Email delivery is disabled by preference | User enables email under My notifications |
| `noEmailSubscription` | Email subscription is not enabled | User enables email under My notifications, or admin adds subscription |
| `missingEmail` | Recipient does not have an email address | Add email to user/contact |
| `deliveryModeMuted` | Subscription delivery mode is muted | Change subscription delivery mode on definition |
| `deliveryModeDigestOnly` | Digest-only (immediate email deferred) | Change mode; digest/journal not available yet |
| `contactDisabled` | Managed contact is disabled | Re-enable contact |
| `teamDisabled` / `teamEmpty` | Team routing issue | Fix operational team |
| `duplicateEmail` | Duplicate address | Informational — already deliverable elsewhere |
| *(unknown)* | Skipped for an unrecognized reason | Review subscriptions and routing |

`userOptOut` is used by the resolver for global email opt-out (S9). Per-definition disable may appear as `emailOptedOut` when recorded separately.

## Instance suppression (no email at all)

When the entire instance is suppressed (status `suppressed`), check the suppression reason on instance detail:

- `no_route_configured` — no deliverable recipients
- `policy_disabled` — attention routing policy off
- `duplicate_activation_suppressed` — duplicate activation
- `smtp_not_configured` / `smtp_from_not_configured` — platform SMTP settings incomplete

## Troubleshooting missing email

1. **Was an instance created?** Check integration notifications or instance list for the event/incident.
2. **Instance suppressed?** Read suppression label on instance detail.
3. **Recipient skipped?** Open instance detail → Recipient snapshot → Skipped section for label and guidance.
4. **Outbox failed?** Check Last error and Guidance columns; verify Platform settings → Notifications.Email (host, port, from, credentials ref). Secrets are never shown in UI.
5. **Delivery disabled globally?** Delivery health page shows platform pipeline flags.
6. **User self-service?** User must enable email under My notifications for subscribable platform definitions (S9), unless admin/team/implicit routing applies.

## Deferred (out of S10)

- Daily digest / journal delivery
- Team self-service subscriptions
- Provider plugins and native template editor
- Normalized Priority field
- Full notification analytics / reporting

## Tests

- `NotificationRecipientSkipReasonLabelsTests` — skip reason mapping
- `NotificationDeliveryErrorGuidanceTests` — SMTP error guidance (no secrets)
- `S10NotificationDiagnosticsUiTests` — diagnostics UI wiring
- Existing S8B/S8C email and S9 subscription tests unchanged
