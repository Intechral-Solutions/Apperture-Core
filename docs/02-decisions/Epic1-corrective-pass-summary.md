# Epic 1 Corrective Pass — Implementation Summary

## Scope

Narrow stabilization after Epic 1 audit: four work areas only. No Entra/local login flows, no password handling, no broad redesign.

## Deliverables

### 1. First platform user / first admin bootstrap

- **Endpoint:** `POST /api/Bootstrap/first-admin` (no `[Authorize]`). Request body: `{ "displayName", "identitySource": "Entra" | "Local", "externalId" }`.
- **Behavior:** Succeeds only when **zero** platform users exist. Creates one User, one IdentityLink, one UserRole (platform administrator) in a single transaction. Returns 201 with `userId`; 409 if users exist or duplicate link; 400/503 for bad input or missing role.
- **Surface:** `IFirstAdminBootstrapService` (Core.Application), `FirstAdminBootstrapService` (ControlPlane.Security), `BootstrapController` (Management.Api). `IUserRepository.GetUserCountAsync`; `BootstrapConstants.PlatformAdminRoleId` in Core.Application.

### 2. Enrollment endpoint protection

- **CreateInvitation** (`POST /api/Enroll/invitation`): Requires authenticated principal that resolves to a platform user with role `role-platform-administrator`. Otherwise 401 (no actor) or 403 (no user / not admin). EnrollController injects `IPrincipalContext`, `IPrincipalToUserResolver`, `IRoleProvider`; no change to `IRuntimeEnrollmentService`. **Enroll** (token-based) unchanged.

### 3. Identity-link ExternalId and source resolution

- **ExternalId:** Entra = stable subject only (`oid` or `sub`); Local = local username or stable local id. Documented in [next-stage-platform-auth.md](../01-architecture/next-stage-platform-auth.md) and mapper/resolver comments.
- **HumanActor.PreferredIdentitySource:** Optional. When set (e.g. from claim `http://schemas.apperture/identity_source`), resolver uses only that source.
- **ClaimsPrincipalToActorMapper:** Id derivation order: oid (Entra stable), NameIdentifier, sub, name, preferred_username. Reads `identity_source` claim; Epic 2 handlers should set it.
- **LastAuthenticatedAt:** `IUserRepository.UpdateLastAuthenticatedAtAsync(source, externalId, at)`. Called from `SqlPrincipalToUserResolver` after successful resolve (preferred source only when set; both Entra and Local when null).

### 4. Referential integrity and persistence hardening

- **Foreign keys (Restrict):** IdentityLinks.UserId → Users.Id; UserRoles.UserId → Users.Id; UserRoles.RoleId → Roles.Id.
- **Roles:** Unique index on Name; RowVersion for concurrency.
- **Migration:** `AddPlatformIdentityReferentialIntegrity` adds FKs, unique index, Role.RowVersion.

## Remaining for Epic 2

- Real auth schemes (Entra, local) replacing placeholder.
- Setting `identity_source` claim in auth handlers.
- Password/local credential storage and login flows.
- Broader admin APIs for user/link/role management.
- Optional: require authenticated admin for Enroll (token-only remains for this pass).

## Files touched

- **Core.Application:** BootstrapConstants, IUserRepository (GetUserCountAsync, UpdateLastAuthenticatedAtAsync), IFirstAdminBootstrapService, FirstAdminBootstrapResult.
- **Persistence:** SqlUserRepository (new methods), AppertureDbContext (FKs, Role name unique, Role RowVersion), RoleEntity (RowVersion), migration AddPlatformIdentityReferentialIntegrity.
- **ControlPlane.Security:** FirstAdminBootstrapService, HumanActor (PreferredIdentitySource), ClaimsPrincipalToActorMapper (oid first, identity_source claim), SqlPrincipalToUserResolver (PreferredIdentitySource, UpdateLastAuthenticatedAtAsync), ServiceCollectionExtensions (register IFirstAdminBootstrapService).
- **Management.Api:** BootstrapController, EnrollController (protection on CreateInvitation).
- **Docs:** next-stage-platform-auth.md, this summary.
