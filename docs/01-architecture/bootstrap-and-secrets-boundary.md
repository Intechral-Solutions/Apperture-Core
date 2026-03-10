# Bootstrap and Secrets Boundary

This document defines the boundary between bootstrap configuration, operational secret management, and runtime trust.

## Critical Separation

**Bootstrap SQL credentials must NOT depend on the SQL-backed operational secret store they enable.**

- Bootstrap is first-use: the platform needs a connection string to start and create schema.
- The operational secret store lives in the same SQL database but is initialized only after bootstrap succeeds.
- Bootstrap credentials come from config, environment, or a dedicated credential source—never from `ISecretStore` or `ISecretResolver`.

## Dual-Path Credential Resolution

`BootstrapConnectionProfile` resolves credentials in two ways:

### Production path (preferred)

1. Configure `SqlServer.CredentialReference` (SourceType, Reference) in config—no plaintext.
2. Register a non-stub `IBootstrapCredentialResolver` (from `Apperture.Bootstrap.Windows`, `Apperture.Bootstrap.Azure`, or custom).
3. Resolver returns connection string from Key Vault, Managed Identity, certificate, etc.

### Development-only fallback

1. In Development environment only: `SqlServer.User`/`Password` or `ConnectionStrings.DefaultConnection` allowed.
2. `BootstrapConnectionProfile` logs a security warning when config-based credentials are used.
3. In Production, config-based credentials throw—no fallback.

**Do not store plaintext SQL passwords in config for Production.** Use `BootstrapCredentialReference` plus `IBootstrapCredentialResolver`. Transient update requests (`BootstrapUpdateRequest`) may carry sensitive payloads for CLI/operator flows; use and discard—never persist.

## Bootstrap Configuration

Bootstrap owns:

- Bootstrap SQL connection profile (`IBootstrapConnectionProfile`)
- Auth mode (`BootstrapAuthMode`: ManagedIdentity, WindowsIntegrated, SqlPassword, CertificateBased, Development)
- Credential source (`BootstrapCredentialSourceType`: ManagedIdentity, WindowsIntegrated, KeyVaultReference, DpapiProtectedLocal, CertificateBackedLocal, DeploymentInjected)
- Validation-before-commit (test connection on startup)
- Secure post-deployment update support
- Recovery repair path
- Bootstrap audit

### Bootstrap Abstractions

| Interface | Responsibility |
|-----------|----------------|
| `IBootstrapConnectionProfile` | Validated connection string; `ValidateAsync` |
| `IBootstrapConfigurationStore` | Persist/load non-secret config (`BootstrapConfigurationDocument`) |
| `IBootstrapCredentialResolver` | Resolve connection string from `BootstrapCredentialReference`; must not use ISecretStore |
| `IBootstrapCredentialManager` | Orchestrate update, validate, rotate |
| `ISqlConnectionBootstrapProfileManager` | Manage SQL profile |
| `IBootstrapConfigurationValidator` | Validate config before commit |
| `IBootstrapRecoveryAuthorizer` | Authorize recovery actions (break-glass only) |
| `IBootstrapAuditWriter` | Write bootstrap change audit records |
| `IBootstrapEnvironmentStrategy` | Environment-specific credential resolution (Windows vs Azure) |

### Models (non-secret)

- `SqlConnectionBootstrapProfile` — Server, Database, AuthMode; no User/Password
- `BootstrapCredentialReference` — SourceType, Reference; no raw secret
- `BootstrapConfigurationDocument` — Profile + CredentialReference + Version
- `BootstrapUpdateRequest` — Transient; may carry `SensitivePayload`; do not persist
- `BootstrapValidationResult`, `BootstrapRecoveryRequest`, `BootstrapRecoveryAuthorizationResult`, `BootstrapChangeAuditRecord`

Bootstrap does **not** own:

- Operational secret storage
- Runtime identity
- Resolving `SecretRef`

Runtime identity and enrollment tokens are part of the **Runtime Trust** subsystem; see [runtime-trust-and-authentication.md](runtime-trust-and-authentication.md).

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
Runtime Trust → Persistence Foundation (dedicated enrollment tables for enrollment tokens and runtime identities)
```

Bootstrap never depends on Operational Secrets or Runtime Trust. Operational Secrets never depends on Bootstrap for SQL credentials.

## Do Not

- Put bootstrap SQL credentials in the operational secret store or resolve them via `ISecretResolver` at startup.
- Use one credential for bootstrap, operational auth, and DEK protection—keep them separate.
- Store encryption keys (DEK, root) in appsettings or config.

## Related

- [Bootstrap CLI Invocation](bootstrap-cli-invocation.md) — CLI integration, invocation model, deferred implementation
- [Persistence Foundation](persistence-foundation.md)
- [Secrets and key management](secrets-and-key-management.md)
- [Foundational layers](foundational-layers.md)
- [Tracked issues (Phase 1)](../02-decisions/tracked-issues-bootstrap-secrets-phase1.md)
