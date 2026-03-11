# Epic 1: Platform Identity Foundation — Implementation Summary

## What was added

- **Persistence (Persistence.SqlServer):**
  - Entities: `UserEntity`, `IdentityLinkEntity`, `RoleEntity`, `UserRoleEntity`.
  - Tables: `Users`, `IdentityLinks`, `Roles`, `UserRoles` (PascalCase, string IDs, RowVersion where appropriate).
  - Unique index on `IdentityLinks(Source, ExternalId)` for lookup.
  - `IUserRepository` / `SqlUserRepository`, `IRoleRepository` / `SqlRoleRepository` in Core.Application and Persistence respectively.
  - EF migration `AddPlatformIdentityTables` including seed for role `role-platform-administrator` (Platform Administrator) with a minimal permission set.

- **Control plane (ControlPlane.Security):**
  - `SqlRoleProvider` — implements `IRoleProvider` using `IRoleRepository`.
  - `SqlPrincipalToUserResolver` — implements `IPrincipalToUserResolver` using `IUserRepository.GetByIdentityLinkAsync` (Entra then Local).
  - `HostPrincipalContext` — implements `IPrincipalContext`; resolves `IHttpContextAccessor.HttpContext.User` to `IActor` via `ClaimsPrincipalToActorMapper`.
  - `HumanActor` — concrete `IActor` for human principals.
  - `ClaimsPrincipalToActorMapper` — provider-agnostic mapping from ClaimsPrincipal to Human IActor (NameIdentifier/sub, name/preferred_username).
  - `PrincipalContextIdentityResolutionService` — implements `IIdentityResolutionService` by delegating to `IPrincipalContext`.

- **Management.Host:**
  - `PlaceholderAuthHandler` — no-op auth handler so the auth pipeline runs; replace with Entra/local in later epics.
  - `AddAuthentication` (default scheme Placeholder), `AddAuthorization`, `UseAuthentication()`, `UseAuthorization()` in pipeline.

- **Documentation:** Updates to `authorization-and-governance-foundation.md` and `next-stage-platform-auth.md` (principal resolution, persisted identity, host scaffolding, first user steps, deferred work).

## What existing abstractions were reused

- **Shared.Kernel:** IActor, ActorKind, ActorId, PermissionDescriptor, ResourceDescriptor (unchanged).
- **Core.Domain:** User, UserId, IdentityLink, IdentitySource, Role, RoleId (unchanged).
- **Core.Application:** IPrincipalContext, IIdentityResolutionService, IPrincipalToUserResolver, IRoleProvider, IAuthorizationService, IAuthorizationPolicyEvaluator, DelegatingAuthorizationService, RbacAuthorizationPolicyEvaluator, IScopeContainmentEvaluator, IPlatformPermissionSeeder, IResourceDescriptorRegistry (unchanged).
- **Persistence conventions:** String IDs, PascalCase table names, RowVersion, JSON for Role.Permissions (PermissionsJson), repository pattern consistent with SqlConnectionRepository.

## What stubs were replaced

- `StubPrincipalContext` → `HostPrincipalContext`.
- `StubPrincipalToUserResolver` → `SqlPrincipalToUserResolver`.
- `StubRoleProvider` → `SqlRoleProvider`.

Stubs are no longer registered in `AddAppertureGovernance`.

## What was intentionally deferred

- Full Entra interactive authentication flow.
- Full local username/password authentication (including password hashing and credential store).
- Break-glass policy execution in the authorization evaluator.
- Relationship and resource-ownership authorization.
- Full audit emission of authorization evaluations.
- Module permission registration runtime pipeline.
- First-run wizard or full bootstrap UX for first platform user (documented manual steps only).
- CLI operator identity.

## Follow-on work

1. **Epic 2 (Entra / local auth):** Replace placeholder auth in Management.Host with a real authentication scheme (e.g. Microsoft Identity Web for Entra, or local cookie auth with credential verification). Set as DefaultAuthenticateScheme so HostPrincipalContext receives a real ClaimsPrincipal.
2. **First platform user:** Use documented steps (insert Users, IdentityLinks, UserRoles) or add a small admin API/CLI to create the first user and link; assign `role-platform-administrator` or custom roles.
3. **Break-glass:** Implement break-glass path in RbacAuthorizationPolicyEvaluator when required by policy/audit.
4. **Audit emission:** Populate AuthorizationEvaluationContext during evaluation and emit to IAuditEventSink.
5. **Relationship / resource ownership:** Implement relationship-scoped and resource-ownership checks in the policy evaluator when needed.
6. **Module permission registration:** Implement runtime pipeline for module-declared permissions when needed.
