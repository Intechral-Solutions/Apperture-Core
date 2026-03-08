# ADR-0007: Separation of human auth, service auth, and relationship authorization

## Status

Proposed

## Context

Federation introduces risk of privilege bleed if user roles, service identities, and cross-instance trust are merged into one model. Apperture needs clear boundaries to preserve least privilege and auditability.

## Decision

Keep three distinct models:

1. Human user authentication/authorization (Entra users + local users, unified roles/permissions).
2. Service authentication (machine identity for API-to-API calls).
3. Relationship authorization (child-controlled capability/scope grants on active relationships).

No model may implicitly grant privileges in another model.

## Consequences

### Positive

- Reduces confused-deputy and privilege-escalation risk.
- Improves audit clarity and policy reasoning.
- Preserves local operator role semantics.

### Negative

- Requires explicit mapping layers and additional policy evaluation components.

### Neutral

- A `RemoteInstancePrincipal` is treated as a dedicated principal type, not a human role.
