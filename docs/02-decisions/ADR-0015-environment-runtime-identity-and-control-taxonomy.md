# ADR-0015: Environment/Runtime Governance Identity & Control Taxonomy

## Status
Proposed

## Context
Apperture already has a governance spine that is operation-centric:
- canonical descriptor registry with strict duplicate semantics
- authorization evaluation seam (`IAuthorizationEvaluationService`) backed by RBAC + scope containment
- role permission assignment validation against the canonical descriptor catalog

To generalize governance beyond operations (and prove the model’s portability), we must govern and control:
- environment-scoped operator visibility
- runtime declared capability visibility
- privileged operator control over runtime enrollment

This cycle must preserve control-plane vs runtime-plane boundaries and keep runtime authentication (token-based enrollment) distinct from operator authorization (RBAC).

## Decision
1. **Canonical governed runtime resource** is `ResourceTypeConstants.RuntimeInstance`, keyed by `runtimeId`.
2. **Environment scoping** uses a process-stable `environmentId` for the single-tenant instance.
   - `environmentId` is derived from `APERTURE_ENVIRONMENT_ID` (fallback: `local`).
3. **Scope key shapes**:
   - Environment scope: `environment:{environmentId}`
   - Runtime scope: `environment:{environmentId}:runtime:{runtimeId}`
4. **Actions taxonomy**:
   - `environment.read` for `GET /api/environments`
   - `runtime.read` for `GET /api/runtime-instances/{runtimeId}/capabilities`
   - `runtime.enroll.invitation.create` for `POST /api/enroll/invitation`
5. **Operator control path** (`POST /api/enroll/invitation`) is governed and auditable:
   - admission is decided by `IAuthorizationEvaluationService`
   - successful invitation creation emits an audit record (`governance.runtime.enrollment.invitation.created`)
6. **Runtime authentication** remains token-authenticated:
   - `POST /api/enroll` is `AllowAnonymous`
   - `SqlRuntimeEnrollmentService.EnrollAsync` validates token-scoped environment mapping before registering runtime identity
7. **Declared runtime capability metadata** (invitation scopes) is persisted:
   - invitation `ScopesJson` is transferred into runtime identity `DeclaredScopesJson`
   - operator inspection reads declared scopes from runtime identity after enrollment

## Consequences
### Positive
- Environment/runtime governance uses the existing descriptor registry and authorization evaluation seam (no parallel permission system).
- Privileged operator actions are auditable with a clear success path.
- Operator visibility of runtime declared capabilities is durable and authorization-gated.

### Negative
- In the current cycle, `environmentId` is process-stable (single-tenant); multi-environment deployments require a follow-up cycle to broaden the model.

### Neutral
- Runtime-plane communication substrate is intentionally not built in this cycle; governance and inspection are implemented on existing control surfaces.

