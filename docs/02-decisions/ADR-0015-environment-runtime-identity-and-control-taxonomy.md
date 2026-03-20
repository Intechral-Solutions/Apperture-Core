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
- governed runtime capability visibility (validated invitation scopes)
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
   - `runtime.read` for `GET /api/runtime-instances` (durable runtime identity discovery)
   - `runtime.read` for `GET /api/runtime-instances/{runtimeId}/capabilities` (governed capability inspection: `capabilityId` + `kind`)
   - `runtime.enroll.invitation.create` for `POST /api/enroll/invitation`
   - `runtime.control.shutdown.request` for `POST /api/runtime-instances/{runtimeId}/controls/shutdown/request`
5. **Operator control path** (`POST /api/enroll/invitation`) is governed and auditable:
   - admission is decided by `IAuthorizationEvaluationService`
   - successful invitation creation emits an audit record (`governance.runtime.enrollment.invitation.created`)
6. **Runtime authentication** remains token-authenticated:
   - `POST /api/enroll` is `AllowAnonymous`
   - `SqlRuntimeEnrollmentService.EnrollAsync` validates token-scoped environment mapping before registering runtime identity
7. **Governed runtime capabilities** (invitation scopes) is persisted as validated capability ids:
   - invitation `ScopesJson` is transferred into runtime identity `DeclaredScopesJson`
   - invitation scopes are validated against the platform capability catalog at invitation creation time
   - operator inspection reads runtime identity and projects governed capabilities (`capabilityId` + `kind`)
   - any legacy/unknown capability ids persisted in existing runtime identities are treated as invalid metadata and are filtered out by inspection surfaces

## Consequences
### Positive
- Environment/runtime governance uses the existing descriptor registry and authorization evaluation seam (no parallel permission system).
- Privileged operator actions are auditable with a clear success path.
- Operator visibility of governed runtime capabilities is durable (DB-backed runtime identity) and authorization-gated.

### Negative
- In the current cycle, `environmentId` is process-stable (single-tenant); multi-environment deployments require a follow-up cycle to broaden the model.

### Neutral
- Minimal runtime-plane control substrate is built for shutdown proving ground:
  - operator control requests are persisted durably
  - the WorkerHost polls queued shutdown requests and transitions them to `applied` (stopping the host)

