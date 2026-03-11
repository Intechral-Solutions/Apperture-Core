# Authorization and Governance Foundation

This document captures the status of the auth/governance scaffolding (Phases 1–3): principles, implementation summary, project placement, core types, and the recommended next phase.

## Principles

- **Authorization is central and generic** — Auth knows principals, resource types, actions, scopes; it does not know connector internals.
- **Resource governance is broader than connectors** — Connector/connection/capability concepts are one governed resource family.
- **Governed resources plug in via IGovernedResource** — Auth consumes identity only; no family-specific types in auth core.
- **Administrative vs operational auth** — Distinguished by permission action (e.g. `connection.create` vs `capability.consume`).
- **Binding is delegation** — Does not replace operational authorization.

## Implementation Summary

| Area | Status | Location |
|------|--------|----------|
| Principal/identity | Done | IActor, ActorKind, ActorId, IPrincipalContext, IIdentityResolutionService, StubPrincipalContext |
| Role/permission | Done | Role, Permission, IRoleProvider, PermissionDescriptor, StubRoleProvider |
| Governed resource vocabulary | Done | IGovernedResource, IGovernedResourceIdentity, GovernedResourceIdentity, ResourceTypeConstants, ActionConstants, ScopeKeyConstants, GovernedResourceDescriptor |
| Authorization evaluation | Done | IAuthorizationService, IAuthorizationPolicyEvaluator, DelegatingAuthorizationService, RbacAuthorizationPolicyEvaluator, IScopeContainmentEvaluator |
| Principal-to-user bridge | Done | IPrincipalToUserResolver, StubPrincipalToUserResolver |
| Audit context | Done | AuthorizationEvaluationContext, AuthorizationEvaluationResult, AuthorizationEvaluationPath |
| Permission catalog | Done | IPlatformPermissionSeeder, PlatformPermissionSeeder, IResourceDescriptorRegistry |
| Connector domain (Phase 1) | Done | Connection, Binding, ConnectorDefinition, MappingDefinition, ICapabilitySlot, CapabilityVisibility |

## Project/Layer Placement

| Concern | Project | Files |
|---------|---------|-------|
| Primitives (Actor, ResourceDescriptor, PermissionDescriptor) | Shared.Kernel | IActor.cs, ResourceDescriptor.cs, PermissionDescriptor.cs |
| Governed resource contracts | Shared.Kernel | IGovernedResource.cs, IGovernedResourceIdentity.cs |
| Domain model (User, Role, Permission) | Core.Domain | Identity/, Trust/ |
| Governance vocabulary | Core.Domain | Governance/ |
| Connector domain types | Core.Domain | Connector/ |
| Application contracts | Core.Application | Services/, Authorization/ |
| Implementations | ControlPlane.Security | ServiceCollectionExtensions.Governance.cs, RbacAuthorizationPolicyEvaluator, DelegatingAuthorizationService, stubs |

## Core Types and Contracts

### Principal/Identity

- **IActor** — Id, Kind, DisplayName
- **ActorKind** — Human, Machine, System, RemoteInstance
- **ActorId** — Stable identifier
- **IPrincipalContext** — GetCurrentActorAsync
- **IIdentityResolutionService** — ResolveAsync (auth context)
- **IPrincipalToUserResolver** — Resolve Human actor to UserId for role lookup

### Roles/Permissions

- **Role** — Id, Name, Permissions (PermissionDescriptor list)
- **Permission** — Action, Resource, ModuleId
- **PermissionDescriptor** — Action, optional ResourceDescriptor
- **IRoleProvider** — GetRoles, GetRolesForUserAsync, Assign/Remove

### Governed Resource Vocabulary

- **IGovernedResource** — GetIdentity()
- **IGovernedResourceIdentity** — ResourceType, ResourceId, ScopeKey, OwnerId
- **GovernedResourceIdentity** — Value object
- **GovernedResourceDescriptor** — ResourceDescriptor + ResourceFamily; FromIdentity()
- **ResourceTypeConstants** — Connection, Binding, Operation, etc.
- **ActionConstants** — connection.create, operation.execute, etc.
- **ScopeKeyConstants** — Instance, Module(), Connection(), Slot()

### Authorization Evaluation

- **IAuthorizationService** — EvaluateAsync(actor, permission, resource)
- **IAuthorizationPolicyEvaluator** — EvaluateAsync(actor, permission, resource, resourceIdentity)
- **IScopeContainmentEvaluator** — IsContained(innerScopeKey, outerScopeKey)
- **DelegatingAuthorizationService** — Forwards to policy evaluator
- **RbacAuthorizationPolicyEvaluator** — RBAC + scope containment

### Audit-Aware Context

- **AuthorizationEvaluationContext** — ActorId, Permission, Resource, Result, EvaluationPath
- **AuthorizationEvaluationResult** — Allow, Deny
- **AuthorizationEvaluationPath** — BreakGlass, Relationship, Rbac, ResourceOwnership, None

## Dependency and Boundary Check

| Check | Result |
|-------|--------|
| Auth remains generic | Yes — no connector types in IAuthorizationService, IAuthorizationPolicyEvaluator, IScopeContainmentEvaluator |
| Governed-resource participation generic | Yes — IGovernedResource, IGovernedResourceIdentity are string-based |
| No connector-specific abstractions in auth core | Yes — Connector types are in Core.Domain.Connector; auth uses ResourceType constants |
| No service-locator patterns | Yes — explicit DI; callers pass actor, permission, resource |
| No resource-browsing loopholes | Yes — modules resolve via declared slots and bindings; no global browse |

## Seed Naming Guidance

- **Actions:** `{resourceType}.{action}` — connection.create, binding.assign, operation.execute (ActionConstants)
- **Resource types:** PascalCase — Connection, Binding, ConnectorDefinition (ResourceTypeConstants)
- **Scope keys:** instance, module:{id}, connection:{id}, operation:{id}, module:{id}:slot:{name} (ScopeKeyConstants)
- **Principal categories:** ActorKind — Human, Machine, System, RemoteInstance

## Remaining Gaps (Optional, Not Blocking)

- **AuthorizationEvaluationContext** is defined but not yet populated during evaluation. Populating it would enable future audit emission. Defer until audit integration.
- **Access-denied reasoning** — Current API returns bool. A richer result type (Allow/Deny + reason) could be added later if needed.
- **IAuthorizationService** does not accept IGovernedResource directly; callers use GovernedResourceDescriptor.FromIdentity. This is intentional and documented.

## Deferred Items

- Entra/local identity resolution
- Role and User persistence
- Break-glass and relationship evaluation
- Machine/System actor role resolution
- Connector resolution, IBindingService implementation
- IConnectionResolver implementation
- Full audit emission of auth decisions
- Connector management UI
- Policy authoring, ABAC

## Next Phase: Connector/Connection Integration

1. **Persistence:** Add EF entities for Connection, Binding, ConnectorDefinition, MappingDefinition; migrations.
2. **IBindingService:** Implement create/delete bindings; resolve slot to connection.
3. **IConnectionResolver:** Implement resolution for platform SQL connector (first concrete connector).
4. **API boundaries:** Use IAuthorizationService at connection/binding endpoints (connection.create, binding.assign).
5. **Optional:** Implement IPrincipalToUserResolver and IRoleProvider with persistence for Management API auth.

The auth core is ready. Connectors plug in via IGovernedResource; Connection and Binding already implement it. No changes to the auth core are required for Phase 4.

## Related

- [ADR-0012](../02-decisions/) — ResourceDescriptor vs IGovernedResourceIdentity; evaluation order
- [ADR-0013](../02-decisions/) — Action naming; scope key formats
- [Next Stage: Platform Auth Readiness](next-stage-platform-auth.md)
