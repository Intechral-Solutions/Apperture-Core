# ADR-0006: Entra-backed platform trust for instance authentication

## Status

Withdrawn (2026-10-03) by [ADR-0046](ADR-0046-external-organization-access-and-federation-withdrawal.md). Never implemented. Kept as history. Previously: Proposed.

## Context

Cross-instance management calls require strong machine-to-machine authentication. Apperture already uses Microsoft Entra for operator authentication and needs a trusted bootstrap for platform identities.

## Decision

Use Microsoft Entra app registration as the authentication anchor for instance-to-instance communication. Entra validates remote service identity, while Apperture relationship policy determines authorization. Prefer certificate-backed client assertions or federated credentials over long-lived shared secrets.

## Consequences

### Positive

- Reuses proven identity infrastructure for machine authentication.
- Supports credential lifecycle controls and short-lived token patterns.
- Aligns cloud and enterprise deployment expectations.

### Negative

- Introduces dependency on Entra configuration and tenant governance.
- Requires fallback strategy for disconnected or constrained environments.

### Neutral

- Entra authenticates identity only; relationship capabilities remain Apperture-owned policy.
