# Epic 41 — Config-hosted Governance UI and Role Assignment

## Status

Implemented.

## Purpose

Epic 41 completes the missing user↔role management loop in the governance/admin experience while correcting the information architecture so Governance is hosted under **Config** instead of being treated as a standalone top-level domain.

This epic builds on:

- **Epic 39** backend governance APIs
- **Epic 40** governance UI and corrective pass
- the decision that Governance belongs within the **Config** area of the platform

The result is a more coherent configuration surface where operators can:

- navigate to **Config**
- enter **Governance**
- browse users
- inspect a user’s current role assignments
- assign roles to a user
- remove roles from a user
- inspect the user’s role-derived access
- navigate to relevant role detail pages

---

## Goals

The goals of Epic 41 were to:

1. Move Governance under **Config** in the menu and page structure
2. Preserve Governance as a multi-page admin surface rather than collapsing it into a single page
3. Add a dedicated **user detail / role management** page
4. Support **assigning** and **removing** roles for users
5. Keep wording and behavior truthful:
   - role assignment is distinct from role definition editing
   - role-derived access is not overstated as full effective access
6. Keep the implementation aligned with the existing backend and corrected Epic 40 UI patterns

---

## Summary of implementation

### 1. Management client and DTO support

The management client was extended to support user-level governance workflows.

#### Added client methods

- `GetUserAsync`
- `AssignRoleAsync`
- `RemoveRoleAsync`

These methods provide the minimum governance client surface needed for:

- loading a user governance detail view
- assigning a role to a user
- removing a role from a user

#### Added DTOs

- `UserDetailDto`
- `IdentityLinkDto`

These DTOs support the new user detail page and allow the UI to show available user identity information without implying richer identity/profile data than the backend actually provides.

---

### 2. Capability gating

A new operator capability was added:

- `CanAssignRoles`

This capability is derived from:

- `role.assign` on `Role`

This allows the UI to distinguish between:

- operators who may **view** governance/user information
- operators who may **mutate** user-role assignments

The result is that assign/remove controls are only presented when the current operator has the corresponding assignment capability.

This keeps the UI honest and prevents mutation affordances from appearing where the operator should only be able to inspect.

---

### 3. Config-hosted information architecture

Epic 41 moved Governance into the **Config** area of the site.

#### New/updated Config structure

- `ConfigLanding.razor` at `/config`
- `GovernanceLanding.razor` at `/config/governance`
- `ConfigLayout.razor` introduced to host Config-level framing/tab behavior

#### Menu changes

- Governance was removed as a standalone top-level domain
- A new **Config** domain/menu area was introduced
- Governance and its child pages now appear under Config
- The nav rail icon mapping was updated to support Config

This was the key IA correction in the epic.

The site now teaches the intended mental model:

- Governance is a **configuration concern**
- Config is the owning area
- Governance can still contain multiple specialized subpages

---

### 4. Governance route migration

Governance pages were moved under Config-owned routes.

#### New route shape

Governance pages now live under:

- `/config/governance/catalog`
- `/config/governance/roles`
- `/config/governance/roles/{RoleId}`
- `/config/governance/users`
- `/config/governance/users/{UserId}`
- `/config/governance/users/{UserId}/effective-access`
- `/config/governance`

#### Page/layout updates

- governance pages now use `ConfigLayout`
- breadcrumbs were updated to follow the Config-owned hierarchy:
  - `Config — Governance — ...`
- internal links were updated to the new routes

#### Compatibility support

A redirect surface was added:

- `GovernanceRedirects.razor`

This preserves old `/governance/*` entry points by redirecting them to the new `/config/governance/*` routes.

This reduces churn while allowing the new IA to become canonical.

---

### 5. Governance landing page

A governance landing page was added at:

- `/config/governance`

This page provides an overview and entry points into the Governance section.

Its purpose is to:

- reinforce that Governance is a Config-owned area
- give operators a stable starting point for governance tasks
- link into the specialized governance pages

This helps avoid the feeling that Governance is just a collection of disconnected admin screens.

---

### 6. User detail / role management page

The main functional addition in Epic 41 is:

- `UserDetail.razor`
- route: `/config/governance/users/{UserId}`

This page becomes the primary user-centered governance screen.

#### What it shows

- user identity/display information returned by the backend
- assigned roles
- links from assigned roles to role detail pages
- link to the user’s **Role-derived access** page
- link back to the Users list

#### What it allows

When the operator has `CanAssignRoles`:

- assign a role to the user
- remove a role from the user

This closes the key gap left after Epic 40.

Before Epic 41, the governance area could inspect roles and inspect user role-derived access, but it could not actually manage the user↔role relationship. Epic 41 completes that core loop.

---

### 7. Role assignment flow

The UI now supports assigning roles to users.

#### Expected behavior

- the operator can choose from available roles
- roles already assigned to the user are not misleadingly presented as newly assignable
- on success, the page refreshes to show the updated state
- success/failure messaging is clear
- the UI does not silently fail

The implementation favors a simple, honest operator flow instead of a more elaborate interaction model.

---

### 8. Role removal flow

The UI now supports removing roles from users.

#### Expected behavior

- assigned roles can be explicitly removed
- removal is clearly presented as a role-assignment action
- state refreshes after the action
- success/failure feedback is shown

This is intentionally kept separate from role definition editing. The user page manages the **relationship** between a user and roles, not the contents of a role definition.

---

### 9. Users list updates

The users list was updated to support the new governance flow.

#### Changes

- user ID and display name now link to the new user detail page
- a **Manage roles** link was added

This gives operators a direct path from the users list into role-assignment work.

---

### 10. Role-derived access page linkage

The existing role-derived access page was updated to support the completed governance workflow.

#### Changes

- added a **Manage role assignments** link back to the new user detail page

This allows the operator to move naturally between:

- “What role-derived access does this user currently have?”
- “What roles are currently assigned?”
- “What should I add or remove?”

This improves coherence without changing the truthfulness boundary established in Epic 40.

---

## Truthfulness and UX decisions

Epic 41 intentionally preserves the truthfulness standards established in Epic 40.

### 1. Governance remains under Config

The UI now teaches that Governance is part of Config rather than a peer business domain.

### 2. Role assignment is separate from role definition editing

The user detail page does **not** edit role permissions inline.

If an operator needs to inspect or edit the role definition itself, they must navigate to the role detail page.

### 3. Role-derived access wording is preserved

The user workflow continues to use **Role-derived access** wording rather than regressing to ambiguous “effective access” terminology.

### 4. User identity is only shown to the extent actually supported by the API

The new user detail page does not invent a richer user profile model than the backend provides.

---

## Files created

- `Layout/ConfigLayout.razor`
- `Pages/Config/ConfigLanding.razor`
- `Pages/Config/Governance/GovernanceLanding.razor`
- `Pages/Config/Governance/UserDetail.razor`
- `Pages/Governance/GovernanceRedirects.razor`
- `tests/Apperture.UnitTests/Governance/Epic41ConfigGovernanceUiTests.cs`
- `docs/02-decisions/epic-41-implementation-summary.md`

---

## Files updated

Representative updated areas include:

- `IManagementClient`
- `ManagementClient`
- governance DTO definitions
- `OperatorCapabilitiesDto`
- `OperatorCapabilitiesQueryService`
- existing Governance pages and internal links
- menu / nav structure and icon map
- users list and role-derived access pages

---

## Testing

A new test file was added:

- `Epic41ConfigGovernanceUiTests.cs`

The test coverage focuses on:

- Config-hosted governance information architecture
- menu structure and placement
- governance/UI truthfulness
- route/page framing
- role-assignment-related UI expectations

### Current build caveat

The full unit test project did not complete due to an existing unrelated build error in:

- `RunQueryServiceTests.cs`
- issue involving `IWorkflowDefinitionResolver`

However:

- the management UI client builds successfully
- the Epic 41 governance tests are in place for validation once the unrelated build issue is resolved

---

## What changed conceptually

Before Epic 41:

- Governance existed as a visible admin area
- role detail editing existed
- user role-derived access inspection existed
- but the user↔role assignment loop was incomplete
- Governance still appeared as a more standalone area than intended

After Epic 41:

- Governance is hosted under Config
- Config is the owning section
- Governance still supports multiple specialized subpages
- the user detail page completes user-role management
- the operator can now inspect and change user-role assignments
- the governance/admin experience is substantially more coherent

---

## Remaining limitations

Epic 41 intentionally does **not** include:

- user CRUD
- direct user permission grants
- a broader redesign of unrelated Config concerns
- richer dashboard/config shell unification beyond what was needed for Governance
- deeper identity/profile management beyond the backend user detail surface
- component-level UI tests for all shell interactions

These remain acceptable out of scope for this epic.

---

## Follow-up recommendations

### 1. Shell/nav alignment follow-up
Because later shell work introduced custom top-logo and gear section-owner behavior, the Config placement and control-panel behavior should continue to be checked for consistency as the shell evolves.

### 2. Auth/application shell stabilization
A focused shell/auth UX pass is still valuable for:
- current user/profile menu
- theme switching
- alert/toast infrastructure
- predictable shell behavior

### 3. Dashboard/admin shell follow-up
Dashboard/home and Config are likely to benefit from a more intentional multi-zone shell model as the platform grows.

### 4. Test hygiene follow-up
Resolve the unrelated failing test/build issue so the full suite can validate the Epic 41 governance additions.

---

## Overall assessment

Epic 41 successfully completes the core governance workflow that was still missing after Epic 40.

It does two important things:

1. **Corrects the information architecture** by hosting Governance under Config
2. **Completes the operator workflow** by adding user-role assignment/removal

This makes the governance surface more coherent, more actionable, and more aligned with the intended platform model.

Epic 41 should be considered a successful implementation, with later shell/navigation refinements handled as follow-on work rather than as a reason to revisit the governance design itself.