# Bootstrap and Secrets Boundary

This document defines the boundary between bootstrap configuration, operational secret management, and runtime trust.

## Critical Separation

**Bootstrap SQL credentials must NOT depend on the SQL-backed operational secret store they enable.**

- Bootstrap is first-use: the platform needs a connection string to start and create schema.
- The operational secret store lives in the same SQL database but is initialized only after bootstrap succeeds.
- Bootstrap credentials come from config, environment, or a dedicated credential source—never from `ISecretStore` or `ISecretResolver`.

## Bootstrap Configuration

Bootstrap owns:

- Bootstrap SQL connection profile (`IBootstrapConnectionProfile`)
- Auth mode (Integrated vs SQL auth)
- Credential source (config, environment; Key Vault reference in Phase 2+)
- Validation-before-commit (test connection on startup)
- Secure post-deployment update support
- Recovery repair path
- Bootstrap audit

Bootstrap does **not** own:

- Operational secret storage
- Runtime identity
- Resolving `SecretRef`

## Operational Secret Management

Operational secrets are managed by `Apperture.Core.Security.Secrets`:

- `ISecretStore` — Metadata, version lifecycle, audit
- `ISecretResolver` — Runtime resolution by `SecretRef`
- `ISecretProtector` — Envelope encryption for at-rest payloads

Secrets are stored in the shared persistence schema (`SecretDescriptors`, `SecretVersions`) but the subsystem is distinct from bootstrap. See [secrets-and-key-management.md](secrets-and-key-management.md).

## Dependency Direction

```
Bootstrap → Persistence Foundation
Operational Secrets → Persistence Foundation
Runtime Trust → Operational Secrets (for enrollment token storage) OR Persistence Foundation (dedicated enrollment table)
```

Bootstrap never depends on Operational Secrets or Runtime Trust. Operational Secrets never depends on Bootstrap for SQL credentials.

## Do Not

- Put bootstrap SQL credentials in the operational secret store or resolve them via `ISecretResolver` at startup.
- Use one credential for bootstrap, operational auth, and DEK protection—keep them separate.
- Store encryption keys (DEK, root) in appsettings or config.

## Related

- [Persistence Foundation](persistence-foundation.md)
- [Secrets and key management](secrets-and-key-management.md)
- [Foundational layers](foundational-layers.md)
