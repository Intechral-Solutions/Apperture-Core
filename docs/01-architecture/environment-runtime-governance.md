# Environment / Runtime Governance and Control (Cycle)

## Overview
This document defines how Apperture governs and controls **environments** and **runtimes** using the existing platform governance spine:
`IResourceDescriptorRegistry` + `IResourceDescriptorRegistry.IsPermissionRegistered(...)` + `IAuthorizationEvaluationService`.

The implementation in this cycle adds:
- Authorization-aware operator inspection endpoints for environments and governed runtime capabilities (validated invitation scopes).
- A governance-gated and auditable operator control path for runtime enrollment invitations.
- A governance-gated runtime control intent path (shutdown proving ground) that persists operator intent and transitions queued requests in the worker host.
- Token-scoped validation and persistence so runtime governed capability ids can be inspected after enrollment.

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

### Runtime Control Intent
- `runtime.control.shutdown.request` gates `POST /api/runtime-instances/{runtimeId}/controls/shutdown/request`

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

After successful enrollment, operator inspection can safely project governed runtime capabilities
from persisted runtime identity (`RuntimeIdentity.DeclaredScopes`), filtered by the platform capability catalog.

Note: invitation scopes are validated against the platform capability catalog at invitation creation time and persisted as governed runtime capability ids.
Any legacy/unknown capability ids already persisted on older runtimes are treated as invalid metadata and filtered out by inspection surfaces.

## Auditability
Successful privileged operator control events write durable audit records via `IAuditService`.

For this cycle:
- `governance.runtime.enrollment.invitation.created` is appended when an operator is authorized and an invitation is created.

Authorization-denied attempts do not emit the audit record (by design and covered by unit tests).

