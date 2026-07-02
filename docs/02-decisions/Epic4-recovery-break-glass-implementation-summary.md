# Epic 4: Recovery, Break-Glass, and Security Hardening — Implementation Summary

## v1 status (Epic S2B)

Break-glass **operator login** (`POST /api/auth/break-glass`) is **disabled by default** (`BreakGlassAuth:Enabled = false`). The endpoint returns **404** with `auth.break_glass.disabled`. No users are break-glass eligible by default. SQL/bootstrap recovery (`RestorePlatformAccess`, CLI recovery-unlock) remains available and is **separate** from break-glass login.

## Status

Implemented (Epic 4). Idempotent platform-admin permission seed repair at startup, lockout-prevention guards for last platform administrator, real break-glass sign-in path, recovery authorization and RestorePlatformAccess execution, hardened audit for blocked operations and break-glass/recovery, and narrow Entra external-id validation.

## Permission seed repair (auth-admin robustness)

- **Problem:** The Epic 3 migration appends four auth-admin permissions to the platform-admin role via `JSON_MODIFY(..., 'append $', ...)`. Re-running or drift can duplicate entries.
- **Approach:** No change to the existing migration. A **startup repair** runs on the host that has DbContext and governance.
- **IRoleRepository:** Added `UpdatePermissionsAsync(RoleId, IReadOnlyList<PermissionDescriptor>, CancellationToken)` and `CountUserAssignmentsAsync(RoleId, CancellationToken)`. Implemented in `SqlRoleRepository` (load role, replace PermissionsJson, SaveChanges; count UserRoles by roleId).
- **PlatformAdminRoleRepairService:** `IHostedService` in ControlPlane.Security. In `StartAsync`, loads role `BootstrapConstants.PlatformAdminRoleId`, deduplicates permissions by (Action, Resource?.ResourceType, Resource?.ScopeKey), ensures the four auth-admin permissions exist exactly once, then calls `UpdatePermissionsAsync` if the list changed. Idempotent and safe to run every startup.
- **Registration:** Registered in `ServiceCollectionExtensions.Governance` (AddAppertureGovernance); runs when Management host starts.

## Platform-admin lockout prevention

- **Definition:** "Last platform admin" = exactly one user in the instance has the role `BootstrapConstants.PlatformAdminRoleId`. Enforcement is in **UserManagementService** (service layer).
- **RemoveRoleAsync:** If `roleId == PlatformAdminRoleId` and `CountUserAssignmentsAsync(roleId) <= 1`, the operation is blocked. Audit `auth_admin.blocked.last_platform_admin_removal`, then throw `InvalidOperationException` ("Cannot remove the last platform administrator role assignment."). API returns 400 with that message.
- **SetLocalCredentialDisabledAsync(disabled: true):** If the target user is the only platform admin (count == 1 and user has that role) and has only a Local identity link, blocking their local credential would remove their only sign-in path. Block, audit `auth_admin.blocked.last_admin_credential_disabled`, throw `InvalidOperationException` with a clear message. API returns 400.
- **Not blocked:** Removing an identity link when the user would have zero links is already enforced in `SqlUserRepository.RemoveIdentityLinkAsync`; no extra guard for "last admin's last link" beyond that.

## Break-glass execution path

- **What it is:** User-based, local-auth-only recovery path. Users with `User.IsBreakGlassEligible == true` can sign in via a **dedicated endpoint** (not the normal local login).
- **Endpoint:** `POST /api/auth/break-glass` body `{ username, password }`. Same normalization as local login. Look up user by Local identity link; verify password and credential not disabled; load user and require `user.IsBreakGlassEligible`. If not eligible or auth fails, return 401 and audit `auth.break_glass.login.failed` (no user-existence hint). If eligible and auth succeeds, sign in with cookie and break-glass claim `http://schemas.apperture/break_glass` = `"1"`, audit `auth.break_glass.login.success`.
- **Actor:** `HumanActor` (ControlPlane.Security) has `IsBreakGlassSession`. `ClaimsPrincipalToActorMapper` sets it when the principal has the break-glass claim. `HostPrincipalContext` uses the mapper, so the evaluator receives the flag.
- **Privileges:** In `RbacAuthorizationPolicyEvaluator`, when actor is Human and `IsBreakGlassSession` is true, the evaluator returns `true` for the permission check (grant). Only Human actors; Machine/System/RemoteInstance do not get break-glass.
- **Restrictions:** Break-glass is local-only (no Entra break-glass in this epic). Hard to use accidentally (separate endpoint, eligible account only). Document as recovery-only; monitor usage.

## Recovery flow completion

- **Recovery vs break-glass:** Recovery = operational process (e.g. run recovery-unlock from CLI or call the service). Break-glass = how to get in (break-glass-eligible user signs in via break-glass endpoint). Recovery can **restore** platform access (e.g. assign platform-admin to a user); break-glass is one way to **use** access once restored.
- **BootstrapRecoveryRequest:** Extended with optional `TargetUserId`, `TargetExternalId`, and `TargetIdentitySource` for RestorePlatformAccess (target user by id or by identity link).
- **Real authorizer:** `ConfigRecoveryAuthorizer` (ControlPlane.Security) implements `IBootstrapRecoveryAuthorizer`. Reads `Auth:RecoveryToken` or `Bootstrap:RecoveryToken` from `IConfiguration`. For `RestorePlatformAccess`, authorizes when request proof (as string) equals the configured token; otherwise denies. No secret in audit.
- **Real recovery service:** `RealBootstrapRecoveryService` (ControlPlane.Security) implements `IBootstrapRecoveryService`. Depends on `IBootstrapRecoveryAuthorizer`, `IBootstrapAuditWriter`, and `IServiceScopeFactory` (to resolve scoped repositories). For RestorePlatformAccess when authorized: resolve target user by `TargetUserId` or by `TargetExternalId` + `TargetIdentitySource` (Local/Entra); if not found, write recovery audit Outcome Failed and return; else `IRoleRepository.AssignToUserAsync(targetUserId, PlatformAdminRoleId)`, write recovery audit Outcome Executed, optionally append to `IAuditService` event `recovery.restore_platform_access.executed` with targetUserId.
- **Registration:** `AddAppertureRecovery()` in ControlPlane.Security registers `ConfigRecoveryAuthorizer` and `RealBootstrapRecoveryService` as singletons (replacing Bootstrap stubs when called after AddAppertureBootstrap and AddAppertureGovernance). Management host calls `AddAppertureRecovery()` after `AddAppertureGovernance()`. CLI can use the same when running recovery-unlock with a provider that has persistence and recovery registered.

## Audit events (new or updated)

- **Blocked operations:** `auth_admin.blocked.last_platform_admin_removal`, `auth_admin.blocked.last_admin_credential_disabled` (IAuditService.AppendAsync, Outcome auth-admin; minimal details).
- **Break-glass:** `auth.break_glass.login.success`, `auth.break_glass.login.failed` (no user-existence hint on failure). **Corrective pass:** `auth.break_glass.admin_operation` when a break-glass session performs an auth-admin mutation (user create, identity-link add/remove, role assign/remove, credential set/disable); emitted by UserManagementService, Outcome auth.
- **Recovery:** Existing `IBootstrapAuditWriter.WriteRecoveryAsync` (Outcome: Denied, Authorized, Executed, Failed). Platform audit (durable): `recovery.restore_platform_access.executed`, **corrective pass** `recovery.restore_platform_access.denied`, and `recovery.restore_platform_access.failed` so all outcomes are visible in AuditLogs even when the bootstrap audit writer is stubbed.

## Entra external-id validation

- **Narrow validation:** In `UserManagementService`, for `CreateUserAsync` and `AddIdentityLinkAsync` when `source == IdentitySource.Entra`: reject null/whitespace `externalId` with message that Entra external id must be the stable subject (oid/sub). Reject if `externalId` contains `@` with message that Entra external id must be the stable subject (oid/sub), not an email or UPN. Implemented in both methods via `ValidateEntraExternalId`.

## Files touched (summary)

- **Core.Application:** IRoleRepository (CountUserAssignmentsAsync, UpdatePermissionsAsync).
- **Persistence.SqlServer:** SqlRoleRepository (implement new methods; SerializePermissions for UpdatePermissionsAsync).
- **Bootstrap:** BootstrapRecoveryRequest (TargetUserId, TargetExternalId, TargetIdentitySource).
- **ControlPlane.Security:** PlatformAdminRoleRepairService, ConfigRecoveryAuthorizer, RealBootstrapRecoveryService, HumanActor (IsBreakGlassSession), ClaimsPrincipalToActorMapper (BreakGlassClaimType, isBreakGlassSession), RbacAuthorizationPolicyEvaluator (break-glass grant), UserManagementService (lockout guards, IPrincipalToUserResolver, ValidateEntraExternalId), ServiceCollectionExtensions.Governance (AddAppertureRecovery, HostedService).
- **Management.Api:** AuthController (POST break-glass, audit constants).
- **Management.Host:** Program.cs (AddAppertureRecovery).
- **Docs:** next-stage-platform-auth.md, authorization-and-governance-foundation.md, bootstrap-recovery-and-break-glass.md, Epic4-recovery-break-glass-implementation-summary.md (this file).

## Corrective pass (post–Epic 4)

A narrow visibility pass was completed after Epic 4:

- **Durable recovery audit:** Denied and failed recovery attempts are now written to **platform audit** (IAuditService) in addition to IBootstrapAuditWriter. Actions: `recovery.restore_platform_access.denied` (details = authorizer reason, no proof), `recovery.restore_platform_access.failed` (target user not found). Executed continued to use `recovery.restore_platform_access.executed`. All three outcomes are thus durably visible in AuditLogs.
- **Break-glass-use visibility:** When a break-glass session performs an auth-admin mutation in UserManagementService (user create, identity-link add/remove, role assign/remove, credential set/disable), the service appends `auth.break_glass.admin_operation` with details `operation={name}`. Reads and non–auth-admin operations do not emit this event.

**Remains deferred:** Real IBootstrapAuditWriter implementation; per-request or platform-wide authorization audit; break-glass visibility for non–auth-admin operations; shorter break-glass session lifetime or step-up reauth. See [Epic 4 corrective pass summary](Epic4-corrective-pass-summary.md).

## Remaining for later

- Broad admin UI, self-service password reset, SCIM/Entra sync, module permission contribution, broad observability dashboards, advanced federation, runtime trust redesign, relationship authorization expansion, reauth UX for all sensitive actions, full Entra external-id validation beyond the narrow check. CLI recovery-unlock wiring (build request with target user and proof from config/env) when running with DB.

## Related

- [Next Stage: Platform Auth Readiness](../01-architecture/next-stage-platform-auth.md)
- [Authorization and Governance Foundation](../01-architecture/authorization-and-governance-foundation.md)
- [Bootstrap Recovery and Break-Glass](../01-architecture/bootstrap-recovery-and-break-glass.md)
- [Epic 4 corrective pass summary](Epic4-corrective-pass-summary.md)
