# Platform Administrator role and bootstrap assignment

## Summary

The **Platform Administrator** role (`role-platform-administrator`, display name *Platform Administrator*) is the system-managed, seeded top-tier operator role. It is created by EF migrations with `IsSystemRole = true` and is **not** editable from the governance UI (existing Epic 40 behavior).

## Permission repair (startup)

`PlatformAdminRoleRepairService` runs on every Management host startup, after the platform catalog is seeded and the module permissions are registered, and before the first administrator is assigned. It is one **audited unit of work**: it reads the role under `UPDLOCK, HOLDLOCK` (the lock an assignment of that role takes), decides from the row as locked, and commits the change together with one `governance.authorization.administrator_role_repaired` summary row. A start that finds the role already correct writes nothing. Logic lives in `PlatformAdminRolePermissionRepair.Plan` (unit-tested). The stored set ends up as:

- **Every registered permission whose definition is an administrator default** (`GrantedToPlatformAdministratorByDefault`), by its own resource type and scope: platform permissions at instance scope, environment permissions at the real environment's scope, and every registered module permission at its module scope. `relationship.manage` is in the catalog but is not an administrator default, so it is not added.
- **The fixed grants of `PlatformAdminRolePermissionRepair`**: the eight combinations of the administrator floor and `workflow.execute` on WorkflowInstance at instance scope, the one stored grant that is deliberately not a catalog permission (it lets the administrator satisfy every module-scoped check by containment).
- **Whatever else the role already holds.** Only the approved obsolete list is removed: `user.read` on Role (at any scope), which nothing checks any more.

Stored permissions that cannot be read are never overwritten: the repair stops with `platform_administrator_role_permissions_unreadable` and the host does not start. Restore a readable value for the role's `PermissionsJson` (or a backup) and start again.

## Bootstrap principal assignment

Configuration section: **`BootstrapPlatformAdmin`** (see `BootstrapPlatformAdminOptions`).

- **`Principals`:** list of `{ "IdentitySource": "Entra"|"Local", "ExternalId": "...", "DisplayName": "..." }`.
  - **Entra:** `ExternalId` must be the stable **oid/sub** from the token, not email/UPN.
  - **Local:** `ExternalId` is normalized (trim + lowercase) like login.
- **`CreateUserIfMissing`:** when `true` (default), creates a platform user with the identity link and Platform Administrator role if no user matches.
- **`Enabled`:** master switch.

`BootstrapPlatformAdminAssignmentHostedService` runs after `PlatformAdminRoleRepairService` and calls `BootstrapPlatformAdminAssignment.ExecuteAsync` (Moq-tested). Assignments use `IRoleRepository.AssignToUserAsync` (same persistence as normal assignments). **API-driven** `AssignRoleAsync` still emits governance audit events; bootstrap assignment is logged only (see remarks on the hosted service). A follow-up may add durable audit rows if product requires it.

## Host wiring

`Apperture.Management.Host` registers:

```csharp
builder.Services.Configure<BootstrapPlatformAdminOptions>(
    builder.Configuration.GetSection(BootstrapPlatformAdminOptions.SectionName));
```

`deploy/config/examples/appsettings.docker-dev.json.example` includes a sample `BootstrapPlatformAdmin` block (and `DevTestUser` for local **`tenny`**). **`appsettings.Development.json` is gitignored** — copy from that template; see [management-platform-configuration.md](../03-operations/management-platform-configuration.md) and `Apperture.Management.Host/README-Development.md`.

## First-admin bootstrap API (Epic S2A)

Anonymous endpoints on `POST /api/bootstrap/first-admin` and `GET /api/bootstrap/status`.

### v1 initialization gate

- **Initialized** when at least one platform user exists (`GetUserCountAsync() > 0`).
- There is **no** durable initialization flag or migration in v1.
- **Wiping or reseeding the user tables** (or restoring an empty database) reopens first-admin bootstrap.
- A full first-run wizard is **deferred**; scripts and operators use the bootstrap API directly.

### Expected behavior

| State | `GET /api/bootstrap/status` | `POST /api/bootstrap/first-admin` |
|-------|-----------------------------|-----------------------------------|
| No users | `initialized: false`, `allowsFirstAdmin: true` | **201 Created** when request is valid |
| At least one user | `initialized: true`, `allowsFirstAdmin: false` | **409 Conflict**, code `bootstrap.already_initialized` |

Audit events (platform audit):

- `bootstrap.first_admin.created`
- `bootstrap.first_admin.rejected_already_initialized`
- `bootstrap.first_admin.rejected_invalid_request`

### Related bootstrap surfaces (not the same API)

- **`BootstrapPlatformAdmin`** — config-driven principal assignment on host startup (see above).
- **`DevTestUser`** — Development-only convenience user.

## Related

- `BootstrapConstants.PlatformAdminRoleId`
- Last-admin removal / credential guards: `UserManagementService`
- First interactive admin when DB has zero users: `IFirstAdminBootstrapService` / bootstrap API
- Dev-only convenience user: `DevTestUser` (Development)
