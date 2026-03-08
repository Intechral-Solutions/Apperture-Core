# ADR-0010: Signed reliable child-to-parent notifications

## Status

Proposed

## Context

Parent-child collaboration needs timely operational/security signals from child instances. Naive webhook-only delivery is insufficient for production reliability and audit requirements.

## Decision

Adopt a formal notification model with:

- explicit event taxonomy and severity
- signed outbound deliveries
- at-least-once delivery with retries/backoff
- idempotency keys and replay protection
- durable outbox/event log support
- reconciliation APIs for missed event recovery

## Consequences

### Positive

- Improves reliability and trustworthiness of remote signals.
- Strengthens security against spoofing and replay.
- Enables auditable end-to-end delivery tracking.

### Negative

- Adds event delivery and reconciliation complexity.
- Requires operational monitoring for delivery health.

### Neutral

- Notification transport may vary by host, while semantics remain contract-stable.
