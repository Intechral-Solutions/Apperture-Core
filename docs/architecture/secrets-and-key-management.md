# Secrets and Key Management

## Problem statement

Apperture modules and workflows need credentials, keys, and certificates without coupling runtime execution to a specific secret backend. We need a portable contract that supports on-prem storage first, Azure Key Vault in cloud deployments, and HashiCorp Vault later, while preventing unsafe plaintext flows across control plane and data plane boundaries.

This document defines the architecture contract before provider implementation.

## Threat model

- **Primary risk:** accidental disclosure of plaintext secrets through API payloads, logs, traces, or persisted workflow state.
- **Primary boundary:** control plane manages references and metadata; worker/data plane resolves secret material at execution time.
- **Identity risk:** control plane identity must not automatically gain decryption capability for worker-resolved payloads.
- **Operational risk:** stale or revoked versions continue to be used without explicit activation and auditability.
- **Future risk:** dynamic or leased credentials (Vault) require expiry-aware contracts from the start.

## Non-goals

- Implementing production-ready Azure Key Vault integration in this phase.
- Implementing production-ready HashiCorp Vault KV v2, Transit, or dynamic secrets in this phase.
- Defining final RBAC policy model for secret access decisions.
- Enabling arbitrary plaintext secret transport from control plane to worker.

## Responsibilities by plane

## Control Plane

- Accepts and validates secret metadata and lifecycle commands.
- Stores `SecretRef` and descriptor metadata in domain models and configuration records.
- Provides administrative operations for create/version/activate/disable/revoke.
- Emits audit events for control operations.
- Does **not** send decrypted secret values to worker by default.

## Data Plane (Worker)

- Resolves `SecretRef` at execution time via `ISecretResolver`.
- Accesses provider backends directly where possible, using worker identity.
- Optionally unwraps protected payloads through `ISecretProtector` using worker-only KEK access.
- Avoids writing resolved material to logs or long-lived workflow state.

## Core abstractions

- `ISecretStore`
  - Metadata and lifecycle boundary.
  - Manages secret descriptors, version state, activation, disable/revoke, and audit retrieval.
- `ISecretResolver`
  - Runtime lookup boundary used by modules/connectors.
  - Converts a `SecretRef` into `ResolvedSecret`.
  - Encapsulates provider selection and resolution strategy.
- `ISecretProtector`
  - Envelope encryption helper for providers that store protected payloads in DB/object storage.
  - Ensures protect/unprotect semantics remain explicit and testable.

This split keeps store concerns (governance/audit) separate from runtime material resolution and cryptographic protection.

## Secret model and reference policy

- Secrets are passed and persisted as `SecretRef`.
- Raw secret values are confined to provider/protector boundaries and ephemeral runtime memory.
- `ResolvedSecret` supports optional lease metadata (`ExpiresAt`, `LeaseId`) for future Vault dynamic secrets.
- `SecretMaterialType` covers:
  - `OpaqueString`
  - `Json`
  - `Bytes`
  - `X509Certificate`

## Version lifecycle

State machine per secret version:

- `Pending` -> created but not active for resolution.
- `Active` -> current version returned by default resolution.
- `Disabled` -> temporarily ineligible for resolution.
- `Revoked` -> permanently invalidated; cannot be reactivated.

Rules:

- Only one active version per logical secret.
- Activation is explicit (`ActivateVersion`) and auditable.
- Resolver should reject disabled/revoked versions.

## Rotation workflow

1. Create a new version (`AddVersion`) in `Pending`.
2. Validate and test in a non-production execution path.
3. Activate new version (`ActivateVersion`) atomically.
4. Keep prior version available for rollback window if policy allows.
5. Disable or revoke superseded version according to compliance and incident posture.

Rotation metadata and activation actor must be auditable.

## Audit expectations

The store/resolver pipeline should emit structured events for:

- Secret created.
- Version added.
- Version activated.
- Secret disabled.
- Version revoked.
- Resolution attempt (success/failure, no material).
- Protector operations (protect/unprotect) as metadata-only events.

Audit records should include timestamp, actor identity, operation, target `SecretId`/version, and outcome. Secret material must never be included.

## Provider matrix

| Provider | Storage pattern | Resolve path | Notes |
| --- | --- | --- | --- |
| On-prem DB + Protector | Secret payload in DB, encrypted/protected | Worker reads + unprotects via worker identity key material | Initial implementation path. `ISecretProtector` may wrap cert-based or DPAPI-backed mechanics. |
| Azure Key Vault | Metadata and references in control plane store; secret material in Key Vault | Worker resolves directly from Key Vault using managed identity or app identity | Prefer direct worker resolution over control-plane plaintext relay. |
| HashiCorp Vault KV v2 | Metadata + Vault path/version mapping | Worker resolves via Vault API | Supports versioned key-value retrieval. |
| HashiCorp Vault Transit (future) | Ciphertext in app store, key ops in Vault Transit | Worker unwrap/sign/encrypt through Vault Transit | Keeps key material external to app. |
| Vault dynamic secrets (future) | Lease-bound credentials generated on demand | Resolver returns `ExpiresAt` and `LeaseId` | Requires renewal/revocation workflow integration. |

## Migration strategy

From DB-encrypted secrets to KV/Vault:

1. Introduce provider-agnostic `SecretRef` in module/workflow configuration contracts.
2. Backfill descriptors for existing DB-encrypted entries and assign stable `SecretId`.
3. Add provider-specific reference metadata without changing consumer contracts.
4. Migrate values per secret to external backend and mark active version there.
5. Switch resolver routing by secret/provider policy.
6. Disable old versions in DB store after verification window.

Consumers remain unchanged because they resolve via `ISecretResolver`.

## Logging redaction requirements

- Never log raw secret values, decrypted payloads, certificates, or byte arrays containing secret material.
- Treat fields named `password`, `secret`, `token`, `key`, `privateKey`, `connectionString` as redact-by-default.
- Log `SecretRef` identifiers, provider name, and operation outcomes only.
- For future Serilog integration, add a dedicated destructuring/masking policy and enforce it in shared logging bootstrap.

## Minimal integration touchpoints

The following surfaces should carry `SecretRef` instead of plaintext secret values:

- `src/Apperture.Runtime/Apperture.Runtime.Abstractions/StartWorkflowRequest.cs`
- `src/Apperture.Runtime/Apperture.Runtime.Abstractions/IWorkflowExecutor.cs`
- `src/Apperture.ModuleSdk/Apperture.ModuleSdk.Abstractions/ModuleManifest.cs`
- `docs/05-modules/module-manifest-spec.md`

In current architecture, these are schema and contract touchpoints; provider behavior remains behind interfaces.

## How to use (pseudo-code)

```csharp
public sealed class ConnectorStep
{
    private readonly ISecretResolver _secretResolver;
    private readonly ILogger<ConnectorStep> _logger;

    public ConnectorStep(ISecretResolver secretResolver, ILogger<ConnectorStep> logger)
    {
        _secretResolver = secretResolver;
        _logger = logger;
    }

    public async Task ExecuteAsync(SecretRef apiKeyRef, CancellationToken cancellationToken)
    {
        var resolved = await _secretResolver.ResolveAsync(apiKeyRef, cancellationToken);

        // Use resolved material in-memory only. Never log secret payload.
        _logger.LogInformation(
            "Resolved secret reference {SecretRef} with material type {MaterialType}.",
            apiKeyRef.Value,
            resolved.Material.Type);

        // ... call downstream API using resolved.Material ...
    }
}
```

The example intentionally logs only reference and type metadata, not secret content.
