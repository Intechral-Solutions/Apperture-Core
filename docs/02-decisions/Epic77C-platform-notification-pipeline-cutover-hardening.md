# Epic 77C — Platform Notification Pipeline Cutover Hardening

Epic 77C adds operator-facing cutover status and reusable preflight checks for moving expected-run-missing notifications from Epic 69 to the platform pipeline.

## Cutover status API

`GET /api/notification-diagnostics/cutover-status`

Returns:

- Active delivery path (`legacy`, `dual`, `platform`, `platform_no_delivery`)
- Platform pipeline and legacy path state
- Operating mode labels (shadow, dry-run, dual-send risk, etc.)
- Expected-run-missing built-in definition exists/enabled
- Email transport readiness (`Notifications:Email` — no secrets)
- `ShellUi:ManagementHostUrl` configured
- Preflight check list with guidance
- `CriticalPreflightPassed` heuristic (core flags + email fields)

## UI surfaces

- **Config → Notifications → Delivery** — cutover status card
- **Config → Notifications → Diagnostics** — same card at top
- **Integrations → {id} → Notifications** — existing per-integration parity/sign-off (unchanged)

## Preflight helper

`NotificationPipelineCutoverPreflight` in `Apperture.Core.Application.Notifications.Cutover` centralizes checks. `PlatformNotificationCutoverGate` moved to Core.Application; ControlPlane.Security delegates to it.

## Defaults unchanged

`Notifications:UsePlatformPipeline:Enabled` remains **false** by default. Legacy Epic 69 path stays active until operators enable cutover manually.

## Related

- [epic77 cutover runbook](../03-operations/epic77-expected-run-missing-notification-cutover-runbook.md)
- [S8D legacy email alignment](S8D-epic69-email-parity-legacy-attention-alignment.md)
- [S9 subscriptions](S9-notification-subscriptions-operator-delivery-preferences-v1.md)
- [S10 diagnostics](S10-notification-delivery-diagnostics-operator-transparency.md)

## Tests

- `NotificationPipelineCutoverPreflightTests`
- `PlatformPipelineCutoverGuardTests` (gate behavior)
- `Epic77CCutoverStatusUiTests`
