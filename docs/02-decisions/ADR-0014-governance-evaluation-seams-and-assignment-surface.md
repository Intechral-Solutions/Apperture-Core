# ADR-0014: Governance evaluation seams and assignment surface

## Status

Accepted

## Context

Operation execution established feature-level governance patterns, but platform-wide governance still risked drift:

- authorization checks were duplicated across layers with inconsistent context shape
- role-permission assignment existed in persistence but lacked explicit control-plane API surface
- module permission contribution did not cover governed resource/action declarations

## Decision

1. Introduce a shared authorization seam (`IAuthorizationEvaluationService`) using `AuthorizationEvaluationContext`.
2. Keep `IResourceDescriptorRegistry` as the canonical descriptor source and enforce naming/shape validation + deduplication at registration time.
3. Extend module manifests with optional governance contributions (`GovernedResources`, `GovernedActions`) validated during startup registration.
4. Add minimum governance management APIs:
   - descriptor listing and effective-permission inspection
   - explicit role-permission read/replace endpoints
5. Standardize privileged governance audit action names for role/permission operations.

## Consequences

- API/application/query enforcement paths now share a consistent evaluation context.
- Module extensibility can grow without bypassing governance naming and collision rules.
- Permission administration is operator-visible through explicit APIs without introducing a large policy framework.
- Schedule governance can be projected consistently via capability endpoints even before full scheduling CRUD exists.

## Implementation notes (corrective pass)

Refinements applied to align implementation with governance discipline:

- **Registry collision semantics:** Exact duplicate (same descriptor + family) is idempotent; conflicting duplicate (same key, different descriptor) throws with actionable diagnostics. No silent `TryAdd`-style drops.
- **Module contribution neutrality:** Generic module contributions require explicit `ResourceType`; no operation-shaped defaults. Contributions use `ResourceFamily.Core`.
- **Identity validation:** ResourceType, ScopeKey, and Action enforce naming/shape rules at registration boundaries.
- **Role-permission validation:** `SetRolePermissionsAsync` validates requested permissions against `IResourceDescriptorRegistry.IsPermissionRegistered` before persistence; unknown or malformed permissions are rejected with clear errors.

