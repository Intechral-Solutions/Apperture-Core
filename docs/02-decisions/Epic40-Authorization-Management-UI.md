# Epic 40 — Authorization Management UI

## Status

Implemented

## Context

Epic 39 delivered a coherent governance admin backend surface: permission/resource catalog, role list/detail, role permission update (replace-all), and principal effective-access. The management UI had no surfaces for auth/governance administration. Epic 40 implements a truthful authorization management UI that builds directly on the Epic 39 backend and makes auth/governance administration meaningfully usable.

## Summary

- **Permission catalog UI** (`/governance/catalog`): Browse resources and permissions with Family, Source, ModuleId
- **Roles UI** (`/governance/roles`, `/governance/roles/{id}`): List roles, view role detail with granted permissions
- **Role permission editing**: Inline edit on role detail; replace-all semantics explicit; Save sends full permission set
- **Users UI** (`/governance/users`): Paginated user list with link to effective access
- **Effective access UI** (`/governance/users/{id}/effective-access`): Merged permissions from roles; limitation text explicit
- **Audit links**: Governance catalog and role detail link to audit timeline with `actionPrefix=governance.`
- **Operator capabilities**: `CanViewGovernance` (user.read), `CanAssignPermissions` (permission.assign)

## Auth Management UI Routes

| Route | Page | Purpose |
|-------|------|---------|
| `/governance/catalog` | GovernanceCatalog | Permission/resource catalog |
| `/governance/roles` | RolesList | List roles |
| `/governance/roles/{RoleId}` | RoleDetail | Role detail + permission edit |
| `/governance/users` | UsersList | List users (for effective-access entry) |
| `/governance/users/{UserId}/effective-access` | UserEffectiveAccess | Merged permissions from roles |

## Role Permission Editing

- **Replace-all semantics**: Saving replaces the role's entire permission set. Permissions not selected are revoked.
- **UI copy**: "Saving replaces the role's entire permission set. Permissions not selected will be revoked."
- **Flow**: Edit permissions → checkboxes from catalog → Save → PUT full set to backend
- **Visibility**: Edit button shown when `CanAssignPermissions` is true

## Effective Access Scope

- **What it shows**: Merged permissions from all roles assigned to the user
- **Limitation copy**: "Effective access shows merged permissions from assigned roles. It does not include direct user grants, scope containment, or service principals."
- **Not included**: Direct user-level grants, break-glass, scope containment, service principals

## Operator Capabilities (Epic 40)

- **CanViewGovernance**: Evaluated from `user.read` on Role. Gates all governance pages.
- **CanAssignPermissions**: Evaluated from `permission.assign` on Role. Gates Edit permissions button.

## What Is Not Supported (Deferred)

- Incremental assign/revoke — replace-only
- User CRUD (create, edit, delete) — users list is for effective-access entry only
- Role assignment UI (assign/remove user from role) — backend supports it; UI not yet implemented
- Service principal or machine identity effective access
- Per-resource-instance effective access simulation

## Files

| File | Purpose |
|------|---------|
| `GovernanceCatalog.razor` | Permission/resource catalog page |
| `RolesList.razor` | Roles list page |
| `RoleDetail.razor` | Role detail with inline permission edit |
| `UsersList.razor` | Users list page |
| `UserEffectiveAccess.razor` | Effective access page |
| `ControlPanelMenuDefinitions.cs` | Governance domain (Catalog, Roles, Users) |
| `Epic33UiDisplayHelpers.cs` | GetGovernanceAuditLink |
| `OperatorCapabilitiesDto` | CanViewGovernance, CanAssignPermissions |
| `ManagementClient` | Governance API methods |

## Relation to Epic 39

Epic 40 consumes the Epic 39 backend admin surface. No backend changes were required. The UI reflects backend truth exactly: replace-all semantics, role-derived effective access, and no invented auth concepts.
