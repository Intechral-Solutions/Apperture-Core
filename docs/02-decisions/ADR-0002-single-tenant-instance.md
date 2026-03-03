# ADR-0002: Single-tenant instance deployment boundary

## Status

Accepted

## Context

We need a deployment boundary that simplifies isolation and compliance. Multi-tenant architectures add complexity; we want to start with a simpler model.

## Decision

Each deployment is a **single-tenant Instance**. One customer per deployment. Everything is auditable per instance. Tables and models are designed so adding `TenantId` later would be straightforward if multi-tenancy is needed.

## Consequences

### Positive

- Simplified isolation
- Clear audit scope
- Easier compliance story

### Negative

- Multiple deployments for multiple customers (operational overhead)

### Neutral

- Future multi-tenancy would require schema and logic changes but is not precluded
