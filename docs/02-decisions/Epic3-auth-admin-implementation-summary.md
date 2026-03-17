# Epic 3: Auth Administration and Operator Management — Implementation Summary

## Status

Implemented (Epic 3). Administrative API and service surface for human auth: permissions, user list/detail, user creation and identity-link management, role assignment/removal, local credential admin, audit events, and small consistency cleanup. All admin operations are permission-protected and audited.

## Auth-admin permission model

- **Actions added (ActionConstants):** `user.read`, `user.manage`, `role.assign`, `credential.manage`.
- **Catalog:** Registered in `PlatformPermissionSeeder` with `ResourceTypeConstants.User` (or `Role` for `role.assign`) and `ScopeKeyConstants.Instance`, family Core.
- **Platform administrator role:** EF migration `AddAuthAdminPermissionsToPlatformAdminRole` appends the four permission descriptors to the existing `Roles.PermissionsJson` for `role-platform-administrator`.
- **Protection pattern:** All new admin endpoints use class-level `[Authorize]`; `UserManagementService` evaluates the appropriate permission via `IAuthorizationService.EvaluateAsync` and throws `UnauthorizedAccessException` on deny; controllers return `Forbid()`.

## User read APIs

- **List:** `GET /api/users?skip=0&take=20` returns `UserListResponse` (items: userId, displayName, hasLocalLink, hasEntraLink, roleIds; totalCount). Requires `user.read`.
- **Detail:** `GET /api/users/{id}` returns `UserDetailDto` (userId, displayName, identityLinks with source/externalId/lastAuthenticatedAt, roleIds, hasLocalCredential, isCredentialDisabled). Requires `user.read`. Local credential status is populated only when the user has a Local link; no hashes or sensitive data exposed.
- **Repository:** `IUserRepository.GetPageAsync(skip, take)` and existing `GetByIdAsync`; implemented in `SqlUserRepository` with batched loads for links and roles.

## User creation and identity-link management

- **Create user:** `POST /api/users` body `{ displayName, identitySource, externalId [, password ] }`. Creates user with one initial identity link; optional password when source is Local (creates LocalCredential). Normalizes Local externalId (trim, lowercase). Requires `user.manage`. Returns 201 with userId.
- **Add link:** `POST /api/users/{id}/identity-links` body `{ identitySource, externalId }`. Normalizes for Local. Fails if (source, externalId) already exists. Requires `user.manage`.
- **Remove link:** `DELETE /api/users/{id}/identity-links?source=Local&externalId=...`. Blocked if it is the user's last link. Requires `user.manage`.
- **Repository:** `IUserRepository.AddIdentityLinkAsync`, `RemoveIdentityLinkAsync` implemented in `SqlUserRepository`; last-link guard in remove.

## Role assignment / removal

- **List roles:** `GET /api/roles` returns id, name, description for all roles. Requires `user.read`.
- **Assign:** `POST /api/users/{id}/roles` body `{ roleId }`. Idempotent. Requires `role.assign`. Returns 404 if user or role not found.
- **Remove:** `DELETE /api/users/{id}/roles/{roleId}`. Requires `role.assign`. Uses existing `IRoleRepository.AssignToUserAsync` / `RemoveFromUserAsync`.

## Local credential administration

- **Set password:** `POST /api/users/{id}/local-credential/password` body `{ newPassword }`. User must have a Local identity link. Creates credential row if missing (covers first local admin created without password). Validates `LocalAuthOptions.MinPasswordLength`. Requires `credential.manage`.
- **Enable/disable:** `PATCH /api/users/{id}/local-credential` body `{ isDisabled }`. User must have a Local link and an existing credential row. Requires `credential.manage`.
- Uses existing `ILocalCredentialRepository` and `ILocalPasswordHasher`; no password or hash exposed.

## Auth-admin audit

- **Events (IAuditService.AppendAsync):** `user.created`, `identity_link.added`, `identity_link.removed`, `role.assigned`, `role.removed`, `credential.password_set`, `credential.enabled`, `credential.disabled`.
- **Shape:** `AuditRecord` with ActorId (operator), Action, Resource (User/instance), Outcome `auth-admin`, Details (e.g. userId, roleId, source/externalId). Emitted from `UserManagementService` after each successful mutation.

## Service and API surface

- **IUserManagementService** (Core.Application): GetUserPageAsync, GetUserByIdAsync, GetRolesAsync, CreateUserAsync, AddIdentityLinkAsync, RemoveIdentityLinkAsync, AssignRoleAsync, RemoveRoleAsync, SetLocalPasswordAsync, SetLocalCredentialDisabledAsync.
- **UserManagementService** (ControlPlane.Security): Implements interface; performs permission checks and audit; uses `LocalAuthHelpers.NormalizeUsername` for Local externalId. Registered in `ServiceCollectionExtensions.Governance`.
- **UsersController:** `api/users` — list, get, create, identity-links, roles, local-credential/password, PATCH local-credential. **RolesController:** `api/roles` — GET list.

## Cleanup performed

- **MenusController:** Added `[Authorize]` so menus require an authenticated user.
- **PlaceholderAuthHandler:** Removed (obsolete; was not registered in `AddAppertureAuthentication`).

## Files changed (summary)

- **Permissions:** ActionConstants.cs, PlatformPermissionSeeder.cs, migration AddAuthAdminPermissionsToPlatformAdminRole (+ Designer).
- **Repository:** IUserRepository.cs, SqlUserRepository.cs (GetPageAsync, AddIdentityLinkAsync, RemoveIdentityLinkAsync).
- **Application:** IUserManagementService.cs (new).
- **ControlPlane.Security:** UserManagementService.cs (new), ServiceCollectionExtensions.Governance.cs (register service).
- **Management.Api:** UsersController.cs (new), RolesController.cs (new), MenusController.cs ([Authorize]).
- **Management.Host:** PlaceholderAuthHandler.cs (deleted).
- **Docs:** Epic3-auth-admin-implementation-summary.md (this file).

## Remaining for later stages

- Broad admin UI.
- Self-service forgot-password / reset UX.
- User-level global disable semantics for all providers.
- Break-glass execution logic.
- Bootstrap recovery implementation.
- OIDC single sign-out.
- SCIM or Entra group sync.
- Module permission runtime contribution.
- Broad security observability dashboards.
- Runtime trust redesign.
- Relationship authorization.
- Optionally migrate EnrollController from role check to a dedicated permission (e.g. enrollment.create).

## Related

- [Epic 2: Human Authentication Providers](Epic2-human-auth-implementation-summary.md)
- [Next stage: Platform auth readiness](../01-architecture/next-stage-platform-auth.md)
- [Authorization and governance foundation](../01-architecture/authorization-and-governance-foundation.md)
