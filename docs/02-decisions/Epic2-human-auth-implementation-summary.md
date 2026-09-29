# Epic 2: Human Authentication Providers — Implementation Summary

## Status

Implemented (Epic 2). Real human authentication for the control plane: Microsoft Entra (OIDC + cookie) and local username/password, with configuration validation, host pipeline replacement, auth audit baseline, and minimal login surface.

## Entra (Microsoft Entra ID) approach

- **Scheme:** Cookie as DefaultAuthenticateScheme and DefaultSignInScheme; OpenIdConnect as DefaultChallengeScheme when Entra is enabled.
- **Configuration:** Section `Entra`: `Enabled`, `Authority`, `ClientId`, `ClientSecret`, `CallbackPath` (default `/signin-oidc`), `Scopes` (default openid, profile). Validated at startup via `EntraAuthOptionsValidator` (Authority, ClientId, ClientSecret required when Enabled).
- **Flow:** Unauthenticated request to protected endpoint → redirect to `GET /signin` → Challenge(OpenIdConnect) → redirect to Entra → callback `/signin-oidc` → `OnTokenValidated` adds claim `http://schemas.apperture/identity_source` = `Entra` → SignIn with Cookie. Subsequent requests carry the cookie; `ClaimsPrincipalToActorMapper` and `SqlPrincipalToUserResolver` use `identity_source` and resolve to platform user.
- **Sign-out:** `GET /signout` calls SignOut(Cookie). Optional OIDC sign-out for single sign-out not implemented in this epic.

  Current architecture (supersedes this bullet): see [ADR-0045](ADR-0045-browser-session-recovery-and-request-trust.md) and SEC-1. Sign-out is `POST /signout` (`GET /signout` is 405). Entra sessions federate Microsoft end-session and return through `/signout-callback-oidc`. `RemoteSignOutPath` is disabled; `/signout-oidc` is not live.

## Local username/password approach

- **Storage:** Table `LocalCredentials` (UserId FK, PasswordHash, Salt, Algorithm, IsDisabled, RowVersion). One credential per user. `ILocalCredentialRepository`, `SqlLocalCredentialRepository`. Password hashing via `ILocalPasswordHasher` / `LocalPasswordHasher` (PBKDF2-HMAC-SHA256, 100_000 iterations). Do not use ISecretStore for user passwords.
- **Login:** `POST /api/auth/local/login` with body `{ "username", "password" }`. Username is normalized (trim, lowercase) via `LocalAuthHelpers.NormalizeUsername`; lookup `IdentityLink(Local, normalizedUsername)` → User, then `LocalCredential` by UserId; verify password; if valid and not disabled, build ClaimsPrincipal (NameIdentifier = normalized username, Name = display name, `identity_source` = Local), SignIn with Cookie, return 200 with `{ userId, displayName }`. Invalid or disabled → 401; do not reveal whether username exists.
- **Configuration:** Section `LocalAuth`: `Enabled`, `MinPasswordLength`. When only Local is enabled, `GET /signin` returns 401 (client uses POST local/login explicitly).
- **First local admin:** First-admin bootstrap accepts optional `password` when `identitySource` is Local; if provided, after creating User+IdentityLink+UserRole, creates LocalCredential with hashed password. If not provided, first local admin has no password until a future "set password" path (deferred).

## Credential and identity model

- **Local ExternalId:** Normalized username (trim, lowercase) is the canonical `IdentityLink.ExternalId` for `IdentitySource.Local`. Same value is used when creating the link (first-admin or future admin APIs) and when logging in.
- **Unlinked authenticated user:** When `ResolveUserIdAsync` returns null after successful auth (e.g. Entra user not yet linked to a platform user), respond with **403 Forbid** and emit audit event `auth.unlinked_principal`. Applied in `AuthController.Me` and consistent with EnrollController pattern.

## Principal resolution pipeline

- **ClaimsPrincipal** from Entra (OnTokenValidated) or local login (AuthController) includes: sub/oid or NameIdentifier (for mapper Id), Name (display), and `identity_source` claim.
- **HostPrincipalContext** → **ClaimsPrincipalToActorMapper** → **HumanActor** (with PreferredIdentitySource when claim present) → **SqlPrincipalToUserResolver** (uses PreferredIdentitySource, updates LastAuthenticatedAt). No change to Core.Domain; all provider-specific logic in host and ControlPlane.Security.

## Auth audit

- **IAuditService** implemented as **SqlAuditService** (Persistence.SqlServer); appends to `AuditLogs` (Id, At, Actor, Action, Details). Query supported with basic filters.
- **Auth events:** `auth.entra.login.success` (OnTokenValidated), `auth.local.login.success`, `auth.local.login.failed` (actor "anonymous" for failures to avoid revealing existence of user), `auth.unlinked_principal` (when returning 403 for authenticated but unlinked principal).
- **Call sites:** Entra success in Host `AuthenticationServiceCollectionExtensions` OnTokenValidated; local success/failure and unlinked in `AuthController`.

## Endpoint protection

- **Authorize:** ConnectionsController, BindingsController, EnrollController, WorkflowsController, RunsController, ModulesController (class-level `[Authorize]`).
- **AllowAnonymous:** BootstrapController (first-admin), EnrollController POST (token-based enroll), AuthController local/login and local/logout. MenusController remains public.
- **GET /api/auth/me:** Requires `[Authorize]`; returns current user (userId, displayName, roles) or 401/403 if unauthenticated or unlinked.

## Configuration and startup

- **Options:** `EntraAuthOptions`, `LocalAuthOptions` bound from config sections `Entra` and `LocalAuth`. Validators: `EntraAuthOptionsValidator`, `LocalAuthOptionsValidator` (IValidateOptions). Registration and scheme setup in Management.Host `AddAppertureAuthentication(IConfiguration)`; Cookie always registered; OpenIdConnect added when Entra enabled.
- **Placeholder:** PlaceholderAuthHandler removed; real Cookie + OIDC (when enabled) replace it.

## Deferred / out of scope

- Self-service password reset, forgot-password UX.
- MFA, Entra group/tenant sync, SCIM.
- Break-glass execution, relationship authorization.
- Full admin UI; "set local password" for existing local users.
- JWT Bearer for API-only clients (cookie-only for this epic).
- Comprehensive [Authorize] policy sweep beyond default authenticated.

## Related

- [Next stage: Platform auth readiness](../01-architecture/next-stage-platform-auth.md)
- [Authorization and governance foundation](../01-architecture/authorization-and-governance-foundation.md)
