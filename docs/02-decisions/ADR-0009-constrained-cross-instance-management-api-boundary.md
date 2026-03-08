# ADR-0009: Constrained cross-instance management API boundary

## Status

Proposed

## Context

Cross-instance collaboration can bypass security boundaries if parent instances gain direct access to child internals (databases, queues, secret stores, runtime internals).

## Decision

Require all parent-to-child interactions to go through child management API contracts. The API must be versioned, capability-gated, and auditable. Direct access to child internals outside exposed contracts is disallowed by design and deployment policy.

## Consequences

### Positive

- Preserves strict control boundary and enforcement point on child instance.
- Simplifies audit and incident investigation.
- Supports protocol evolution through explicit versioning.

### Negative

- Requires richer API surface and lifecycle governance.
- May increase implementation effort versus direct internal integration.

### Neutral

- Gateway and client abstractions become first-class architecture components.
