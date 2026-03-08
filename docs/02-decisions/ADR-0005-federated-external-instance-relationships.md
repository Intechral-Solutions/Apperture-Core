# ADR-0005: Federated external instance relationships

## Status

Proposed

## Context

Apperture needs cross-organization collaboration between independently operated instances without flattening them into shared tenancy. Existing decisions require strict instance boundaries and auditable control-plane interactions.

## Decision

Adopt a federated relationship model where cross-instance collaboration is represented as explicit `InstanceRelationship` records with lifecycle states and delegated capabilities. Parent-child is the first business scenario, implemented on top of a generalized relationship domain.

## Consequences

### Positive

- Preserves single-tenant instance isolation while enabling collaboration.
- Makes trust explicit, auditable, and revocable.
- Enables future relationship topologies without redesigning core contracts.

### Negative

- Adds relationship lifecycle and policy complexity to control-plane design.
- Requires additional API contracts and operational tooling.

### Neutral

- Parent-child is delivered first, but not hardcoded as the only shape.
