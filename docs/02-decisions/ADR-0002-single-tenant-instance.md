# ADR-0002: Single-tenant instance deployment boundary

## Status

Accepted

## Terminology note (later)

The deployment boundary described here is now named **Environment** in the platform (see [foundational-layers.md](../01-architecture/foundational-layers.md) and [.ai/memory/glossary.md](../../.ai/memory/glossary.md)). "Instance" is reserved for qualified uses only (e.g. **Module Instance**, workflow instance). The decision below is unchanged: one customer per deployment, auditable per deployment boundary.

## Context

We need a deployment boundary that simplifies isolation and compliance. Multi-tenant architectures add complexity; we want to start with a simpler model.

## Decision

Each deployment is a **single-tenant Instance** (now also called **Environment**). One customer per deployment. Everything is auditable per instance. Tables and models are designed so adding `TenantId` later would be straightforward if multi-tenancy is needed.

## Consequences

### Positive

- Simplified isolation
- Clear audit scope
- Easier compliance story

### Negative

- Multiple deployments for multiple customers (operational overhead)

### Neutral

- Future multi-tenancy would require schema and logic changes but is not precluded
