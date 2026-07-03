# S9 — Notification Subscriptions & Operator Delivery Preferences v1

Epic S9 adds operator self-service email preferences for platform built-in notifications using the existing Epic 77 subscription schema.

## Subscription model (v1)

| Aspect | Behavior |
|--------|----------|
| Attachment | Platform user (`principal`) + notification definition |
| Channel controlled | Email delivery only (via existing recipient resolution) |
| Self-service row | `NotificationSubscriptions` with `Origin = userSelfSubscribed`, platform-wide (`IntegrationId = null`) |
| Email opt-out | Global `NotificationSubscriptionUserOverrides` row (`OperationalTeamId = null`) |
| Default | No self-subscription → no email from self-service path |
| Admin/team | Existing admin assignments and implicit attention routing unchanged |

Platform/in-app inbox materialization continues from deliverable recipient resolution (principals with linked users).

## Operator APIs

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `api/me/notification-subscriptions` | List subscribable definitions and email state |
| PUT | `api/me/notification-subscriptions/{definitionId}` | Enable/disable email (`{ "emailEnabled": true/false }`) |

Unknown or non-subscribable definitions return **404**.

## UI

`/me/notifications` includes an **Email preferences** section with per-definition email toggles.

Account deferred-features flag `NotificationSubscriptionsDeferred` is **false**; preferences live on My notifications.

## Delivery gating

Email delivery is gated at **instance creation** by `SqlNotificationRecipientResolver`:

- Subscribed (self or admin) + not opted out → deliverable → outbox row
- Global opt-out → skipped with `userOptOut` (documented as email opt-out)
- No subscription → not in deliverable set (unless admin/team/implicit routing)

Dispatch does not re-check subscriptions (unchanged from Epic 77).

## Diagnostics

Skipped recipients appear in instance `RecipientSnapshotJson` with reasons such as `userOptOut`, `deliveryModeMuted`, `missingEmail`. Constants `emailOptedOut` and `noEmailSubscription` are reserved for future explicit mapping.

## Deferred

- Daily digest/journal
- Operational team subscriptions (admin-only today)
- Provider plugin model
- Provider-native template editor
- Normalized Priority `0.0–1.0`

## Tests

- `tests/Apperture.UnitTests/Notifications/Subscriptions/OperatorNotificationSubscriptionServiceTests.cs`
- Existing subscription/resolver/coordinator tests updated as needed

See also: [S8B](S8B-v1-email-readiness-notification-deep-links.md), [S8C](S8C-html-email-absolute-operator-links.md)
