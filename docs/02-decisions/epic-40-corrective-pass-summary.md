# Epic 40 Corrective Pass — Summary

**Date:** 2025-03-22  
**Scope:** Governance UI truthfulness, safety, and operator UX guardrails  
**Not a new epic.** Reuses Epic 39 backend APIs.

---

## What Was Changed

### 1. System roles read-only in UI

- **RoleDetail.razor**
  - Edit button condition now includes `!_detail.IsSystemRole` so system roles never show an edit affordance.
  - Added amber notice when `IsSystemRole == true`: *"System roles are not editable from this UI. Their permissions are managed by the platform."*
  - `StartEdit` refuses to enter edit mode if `_detail.IsSystemRole`.
  - `SaveEditAsync` refuses save if `_detail.IsSystemRole` (defense in depth).

### 2. Governance visibility capability mapping

- **OperatorCapabilitiesQueryService.cs**
  - Comment added: `CanViewGovernance` uses `user.read` on Role because there is no distinct governance-read permission. Backend governance endpoints use the same check. If a more specific governance-read permission is added later, the capability should be updated.
  - No change to behavior: platform does not define a narrower permission, so `user.read` on Role remains the gating check.
  - `CanAssignPermissions` remains `permission.assign` on Role.

### 3. Effective-access page truthfulness

- **UserEffectiveAccess.razor**
  - Page title changed from "Effective Access" to "Role-derived access".
  - Subtitle clarified: *"User {id} — merged permissions from assigned roles only"*.
  - Notice strengthened: *"This page shows merged permissions from assigned roles only. It is not a complete effective-access model: direct user grants, scope containment, and service principals are excluded."*
- **UsersList.razor**
  - Link text changed from "View effective access" to "View role-derived access".

### 4. Replace-all role editing safety

- **RoleDetail.razor**
  - `StartEdit` now reloads the role and catalog before entering edit mode; if either fails, edit mode is not entered.
  - Save button disabled when `_catalog is null`.
  - Save guard: returns early if `_detail` is null, `_detail.IsSystemRole`, or `_catalog` is null.
  - Warning copy updated: *"Saving replaces the role's entire permission set with the selections below. Permissions not selected will be revoked. Changes apply to the role as currently loaded."*
  - If catalog or role load fails in edit flow: error shown, message "Save is disabled until the catalog loads. Use Cancel to exit edit mode."
  - Cancel: exits edit mode and clears catalog reference; view reverts to last loaded state.

### 5. Operator comprehension and navigation

- **RoleDetail.razor**
  - Breadcrumb: `Roles / Role detail`
  - "Back to list" → "Back to roles"
- **UserEffectiveAccess.razor**
  - Breadcrumb: `Users / Role-derived access`
- **GovernanceCatalog.razor**
  - Context label: "Governance — Permission catalog"
- **RolesList.razor**
  - Context label: "Governance — Roles"
  - Subtitle: *"Platform roles and their permission grants. Use the permission catalog to browse available actions and resources."*
- **UsersList.razor**
  - Context label: "Governance — Users"
  - Subtitle: *"Users for role-derived access inspection. Use the effective-access link to see merged permissions from assigned roles."*

### 6. Tests

- **Epic40GovernanceUiTests.cs**
  - `RoleEditReplaceAllSemantics_MessageContainsReplaceAndRevoke`: updated for new warning text; added assertion for "currently loaded".
  - `EffectiveAccessLimitationMessage_DoesNotOverclaim`: updated for new notice text; added assertion for "not a complete effective-access model".
  - `SystemRoleReadOnlyNotice_ExplainsNonEditability`: new test asserting the system-role notice content.

---

## Risks Reduced

| Risk | Mitigation |
|------|------------|
| Admins editing system roles by mistake | Edit affordance hidden; edit mode blocked; explanatory copy |
| Misreading role-derived access as full effective access | Title, subtitle, and notice clarify scope |
| Accidental overwrite when editing role permissions | Reload before edit; save disabled if catalog failed; clear overwrite copy |
| Stale data used for save | Fresh load on entering edit mode |
| Fragmented governance UX | Breadcrumbs, context labels, and clearer links |

---

## Limitations That Remain (Require Backend Work)

1. **System role edits at API level**
   - UI blocks edits; backend still accepts them. Backend enforcement would require changes in the governance API.
2. **Governance visibility permission**
   - No dedicated governance-read permission. `CanViewGovernance` uses `user.read` on Role; anyone with role read can see governance UI.
3. **Concurrency / versioning**
   - No optimistic locking or version checks when saving role permissions. Last write wins.
4. **Role assignment UI**
   - No UI for assigning roles to users; that would be a follow-up epic.

---

## Follow-up Epic for Role Assignment UI

**Recommended:** Yes. A separate epic for role-assignment UI would be appropriate, given:

- Current governance UI supports catalog browsing, role inspection/editing, and role-derived access, but not role assignment.
- Assignment APIs (if present) are not surfaced in this UI.
- This pass stayed within scope and did not add user CRUD or role-assignment flows.

---

## Verification Note

Epic 40 unit tests were updated. The full unit test suite did not complete due to a pre-existing build error in `RunQueryServiceTests.cs` (unrelated to Epic 40). The Epic 40 tests are structurally correct and should pass once that build issue is resolved.
