# Platform Administrator role and bootstrap assignment

## Summary

The **Platform Administrator** role (`role-platform-administrator`, display name *Platform Administrator*) is the system-managed, seeded top-tier operator role. It is created by EF migrations with `IsSystemRole = true` and is **not** editable from the governance UI (existing Epic 40 behavior).

## Permission repair (startup)

`PlatformAdminRoleRepairService` runs on every Management host startup and merges required grants into the role (deduplicated). Logic lives in `PlatformAdminRolePermissionRepair` (unit-tested) and includes:

- **Auth admin (Epic 3):** `user.read`, `user.manage`, `role.assign`, `credential.manage` on `User` / `Role` as defined in code.
- **Governance UI / catalog:** `user.read` and `permission.assign` on **`Role`** at `instance` scope.  
  These match `GovernanceController` and `OperatorCapabilitiesQueryService` (there is no separate `governance.read` permission today).
- **Operator console:** module configure, workflow execute/start, schedules, recovery, audit read, runtime health (as in previous repair lists).
- **Module-scoped permissions:** any `module:*` permissions registered in `IResourceDescriptorRegistry` at startup.

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

`appsettings.Development.example.json` includes a sample `BootstrapPlatformAdmin` block (and `DevTestUser` for local **`tenny`**). **`appsettings.Development.json` is gitignored** — copy from the example; see `Apperture.Management.Host/README-Development.md`.

## Related

- `BootstrapConstants.PlatformAdminRoleId`
- Last-admin removal / credential guards: `UserManagementService`
- First interactive admin when DB has zero users: `IFirstAdminBootstrapService` / bootstrap API
- Dev-only convenience user: `DevTestUser` (Development)
