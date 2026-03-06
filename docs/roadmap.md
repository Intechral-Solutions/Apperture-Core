# Roadmap

## Secrets and Key Management Phases

### Phase 1: DB Secret Store + Protector + Resolver (interfaces first)

Scope:

- Define and stabilize secrets abstractions (`ISecretStore`, `ISecretResolver`, `ISecretProtector`).
- Implement metadata and version lifecycle contracts (`Pending`, `Active`, `Disabled`, `Revoked`) as compile-safe API surface.
- Provide placeholder implementations for DB store, resolver, and protector.
- Enforce `SecretRef` usage in contracts and documentation.

Acceptance criteria:

- All consumers use `SecretRef` contracts rather than raw secret string fields.
- Control Plane does not send decrypted secret payloads to Worker by default.
- Worker-facing resolver contract supports direct provider resolution or worker-only unwrap path.
- Version activation and revocation APIs exist and are auditable by contract.
- Solution compiles with placeholder providers and no production provider integration.

### Phase 2: Azure Key Vault provider

Scope:

- Add Azure Key Vault-backed secret store/resolution implementation behind existing interfaces.
- Support metadata mapping between internal `SecretId` and Key Vault secret/version references.
- Use workload identity/managed identity for worker-side resolution.

Acceptance criteria:

- Resolver can fetch active versions from Azure Key Vault without control-plane plaintext relay.
- Secret lifecycle operations remain consistent with `ISecretStore` semantics.
- Audit hooks capture create/version/activate/resolve outcomes without secret material.
- Redaction policy is verified for provider and resolver logs.

### Phase 3: HashiCorp Vault KV v2 provider

Scope:

- Add Vault KV v2 provider behind the same store/resolver abstractions.
- Support path/version semantics and lifecycle mapping to internal state model.
- Support environment-specific authentication plumbing without changing consumer contracts.

Acceptance criteria:

- Resolver supports KV v2 reads for active versions via `SecretRef`.
- Store semantics (`AddVersion`, `ActivateVersion`, `Disable`, `RevokeVersion`) are preserved.
- Audit records include Vault operation metadata and outcomes without sensitive payloads.
- Existing connector/module usage remains unchanged.

### Phase 4: Vault Transit and dynamic secrets

Scope:

- Add Transit-backed protect/unprotect flows for envelope encryption scenarios.
- Add dynamic secret/lease-aware resolution capabilities.
- Support lease metadata propagation through `ResolvedSecret` fields.

Acceptance criteria:

- Resolver returns lease metadata (`ExpiresAt`, `LeaseId`) when provider issues leased credentials.
- Rotation and revocation workflows handle lease-bound credentials safely.
- Transit integration enables key operations without exposing key material in app storage.
- Operational runbooks include rotation, revoke, and incident-response paths for leased secrets.
