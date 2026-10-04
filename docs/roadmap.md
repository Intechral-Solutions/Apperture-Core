# Roadmap (historical phase notes)

> **This file is not the roadmap.** The authoritative sequencing document is
> [`00-overview/pre-release-product-roadmap.md`](00-overview/pre-release-product-roadmap.md), and the RC1 program contract is
> [`01-architecture/rc1-program-gate.md`](01-architecture/rc1-program-gate.md). This file keeps two older phase outlines
> for provenance.

## Federated External Instance Collaboration Phases

> **Withdrawn (2026-10-03).** Federation is withdrawn from the active roadmap by
> [ADR-0046](02-decisions/ADR-0046-external-organization-access-and-federation-withdrawal.md). None of Phases 1 to 4 was
> implemented and none is planned. The text below is history.

### Phase 1: Parent-child relationship MVP (observe-first)

Scope:

- Add generalized external relationship domain records (`ExternalInstance`, `InstanceRelationship`, lifecycle state).
- Implement parent-child handshake with Entra-backed machine authentication bootstrap.
- Add explicit child-side approval and delegated observe-only capability grants.
- Expose constrained read-only cross-instance endpoints (metadata, health summary, module inventory).
- Emit basic upward notifications for failures/degraded health with relationship audit.

Acceptance criteria:

- Child can activate/suspend/revoke a parent relationship with auditable state transitions.
- Parent cannot perform operations unless relationship is `Active` and capability grants allow it.
- Parent access is limited to management API boundary; no direct child internal access paths.
- Failure and degraded-health notifications are delivered with correlation IDs and auditable delivery outcomes.

### Phase 2: Delegated operations and reliable subscriptions

Scope:

- Add delegated action scopes for approved remote operations.
- Add notification subscription management (event class + severity filtering).
- Implement retries, idempotency keys, replay protection, and delivery backoff for notifications.
- Implement relationship credential rotation with overlap windows and expiry policy.
- Expand health/workflow APIs for bounded remote operations (for example retry/acknowledge).

Acceptance criteria:

- Remote operations enforce deny-by-default policy with module/resource/operation scopes.
- Subscription filters are enforced and can be enabled/disabled without history loss.
- Notification delivery is at-least-once with duplicate-safe processing.
- Credential rotation does not require relationship teardown and is fully auditable.

### Phase 3: Generalized relationship expansion

Scope:

- Support additional relationship types (observer, peer, provider/customer, auditor).
- Add module/resource-granular delegation patterns and policy templates.
- Add event reconciliation APIs (watermark-based replay/history).
- Harden compatibility/version negotiation and contract downgrade behavior.
- Add policy controls for time-bound grants and conditional constraints.

Acceptance criteria:

- Relationship model supports non-parent-child types without schema redesign.
- Compatibility checks prevent unsupported cross-version operation calls.
- Reconciliation APIs allow parent recovery from missed notifications.
- Policy templates reduce manual configuration while preserving explicit grants.

### Phase 4: Advanced federation patterns

Scope:

- Support advanced topologies such as multi-parent observers where business-approved.
- Add external compliance/auditor relationship profiles with strict read-only controls.
- Add richer cross-instance orchestration patterns built only on bounded management contracts.
- Add governance tooling for large relationship graphs and lifecycle reporting.

Acceptance criteria:

- Advanced topology support does not bypass child authorization enforcement.
- Auditor/compliance profiles are strongly constrained and fully auditable.
- Federated orchestration patterns remain API-bound and do not introduce direct plane bypass.

## Secrets and Key Management Phases

> **Status (2026-10-03).** Not part of the RC1 program. Phase 1 is delivered and exceeded: the SQL secret store and
> resolver are real, with a DPAPI protector on Windows. Phase 2 is partial: a Key Vault resolver exists for the Azure
> runtime host; the Key Vault store is a stub. Phases 3 and 4 are not started and have no owner. A non-DPAPI protector is
> a prerequisite for any non-Windows production host, which is outside RC1 support.

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
