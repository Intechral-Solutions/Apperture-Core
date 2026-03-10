# Next Stage: Platform Auth Readiness

This document describes how the current foundation is prepared for the next phase: **platform authentication** (operator auth, environment auth, control-plane API auth). It does not implement auth; it records intent and plug-in points.

## Principles

- Platform auth will build **on top of** the current boundaries. It must not collapse or conflate:
  - **Bootstrap** (SQL credential and config; first-use)
  - **Runtime Trust** (enrollment, runtime identity; distinct from generic secrets)
  - **Operational Secret Management** (ISecretStore, ISecretResolver; runtime secrets)
- Bootstrap and runtime trust remain distinct from generic operational secret storage. Auth will plug in at control-plane and host boundaries.

## Plug-in points for platform auth

1. **Control Plane API authentication** — Management API (workflows, modules, runs, enrollment) will require an authenticated operator or service identity. Auth middleware and policy should be added at the API boundary; existing orchestration-safe and workflow-start validation remain in place.
2. **CLI operator identity** — Bootstrap CLI and any future operator CLI commands may need to identify the caller (e.g. for audit or scoped operations). This is separate from bootstrap credential resolution and from runtime trust.
3. **Environment / auth context for bootstrap** — When implementing real bootstrap persistence or credential resolution, the host may need an auth context (who is performing the operation) for audit. This should not pull bootstrap into the generic secret store.

## What not to do

- Do not use the operational `ISecretStore` or `ISecretResolver` for bootstrap SQL credentials.
- Do not store platform auth tokens or operator credentials in the same model as workflow/runtime secrets without a clear design.
- Do not conflate runtime trust (enrollment, runtime identity) with operator/UI auth.

## Related

- [Bootstrap and Secrets Boundary](bootstrap-and-secrets-boundary.md)
- [Runtime Trust and Operational Authentication](runtime-trust-and-authentication.md)
