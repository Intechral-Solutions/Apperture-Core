# Epic 39 — Authorization and Governance Administration Backend Surface

## Status

Implemented

## Context

The platform had governance concepts, module-contributed resources and permissions, permission-aware management flows, and audit maturity. What was missing was a coherent backend administration surface for auth/governance itself. Admins could not easily list roles/principals/resources/permissions in an admin-consumable way, inspect what a role or principal can do, assign or revoke grants with clear validation, or distinguish definitions from grants from effective access. Epic 39 implements a truthful backend administration surface sufficient to support a future auth management UI without forcing the UI to invent its own auth model.

## Summary

- **Permission/resource catalog**: `GET /api/governance/descriptors` returns resources and permissions with metadata (ResourceFamily, Source, ModuleId derived from ScopeKey).
- **Role inspection**: `GET /api/roles` (list with IsSystemRole), `GET /api/roles/{id}` (detail with grants), `GET /api/roles/{id}/permissions` (grants).
- **Grant management**: `PUT /api/roles/{id}/permissions` replaces all role permissions. Validates role existence and permission catalog; returns 404 when role not found, 400 on invalid permission.
- **Principal effective access**: `GET /api/users/{id}/effective-access` returns merged permissions from assigned roles for a target user. Requires `user.read`.
- **Audit**: Grant changes emit `governance.permission.assignment.updated`; role assign/remove emit `governance.role.assignment.created` / `governance.role.assignment.removed` (existing).

## Admin APIs

| Method | Path | Purpose | Permission |
|--------|------|---------|------------|
| GET | `/api/governance/descriptors` | Catalog with Family, Source, ModuleId | user.read |
| GET | `/api/governance/effective-permissions` | Caller's effective permissions | user.read |
| GET | `/api/roles` | List roles (Id, Name, Description, IsSystemRole) | user.read |
| GET | `/api/roles/{id}` | Role detail with grants | user.read |
| GET | `/api/roles/{id}/permissions` | Role grants | user.read |
| PUT | `/api/roles/{id}/permissions` | Replace role grants | permission.assign |
| GET | `/api/users/{id}/effective-access` | Target user's effective access | user.read |

## Catalog Response Shape

`GovernanceDescriptorResponse`:

- **Resources**: `ResourceCatalogEntryDto` (ResourceType, ScopeKey, Family)
- **Permissions**: `PermissionCatalogEntryDto` (Action, ResourceType, ScopeKey, Family, Source, ModuleId)

- **Source**: `platform` when ScopeKey is `instance`; `module` when ScopeKey matches `module:*`
- **ModuleId**: Extracted from ScopeKey when Source is `module` (e.g. `module:Sample.HelloWorld` → ModuleId `Sample.HelloWorld`)

## Grant Model

- **Replace semantics**: `PUT /api/roles/{id}/permissions` replaces the full permission set. To assign a permission, include it in the request body. To revoke, omit it.
- **Validation**: All permissions are validated against `IResourceDescriptorRegistry.IsPermissionRegistered` before persistence. Unknown or malformed permissions return 400.
- **Role existence**: If the role does not exist, returns 404.

## Effective Access Meaning

`GET /api/users/{id}/effective-access` returns:

- **UserId**: The target user id.
- **RoleIds**: Role IDs assigned to the user (provenance).
- **Permissions**: Union of permissions from all assigned roles, deduplicated.

This reflects **role-membership-based effective permissions** only. It does not simulate scope containment (e.g. whether the user can act on a specific resource instance). Runtime authorization still applies scope containment via `IScopeContainmentEvaluator`.

## Permissions Protecting Governance Admin

- **user.read**: Descriptors, roles list/detail/grants, effective-permissions (caller), effective-access (target user).
- **permission.assign**: `PUT /api/roles/{id}/permissions`.
- **role.assign**: User-role assignment (`POST /api/users/{id}/roles`, `DELETE /api/users/{id}/roles/{roleId}`).

## Audit

- `SetRolePermissionsAsync` → `governance.permission.assignment.updated` (details include roleId, permissionCount).
- `AssignRoleAsync` → `governance.role.assignment.created`.
- `RemoveRoleAsync` → `governance.role.assignment.removed`.

## What Is Not Supported (Deferred)

- Incremental assign/revoke — replace-only; UI must fetch, modify, PUT.
- Service principal or machine identity effective-access — Human actors resolving to User only.
- Approval workflows or temporary grants.
- Per-resource-instance effective access simulation.
- Human-readable permission descriptions — Action/ResourceType/ScopeKey are the identifiers.

## Files

| File | Purpose |
|------|---------|
| `GovernanceAdminModels.cs` | PermissionCatalogEntryDto, ResourceCatalogEntryDto, RoleDetailDto, PrincipalEffectiveAccessDto |
| `GovernanceController.cs` | Descriptors with Family/Source/ModuleId; effective-permissions |
| `RolesController.cs` | List (IsSystemRole), Get (detail), GetPermissions, SetPermissions (404 on unknown role) |
| `UsersController.cs` | GetEffectiveAccess |
| `IResourceDescriptorRegistry` | GetResourceDescriptorsWithFamily, GetPermissionDescriptorsWithFamily |
| `UserManagementService.cs` | GetRoleAsync; SetRolePermissionsAsync role-existence check |
