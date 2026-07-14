# S8B — v1 Email Readiness & Notification Deep Links

Epic S8B makes built-in email delivery configurable via `Notifications:Email`, moves SMTP non-secrets into platform settings, resolves SMTP password via secret reference when configured, fixes live notification render context, and exposes operator action URLs in the personal inbox.

## Email configuration

### Primary section: `Notifications:Email`

`EmailNotificationOptions` binds to `Notifications:Email` with:

- `SmtpHost`, `SmtpPort`, `SmtpUseSsl`, `SmtpUser`
- `FromAddress`, `FromDisplayName`
- `SmtpPassword` — appsettings/env fallback only (not a platform setting)
- `SmtpPasswordRef` — opaque GUID reference to the SQL secret store (resolved at send time). Operators set this via the Platform Settings **SMTP password** sensitive editor; they do not paste GUIDs.

Platform pipeline SMTP (`SmtpEmailNotificationChannel`) and Epic 69 attention email (`SmtpAttentionNotificationSender`) both use `EmailNotificationOptions`.

### Legacy compatibility: `AttentionNotifications:ExpectedRunMissing`

When `Notifications:Email:SmtpHost` is empty, `EmailNotificationOptionsLegacyMergePostConfigure` copies SMTP fields from the legacy Epic 69 section. Deprecate legacy SMTP fields in ops docs over time; policy/recipient settings remain on the legacy section.

### Management host URL for templates

Set `ShellUi:ManagementHostUrl` to the operator-facing management host base URL (no trailing slash). Live instances populate `instance.baseUrl` in render context for absolute action links in email when templates use `{{instance.baseUrl}}` prefixes.

## Platform settings (SMTP)

| Key | Purpose |
|-----|---------|
| `Notifications.Email.SmtpHost` | SMTP hostname |
| `Notifications.Email.SmtpPort` | SMTP port |
| `Notifications.Email.SmtpUseSsl` | TLS flag |
| `Notifications.Email.SmtpUser` | Auth username |
| `Notifications.Email.FromAddress` | Sender address |
| `Notifications.Email.FromDisplayName` | Sender display name |
| `Notifications.Email.SmtpPasswordRef` | Sensitive setting — password via secret store; setting holds GUID only |

Password is **never** stored as plaintext in platform setting values. Configure it from `/config/platform-settings` (Notifications → SMTP password). After creating or replacing the secret reference, restart the Management host so the options overlay picks up the new ref. Diagnostics report configured/not configured only — never the password or GUID.

## Render context (live instances)

`NotificationLiveRenderContextResolver` supplies:

- `integration.displayName` from `IIntegrationRepository` when `integrationId` is present
- `instance.baseUrl` from `ShellUi:ManagementHostUrl`

Used by `NotificationInstanceCoordinator` when creating instances.

## Built-in `expected_run_missing` action URL

Template includes:

- `actionText`: "Review schedule diagnostics"
- `actionUrl`: `/integrations/{{integration.id}}/schedule-diagnostics`

## Personal inbox deep links

`PersonalNotificationInboxItemDto` exposes `RenderedActionUrl` and `RenderedActionText` from notification instances.

`PersonalNotificationUiRoutes.ResolveOpenTarget` prefers `RenderedActionUrl` over admin instance detail.

In-app routes remain relative. Email delivery (S8C) composes absolute links at send time using `instance.baseUrl` from render context. See [S8C-html-email-absolute-operator-links.md](S8C-html-email-absolute-operator-links.md).

## Deferred

- Provider plugin architecture
- Notification subscriptions and daily digest/journal
- Operational team inheritance
- Normalized Priority `0.0–1.0`
- Provider-native template editor
- HTML email sending

## Tests

- `tests/Apperture.UnitTests/Notifications/Delivery/EmailNotificationOptionsTests.cs`
- `tests/Apperture.UnitTests/Notifications/ExpectedRunMissingSeedTests.cs` (action URL)
- `tests/Apperture.Management.Ui.Shared.Tests/S8BPersonalNotificationDeepLinkTests.cs`
