# Environment / Runtime Governance and Control (Cycle)

## Overview
This document defines how Apperture governs and controls **environments** and **runtimes** using the existing platform governance spine:
`IResourceDescriptorRegistry` + `IResourceDescriptorRegistry.IsPermissionRegistered(...)` + `IAuthorizationEvaluationService`.

The implementation in this cycle adds:
- Authorization-aware operator inspection endpoints for environments and runtime declared capability metadata (invitation scopes).
- A governance-gated and auditable operator control path for runtime enrollment invitations.
- Token-scoped validation and persistence so runtime declared invitation scopes can be inspected after enrollment.

## Canonical Governed Resources
### Environment (inspection surface)
- Governed resource type: `ResourceTypeConstants.Environment`
- Scope key shape: `environment:{environmentId}`

### Runtime (canonical governed resource)
- Governed resource type: `ResourceTypeConstants.RuntimeInstance`
- Scope key shape: `environment:{environmentId}:runtime:{runtimeId}`

In the current single-tenant implementation, `environmentId` is process-stable and derived from `APERTURE_ENVIRONMENT_ID` (fallback: `local`).

## Scope Key Taxonomy
The control-plane governance scope keys follow strict identity shapes:
- Environment scope: `environment:{environmentId}`
- Runtime scope: `environment:{environmentId}:runtime:{runtimeId}`

These are used consistently for:
- descriptor catalog registration
- authorization evaluation scope containment
- operator-facing inspection gates

## Authorization Model: Visibility vs Control
Operator authorization is evaluated using governance actions and resource descriptors.

### Visibility
- `environment.read` gates `GET /api/environments`
- `runtime.read` gates `GET /api/runtime-instances` (durable runtime identity discovery)
- `runtime.read` gates `GET /api/runtime-instances/{runtimeId}/capabilities`

### Privileged Control
- `runtime.enroll.invitation.create` gates `POST /api/enroll/invitation`

`POST /api/enroll/invitation` is the operator-issued privileged control surface.
It does not authenticate runtimes; it issues an enrollment token.

## Runtime Enrollment Identity & Token-Scoped Mapping
Runtime enrollment remains token-authenticated:
- `POST /api/enroll` is `AllowAnonymous`
- the token is validated against persisted enrollment token records

To make env↔runtime governance scoping durable:
1. Operator invitation creation persists:
   - `EnvironmentId` associated with the token
   - `ScopesJson` derived from `CreateInvitationRequest.Scopes`
2. When a runtime enrolls:
   - `SqlRuntimeEnrollmentService.EnrollAsync` validates the token’s `EnvironmentId` matches the current control-plane `EnvironmentId`
   - declared scopes are transferred into the persisted runtime identity record as `DeclaredScopesJson`

After successful enrollment, operator inspection can safely project runtime declared scopes
from persisted runtime identity (`RuntimeIdentity.DeclaredScopes`).

Note: these invitation scopes are treated as declared metadata strings (not platform-governed capabilities).

## Auditability
Successful privileged operator control events write durable audit records via `IAuditService`.

For this cycle:
- `governance.runtime.enrollment.invitation.created` is appended when an operator is authorized and an invitation is created.

Authorization-denied attempts do not emit the audit record (by design and covered by unit tests).

