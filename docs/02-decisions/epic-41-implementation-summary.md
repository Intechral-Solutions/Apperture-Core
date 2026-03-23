# Epic 41 — Config-hosted Governance UI and Role Assignment: Implementation Summary

**Date:** 2025-03-22  
**Scope:** Place Governance under Config, add user role-assignment UI, complete governance workflow.

---

## What Was Implemented

### 1. Information architecture: Config hosts Governance

- **Config layout** ([ConfigLayout.razor](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Layout/ConfigLayout.razor)): Tab bar (Governance), sub-nav (Permission Catalog, Roles, Users) when on governance paths.
- **Config landing** ([ConfigLanding.razor](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/ConfigLanding.razor)): `/config` page linking to Governance.
- **Governance landing** ([GovernanceLanding.razor](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Governance/GovernanceLanding.razor)): `/config/governance` overview with links to Catalog, Roles, Users.
- **Menu structure** ([ControlPanelMenuDefinitions.cs](src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ControlPanelMenuDefinitions.cs)): Governance removed from top-level; Config domain added with Governance and children (Permission Catalog, Roles, Users).
- **Nav rail**: Config domain uses ShieldCheckIcon; Config icon at bottom still navigates to `/config`.

### 2. Route structure

| Route | Page |
|-------|------|
| `/config` | ConfigLanding |
| `/config/governance` | GovernanceLanding |
| `/config/governance/catalog` | GovernanceCatalog |
| `/config/governance/roles` | RolesList |
| `/config/governance/roles/{RoleId}` | RoleDetail |
| `/config/governance/users` | UsersList |
| `/config/governance/users/{UserId}` | UserDetail (new) |
| `/config/governance/users/{UserId}/role-derived-access` | UserEffectiveAccess |

### 3. Route compatibility

- [GovernanceRedirects.razor](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Governance/GovernanceRedirects.razor): Old `/governance/*` routes redirect to `/config/governance/*`.

### 4. Management client and DTOs

- **IManagementClient / ManagementClient**:
  - `GetUserAsync(string userId)` → `GET api/users/{id}`
  - `AssignRoleAsync(string userId, string roleId)` → `POST api/users/{id}/roles`
  - `RemoveRoleAsync(string userId, string roleId)` → `DELETE api/users/{id}/roles/{roleId}`
- **DTOs**: `UserDetailDto`, `IdentityLinkDto` added for user detail and identity links.

### 5. Capability gating

- **CanAssignRoles**: New capability mapped to `role.assign` on Role.
- **OperatorCapabilitiesDto**: Extended with `CanAssignRoles`.
- **OperatorCapabilitiesQueryService**: Evaluates `ActionConstants.RoleAssign` for `CanAssignRoles`.
- **User detail**: Assign/remove controls shown only when `CanAssignRoles == true`.

### 6. User detail / role management page

- **UserDetail.razor** at `/config/governance/users/{UserId}`:
  - User identity (UserId, DisplayName)
  - Assigned roles with links to role detail and Remove (when `CanAssignRoles`)
  - Assign role: dropdown of unassigned roles and Assign button
  - Links: View role-derived access, Back to users
  - Notice: scope limited to role assignments; no user CRUD
  - Error/success feedback for assign/remove

### 7. Navigation and links

- **UsersList**: User ID and Display name link to user detail; “Manage roles” and “View role-derived access” in actions column.
- **UserEffectiveAccess**: “Manage role assignments” links to user detail.
- **RoleDetail**: Existing “Back to roles” updated to `/config/governance/roles`.

### 8. Truthfulness

- User detail notice: “This page manages role assignments only.”
- Role-derived access label and link kept.
- No claims about direct grants or full effective access.

---

## Risks Reduced

| Risk | Mitigation |
|------|------------|
| Governance treated as top-level domain | Governance under Config; menu hierarchy reflects this |
| No way to assign/remove roles | User detail with assign/remove UI |
| Assignment/removal without permission | `CanAssignRoles` gates controls |
| Unclear scope of user page | Notice clarifies role-assignment scope |

---

## Capability Gating

| Capability | Permission | Usage |
|------------|------------|-------|
| CanViewGovernance | user.read on Role | View governance pages |
| CanAssignPermissions | permission.assign on Role | Edit role permission sets |
| CanAssignRoles | role.assign on Role | Assign/remove roles on users |

---

## Limitations

1. **No user CRUD** – User creation and identity management remain outside this epic.
2. **No direct grants** – UI shows role-derived access only.
3. **Config menu visibility** – Config domain shown to all users; individual pages still enforce `CanViewGovernance`.
4. **System roles** – System roles remain assignable; Epic 40 only made role definition editing read-only for them.

---

## Follow-up Recommendations

- **Optional**: Narrow Config menu visibility to users with `CanViewGovernance` (or a broader “CanViewConfig”) to avoid exposing config areas to unauthorized users.
- **Optional**: Add a distinct governance-read permission and use it for `CanViewGovernance` instead of `user.read` on Role.
- **No corrective pass required** – Implementation aligns with Epic 40 standards and the planned IA.

---

## Tests

- **Epic41ConfigGovernanceUiTests.cs**: Menu structure, Governance under Config, user-detail copy, `UserDetailDto` shape, `CanAssignRoles` in capabilities.
- **Epic40GovernanceUiTests.cs**: Unchanged; existing tests still valid.
