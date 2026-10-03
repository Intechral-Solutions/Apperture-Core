# ADR-0011: Delegated capability model for remote instance administration

## Status

Withdrawn (2026-10-03) by [ADR-0046](ADR-0046-external-organization-access-and-federation-withdrawal.md). Never implemented. Kept as history. Previously: Proposed.

## Context

Parent-child relationships must not imply unrestricted admin rights. Cross-instance operations need explicit least-privilege delegation aligned with module/resource authorization semantics.

## Decision

Use a delegated capability model for remote administration:

- deny by default
- explicit capability grants
- relationship scope constraints (global/module/resource/operation)
- optional time-bound constraints
- child-side enforcement on every request

Define capability tiers such as `Observe`, `Operate`, `DelegatedAdmin`, and restricted `BootstrapFullControl`.

## Consequences

### Positive

- Prevents over-broad remote authority.
- Aligns with existing permissions/resource declaration direction.
- Supports auditable, revocable delegated administration.

### Negative

- Requires policy definition tooling and clear operational governance.

### Neutral

- Capability names and templates can evolve without changing relationship fundamentals.
