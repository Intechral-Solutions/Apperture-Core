# ADR-0008: Parent-child first on generalized relationship model

## Status

Proposed

## Context

The initial business requirement is parent-child management. Hardcoding only tree semantics in core domain types would limit future federation patterns and increase migration cost.

## Decision

Implement parent-child as the first supported relationship type, but design core relationship entities and policies as generalized topology-agnostic constructs (`RelationshipType`, lifecycle state, capability grants, scoped authorization, subscriptions, compatibility).

## Consequences

### Positive

- Delivers near-term value quickly.
- Avoids rework for observer, peer, provider/customer, and auditor scenarios.
- Keeps extension points explicit.

### Negative

- Initial model is broader than a pure one-off parent-child implementation.

### Neutral

- Feature flags and policy templates can phase in additional relationship types safely.
