# Next Stage: Platform Auth Readiness

This document describes the **platform authentication** foundation (Epic 1) and what remains for operator auth, control-plane API auth, and later phases.

## Principles

- Platform auth builds **on top of** the current boundaries. It must not collapse or conflate:
  - **Bootstrap** (SQL credential and config; first-use)
  - **Runtime Trust** (enrollment, runtime identity; distinct from generic secrets)
  - **Operational Secret Management** (ISecretStore, ISecretResolver; runtime secrets)
- Bootstrap and runtime trust remain distinct from generic operational secret storage. Auth plugs in at control-plane and host boundaries.

## What is implemented (Epic 1 + corrective pass)

- **Persisted platform identity:** Tables `Users`, `Roles`, `IdentityLinks`, `UserRoles` in SQL Server (migrations `AddPlatformIdentityTables`, `AddPlatformIdentityReferentialIntegrity`). Foreign keys (Restrict), unique Role.Name, Role.RowVersion. Domain types unchanged (User, Role, IdentityLink, IdentitySource).
- **Real IPrincipalContext:** `HostPrincipalContext` resolves `HttpContext.User` (ClaimsPrincipal) to `IActor` via provider-agnostic claim mapping. For Id (ExternalId): **oid** first (Entra stable), then NameIdentifier, sub, name, preferred_username. Human actors only.
- **Real IPrincipalToUserResolver:** `SqlPrincipalToUserResolver` resolves a Human actor to `UserId` using `IdentityLinks` (Source + ExternalId). When `HumanActor.PreferredIdentitySource` is set (from `identity_source` claim), uses only that source; otherwise tries Entra then Local. After successful resolve, updates `LastAuthenticatedAt` on the matching link(s).
- **Identity-link ExternalId semantics:** **Entra:** use stable subject only—`oid` or `sub` (not `preferred_username` or email). **Local:** use local username or another stable local identifier. Documented in mapper and resolver.
- **Real IRoleProvider:** `SqlRoleProvider` backed by `IRoleRepository`; roles and user-role assignments are persisted. Seeded role `role-platform-administrator` (Platform Administrator) with a minimal permission set.
- **Host auth scaffolding:** Management.Host has `AddAuthentication` (placeholder scheme), `AddAuthorization`, `UseAuthentication`, `UseAuthorization`. Placeholder scheme never sets a user; replace with Entra or local auth in a later epic.
- **IIdentityResolutionService:** Implemented as a thin wrapper over `IPrincipalContext.GetCurrentActorAsync`.
- **First-admin bootstrap:** `POST /api/Bootstrap/first-admin` (no auth) creates the first platform user, one identity link, and assigns `role-platform-administrator` when zero users exist. Request: `{ "displayName", "identitySource": "Entra"|"Local", "externalId" }`. Returns 201 with userId, or 409/400/503 as specified.
- **Enrollment protection:** `POST /api/Enroll/invitation` requires an authenticated principal that resolves to a platform user with the platform administrator role; otherwise 401/403.

## What remains (Epic 2 and later)

- **Real auth schemes** — Entra interactive/token auth, local username/password; replace placeholder scheme.
- **Set `identity_source` claim** — Auth handlers should set claim `http://schemas.apperture/identity_source` to `Entra` or `Local` so resolution is unambiguous.
- **Full local username/password** — Credential storage, password hashing, login UI.
- **Break-glass execution** — Policy path reserved; not implemented in evaluator.
- **Full audit emission** of authorization evaluations (AuthorizationEvaluationContext populated and emitted).
- **Module permission registration** runtime pipeline.
- **CLI operator identity** — Bootstrap CLI and future operator CLI caller identity.
- **Broader admin APIs** — General user/link/role management (first-admin bootstrap is the only creation path in this pass).

## Establishing the first platform user

**Recommended:** Call `POST /api/Bootstrap/first-admin` once after deploy (no auth). Body: `{ "displayName": "…", "identitySource": "Entra" | "Local", "externalId": "…" }`. Use the same `externalId` that your auth provider will emit (e.g. Entra **oid**/sub, or local username). If at least one user already exists, the endpoint returns 409. This is the only in-product path for creating the first user/link/assignment until Epic 2 adds admin APIs.

**ExternalId and source:** For **Entra**, `externalId` must be the stable subject: Azure AD object ID (`oid` claim) or OIDC `sub`. Do not use `preferred_username` or email. For **Local**, use the local username or another stable local id. When the auth handler sets the `identity_source` claim (Epic 2), the resolver uses only that source; until then, the resolver tries Entra then Local.

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
