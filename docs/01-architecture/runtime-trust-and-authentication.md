# Runtime Trust and Operational Authentication

## Overview

Runtime Trust covers:

- Enrollment invitations and short-lived enrollment tokens
- Runtime identity registration
- Runtime-to-control-plane authentication
- Credential rollover/renewal paths
- Host-appropriate trust models for Windows and Azure

Enrollment/bootstrap trust artifacts are **not** ordinary operational secrets; they live in dedicated trust tables, not in the operational secret store.

## Enrollment flow

1. Control Plane creates an enrollment invitation via `IRuntimeEnrollmentService.CreateInvitationAsync(...)`.
2. An `EnrollmentTokenEntity` is stored in `EnrollmentTokens` with runtime id, expiry, and `Used = false`.
3. The runtime calls the enrollment endpoint with the token and a `RuntimeIdentityRegistration` payload.
4. The enrollment service validates the token (short-lived, one-time), marks it used, and persists a `RuntimeIdentityEntity`.
5. Operational authentication uses the registered runtime identity; the enrollment token is not reused.

## Models

- `EnrollmentInvitation` — short-lived invitation returned to callers; carries token and enrollment URL.
- `EnrollmentTokenRecord` — metadata view of stored enrollment tokens (no raw token).
- `EnrollmentTokenValidationResult` — structured result for token validation.
- `RuntimeIdentity` — registered runtime identity; includes `Id`, `DisplayName`, `CredentialType`, `PublicKeyOrThumbprint`, and optional `ManagedIdentityPrincipalId`.
- `RuntimeIdentityRegistration` — runtime enrollment payload (name, credential type, public key/thumbprint, optional MI principal id).
- `RuntimeTrustValidationResult` — structured result for auth decisions.
- `RuntimeCredentialRolloverRequest` — request to update the registered credential.

## Runtime-to-control-plane authentication

- Control Plane resolves runtime identity via `IRuntimeIdentityService.GetByIdAsync`.
- Control Plane validates the presented credential via `IRuntimeIdentityService.ValidateAsync`, which returns a `RuntimeTrustValidationResult`.
- Operational auth prefers asymmetric or identity-based trust:
  - Signed challenge or certificate-based validation (Windows).
  - Managed Identity / Entra-issued identity (Azure).
- Long-lived shared symmetric secrets are **not** the preferred path.

## Windows trust model

- Enrollment:
  - Runtime presents a public key or certificate thumbprint as part of `RuntimeIdentityRegistration`.
  - `RuntimeIdentity.CredentialType` typically `PublicKey` or `CertificateThumbprint`.
- Operational auth:
  - Control Plane validates client certificates or signed assertions against the registered key/thumbprint.
  - No long-lived shared symmetric secret between runtime and control plane.

## Azure trust model

- Enrollment:
  - Runtime can register a Managed Identity principal id via `RuntimeIdentityRegistration.ManagedIdentityPrincipalId`.
  - `RuntimeIdentity.CredentialType` set to `ManagedIdentity` (or similar well-known value).
- Operational auth:
  - Control Plane validates the presented Managed Identity assertion against the registered principal id.
  - Aligns with Azure Functions/Managed Identity flows; no local shared secret required.

## Persistence placement

- Trust artifacts use dedicated tables in the shared DbContext:
  - `EnrollmentTokens` — enrollment token metadata.
  - `RuntimeIdentities` — registered runtime identities.
- These tables are **owned by the Runtime Trust subsystem**, not by the operational secret subsystem.
- The operational secret store (`SecretDescriptors`, `SecretVersions`, `SecretAudits`) remains separate and is addressed via `Apperture.Core.Security.Secrets`.

## Credential rollover

- Runtimes can request credential rollover via `IRuntimeIdentityService.RequestCredentialRolloverAsync`.
- A rollover may:
  - Directly update the registered credential (public key or MI principal id), or
  - Issue a new short-lived enrollment token and require re-enrollment (future enhancement).
- Rollover events should be audited via `IEnrollmentAuditWriter`.

## Audit

- Enrollment and trust operations should produce `EnrollmentAuditRecord` entries:
  - Invitation created
  - Token used
  - Runtime identity registered
  - Trust validation failure
  - Credential rollover requested/completed
- Control Plane implementations are responsible for persisting and emitting these audit records.

## Related

- [Bootstrap and Secrets Boundary](bootstrap-and-secrets-boundary.md)
- [Persistence Foundation](persistence-foundation.md)
- [Secrets and key management](secrets-and-key-management.md)

