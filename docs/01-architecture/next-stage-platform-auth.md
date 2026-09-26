# Next Stage: Platform Auth Readiness

This document describes the **platform authentication** foundation (Epic 1) and what remains for operator auth, control-plane API auth, and later phases.

## Principles

- Platform auth builds **on top of** the current boundaries. It must not collapse or conflate:
  - **Bootstrap** (SQL credential and config; first-use)
  - **Runtime Trust** (enrollment, runtime identity; distinct from generic secrets)
  - **Operational Secret Management** (ISecretStore, ISecretResolver; runtime secrets)
- Bootstrap and runtime trust remain distinct from generic operational secret storage. Auth plugs in at control-plane and host boundaries.

## What is implemented (Epic 1 + corrective pass + Epic 2)

- **Persisted platform identity:** Tables `Users`, `Roles`, `IdentityLinks`, `UserRoles` in SQL Server (migrations `AddPlatformIdentityTables`, `AddPlatformIdentityReferentialIntegrity`). Foreign keys (Restrict), unique Role.Name, Role.RowVersion. Domain types unchanged (User, Role, IdentityLink, IdentitySource).
- **Real IPrincipalContext:** `HostPrincipalContext` resolves `HttpContext.User` (ClaimsPrincipal) to `IActor` via provider-agnostic claim mapping. For Id (ExternalId): **oid** first (Entra stable), then NameIdentifier, sub, name, preferred_username. Human actors only.
- **Real IPrincipalToUserResolver:** `SqlPrincipalToUserResolver` resolves a Human actor to `UserId` using `IdentityLinks` (Source + ExternalId). When `HumanActor.PreferredIdentitySource` is set (from `identity_source` claim), uses only that source; otherwise tries Entra then Local. After successful resolve, updates `LastAuthenticatedAt` on the matching link(s).
- **Identity-link ExternalId semantics:** **Entra:** use stable subject only—`oid` or `sub` (not `preferred_username` or email). **Local:** use **normalized username** (trim + lowercase) as `IdentityLink.ExternalId`; same value is used for first-admin bootstrap and for local login. Documented in `LocalAuthHelpers.NormalizeUsername` and resolver.
- **Real IRoleProvider:** `SqlRoleProvider` backed by `IRoleRepository`; roles and user-role assignments are persisted. Seeded role `role-platform-administrator` (Platform Administrator) with a minimal permission set.
- **Real auth schemes (Epic 2):** Placeholder scheme removed. **Cookie** is DefaultAuthenticateScheme and DefaultSignInScheme. **Entra:** OpenID Connect (authorization code) with `identity_source` = Entra set in `OnTokenValidated`. **Local:** `POST /api/auth/local/login` (username, password) validates against `IdentityLinks` (Local, normalized username) + `LocalCredentials` table, then signs in with Cookie and sets `identity_source` = Local. Sign-in/sign-out: `GET /signin` (challenges OIDC when Entra enabled; otherwise 401), `POST /signout` (clears cookie; a form POST from the trusted UI origin, SEC-1B2).
- **Configuration:** **Entra:** section `Entra` (or `Authentication:Entra`): `Enabled`, `Authority`, `ClientId`, `ClientSecret`, `CallbackPath`, `Scopes`. Validated at startup when Enabled. **Local:** section `LocalAuth`: `Enabled`, `MinPasswordLength`. When Entra enabled, DefaultChallengeScheme = OpenIdConnect; when only Local, challenge returns 401 (client uses POST local/login).
- **Local credential storage:** Table `LocalCredentials` (UserId FK, PasswordHash, Salt, Algorithm, IsDisabled, RowVersion). One row per user for local auth. PBKDF2-HMAC-SHA256 via `ILocalPasswordHasher`; do not use ISecretStore for user passwords.
- **First-admin bootstrap:** `POST /api/Bootstrap/first-admin` (no auth) creates the first platform user, one identity link, and assigns `role-platform-administrator` when zero users exist. Request: `{ "displayName", "identitySource": "Entra"|"Local", "externalId" [, "password" ] }`. When `identitySource` is **Local**, optional **password** creates the local credential (hashed); if omitted, first local admin has no password until a future "set password" path exists. For Local, `externalId` is normalized (trim, lowercase) before creating the link.
- **Enrollment protection:** `POST /api/Enroll/invitation` requires an authenticated principal that resolves to a platform user with the platform administrator role; otherwise 401/403. **Endpoint protection:** `[Authorize]` on ConnectionsController, BindingsController, EnrollController, WorkflowsController, RunsController, ModulesController; `[AllowAnonymous]` on Bootstrap first-admin, Enroll POST (token), Auth local/login (the unused local/logout was removed in SEC-1B2). GET `/api/auth/me` returns current user (userId, displayName, roles) or 403 if unlinked.
- **Auth audit:** `IAuditService` implemented as `SqlAuditService` (writes to AuditLogs). Auth events: `auth.entra.login.success`, `auth.local.login.success`, `auth.local.login.failed`, `auth.unlinked_principal`. See [Epic 2 implementation summary](../02-decisions/Epic2-human-auth-implementation-summary.md).
- **Break-glass (Epic 4):** User-based, local-auth-only recovery path. Users with `User.IsBreakGlassEligible == true` can sign in via `POST /api/auth/break-glass` (username, password). On success, the session receives a break-glass claim; the RBAC evaluator grants all permission checks for that session. Audit: `auth.break_glass.login.success`, `auth.break_glass.login.failed` (no user-existence hint on failure). Use only for recovery; monitor usage.
- **Recovery (Epic 4):** Real `IBootstrapRecoveryAuthorizer` (config token: `Auth:RecoveryToken` or `Bootstrap:RecoveryToken`) and `IBootstrapRecoveryService` in ControlPlane.Security. For action `RestorePlatformAccess`, request proof must match configured token; then the service assigns `role-platform-administrator` to the target user (identified by `TargetUserId` or `TargetExternalId` + `TargetIdentitySource`). Bootstrap recovery audit plus optional platform audit `recovery.restore_platform_access.executed`. See [Epic 4 implementation summary](../02-decisions/Epic4-recovery-break-glass-implementation-summary.md).
- **Lockout prevention (Epic 4):** In `UserManagementService`: removing the last platform administrator role assignment is blocked and audited (`auth_admin.blocked.last_platform_admin_removal`). Disabling the only local credential for the last platform admin who has no other sign-in path is blocked and audited (`auth_admin.blocked.last_admin_credential_disabled`). API returns 400 with a clear message.
- **Self-service account surface (AUP-1):** `AccountController` (`api/account/*`) exposes profile, personal preferences, avatar, and notification preferences to the signed-in operator. Every action resolves the acting user from the authentication cookie via `IPrincipalToUserResolver`; **no route, query string, or body accepts a user id**, so no request shape lets one operator read or mutate another's account. An authenticated but unlinked principal receives 403, matching `GET /api/auth/me`. Self-service required no new permission constant: `[Authorize]` plus resolver-derived scoping is the pattern, following `MeNotificationSubscriptionsController`. Account audit actions: `account.profile.display_name.updated`, `account.profile.avatar.updated`, `account.profile.avatar.reset`, `account.profile.avatar.upload_rejected`, `account.notification.email.opted_in`, `account.notification.email.opted_out`. See [AUP-1 closure](../audits/aup1-account-profile-personal-preferences.md).
- **Display name ownership (AUP-1):** `User.DisplayName` is platform-owned. Apperture stores it and does **not** re-synchronize it from the identity provider after account creation, so an operator's edit is authoritative. Email and principal are read from request claims and presented read-only; `Users.EmailAddress` exists in the schema but nothing populates it.
- **`GET /api/auth/me` contract (AUP-1):** `deferred` is now nullable and returns `null` when no account capability is unavailable. The response additionally carries `avatar`, `appearance`, and `timeFormat` so the shell can paint correctly on first render without a second request.

## What remains (later)

- **Full audit emission** of authorization evaluations (AuthorizationEvaluationContext populated and emitted).
- **Module permission registration** runtime pipeline.
- **CLI operator identity** — Bootstrap CLI and future operator CLI caller identity.
- **Broader admin APIs** — General user/link/role management; "set local password" for existing users (deferred).
- **Self-service password reset, MFA, Entra group/tenant sync, SCIM** — Out of scope for Epic 2, and still not implemented. AUP-1 did not add them: where a security feature belongs to the identity provider, the Security page states that it is managed by the organization's directory rather than duplicating it. Administrator avatar management was likewise not added, because no such capability existed to extend.
- **Entra profile photo synchronization** — Deferred. `Microsoft.Graph` is already referenced, but only for directory discovery (`EntraDirectoryDiscoveryService` selects `id`, `displayName`, `userPrincipalName`, `mail`). Reading a signed-in user's photo needs delegated `User.Read` on their own token, while the OIDC configuration requests only `openid` and `profile`. That means new scopes, token retention for Graph calls, re-consent, and a staleness policy — out of proportion to the value. See AUP-1 §19.
- **Epic 4 completed:** Permission seed repair (startup), lockout guards, break-glass sign-in, recovery authorization and RestorePlatformAccess execution, audit hardening, narrow Entra external-id validation. See [Epic 4 summary](../02-decisions/Epic4-recovery-break-glass-implementation-summary.md).

## Establishing the first platform user

**Recommended:** Call `POST /api/Bootstrap/first-admin` once after deploy (no auth). Body: `{ "displayName": "…", "identitySource": "Entra" | "Local", "externalId": "…" }`. Use the same `externalId` that your auth provider will emit (e.g. Entra **oid**/sub, or local username). If at least one user already exists, the endpoint returns 409. This is the only in-product path for creating the first user/link/assignment until Epic 2 adds admin APIs.

**ExternalId and source:** For **Entra**, `externalId` must be the stable subject: Azure AD object ID (`oid` claim) or OIDC `sub`. Do not use `preferred_username` or email. For **Local**, use the **normalized username** (trim, lowercase)—the same value used for login. The auth handlers set the `identity_source` claim (`http://schemas.apperture/identity_source` = `Entra` or `Local`) so the resolver uses only that source.

## Plug-in points for platform auth

1. **Control Plane API authentication** — Management API uses IPrincipalContext and IAuthorizationService; once a real scheme sets `HttpContext.User`, authorization uses persisted roles.
2. **CLI operator identity** — Deferred; separate from bootstrap credential resolution and runtime trust.
3. **Environment / auth context for bootstrap** — When implementing real bootstrap persistence, the host may need an auth context for audit; do not pull bootstrap into the generic secret store.

## What not to do

- Do not use the operational `ISecretStore` or `ISecretResolver` for bootstrap SQL credentials.
- Do not store platform auth tokens or operator credentials in the same model as workflow/runtime secrets without a clear design.
- Do not conflate runtime trust (enrollment, runtime identity) with operator/UI auth.

## Related

- [Authorization and Governance Foundation](authorization-and-governance-foundation.md)
- [Bootstrap and Secrets Boundary](bootstrap-and-secrets-boundary.md)
- [Runtime Trust and Operational Authentication](runtime-trust-and-authentication.md)
