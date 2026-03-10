# ADR-0012: Authorization Evaluation Model

## Status

Accepted

## Context

The platform authorization model uses two distinct concepts for resource identification: `ResourceDescriptor` (type + scope) and `IGovernedResourceIdentity` (instance identity with type, id, scope, owner). Without clear documentation of when to use each and how they merge during evaluation, implementers risk inconsistent authorization behavior, scope confusion, and improper instance-level checks.

## Decision

### ResourceDescriptor vs IGovernedResourceIdentity

- **ResourceDescriptor(ResourceType, ScopeKey)** — Used for type- and scope-level permission checks. Appears in `PermissionDescriptor` as the optional resource context. Does not identify a specific instance; it describes the resource category and scope boundary (e.g. "Connection" at "instance" scope).

- **IGovernedResourceIdentity** — Instance-level identity. Provided by `IGovernedResource.GetIdentity()` when evaluating against a concrete governed entity. Carries `ResourceType`, `ResourceId`, `ScopeKey`, and optional `OwnerId`. Use this when the caller has a specific resource instance and the policy must consider ownership or instance boundaries.

**Merge logic for instance-level checks:**

1. Type match: `resourceIdentity.ResourceType` must match `permission.Resource?.ResourceType` (or permission is unscoped).
2. Scope containment: `resourceIdentity.ScopeKey` must be contained within or equal to the permission's scope (e.g. `instance` contains `module:{id}`).
3. Ownership (optional): If policy requires owner checks, `resourceIdentity.OwnerId` is compared to the principal's identity or scope.

### Policy Evaluation Order

Evaluation proceeds in this order; the first applicable result wins:

1. **Break-glass** (if applicable): Explicit override flag and audit. Deny if not authorized for break-glass.
2. **Relationship scopes** (future): For `RemoteInstancePrincipal`, evaluate `GrantedCapability` and `RelationshipScope`. Defer detailed integration.
3. **RBAC**: Resolve principal's roles via `IRoleProvider`, compute effective permissions, match `PermissionDescriptor`.
4. **Resource ownership/scope**: When a governed resource is passed, validate scope containment and optional owner match.

### IAuthorizationService.EvaluateAsync Semantics

- **Signature:** `EvaluateAsync(IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource, CancellationToken cancellationToken = default)`

- **Caller responsibilities:**
  - Pass `permission` with the required action and optional `ResourceDescriptor` for type/scope.
  - When the target is a governed resource, the caller may either:
    - Pass `resource` derived from `governedResource.GetIdentity()` mapped to `ResourceDescriptor(resourceType, scopeKey)` for scope checking, or
    - Pass `null` for `resource` when doing type-only checks.
  - Auth evaluates permission and, when `resource` is present, applies scope containment and optional ownership rules.

- **Implementation:** Policy evaluation uses `IResourceDescriptorRegistry` for known permission descriptors, `IRoleProvider` for role resolution (Phase 2), and the merge logic above for instance-level validation.

## Consequences

### Positive

- Clear separation of type-level vs instance-level checks.
- Documented evaluation order reduces policy collisions.
- Governed resources integrate cleanly without auth depending on resource-family specifics.

### Negative

- Callers must understand when to pass `resource` and how to derive it from `IGovernedResourceIdentity`.

### Neutral

- Relationship-scoped authorization is deferred; integration point is reserved.
