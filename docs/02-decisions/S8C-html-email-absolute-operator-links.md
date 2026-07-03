# S8C — HTML Email & Absolute Operator Links

Epic S8C makes built-in notification emails usable from email clients by composing absolute action links and sending multipart plain-text + HTML messages.

## Base URL requirement

Set `ShellUi:ManagementHostUrl` to the operator-facing management host (no trailing slash required). Live notification instances store this as `instance.baseUrl` in render context.

At email send time, relative `RenderedActionUrl` values (for example `/integrations/{id}/schedule-diagnostics`) are combined with `instance.baseUrl` to produce absolute links in email bodies.

If `instance.baseUrl` is missing or invalid, email still sends with the relative action path in plain text and HTML. Configure the management host URL for working links in external email clients.

Personal inbox navigation (S8B) continues to use relative `RenderedActionUrl` values for in-app routing.

## Email output behavior

`NotificationDispatchProcessor` composes email via `NotificationEmailMessageComposer` before SMTP dispatch:

- **Plain text:** rendered body plus an action line when `RenderedActionUrl` is present
- **HTML:** simple v1 layout (title, summary, body, action button, integration context, timestamp, footer)
- **SMTP:** `NotificationSmtpMessageBuilder` sends `multipart/alternative` when HTML is available; plain-text-only when not

Epic 69 (`AttentionIncidentNotificationOrchestrator`) remains plain-text unless migrated to the platform pipeline. Prefer `Notifications:UsePlatformPipeline` for built-in expected-run-missing delivery.

## Link normalization

`NotificationEmailActionLinkNormalizer`:

- preserves already-absolute `http`/`https` URLs
- joins relative paths to a valid base URL without double slashes
- leaves relative paths unchanged when base URL is missing or invalid

## Deferred

- Notification subscriptions and operator delivery preferences
- Daily digest/journal
- Operational team inheritance
- Normalized Priority `0.0–1.0`
- Provider-native template editor
- Provider plugin model

## Tests

- `tests/Apperture.UnitTests/Notifications/Delivery/NotificationEmailActionLinkNormalizerTests.cs`
- `tests/Apperture.UnitTests/Notifications/Delivery/NotificationEmailMessageComposerTests.cs`
- `tests/Apperture.UnitTests/Notifications/Delivery/NotificationSmtpMessageBuilderTests.cs`
- `tests/Apperture.UnitTests/Notifications/Delivery/NotificationDispatchProcessorTests.cs` (absolute link + HTML)

See also: [S8B-v1-email-readiness-notification-deep-links.md](S8B-v1-email-readiness-notification-deep-links.md)
