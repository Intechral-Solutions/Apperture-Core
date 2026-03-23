# Epic 44 — Config-hosted authentication administration and Entra registrations

## Status

Accepted — implemented.

## Context

Operators need a single place in Config to understand effective sign-in behavior, edit chooser policy safely, and manage persisted Entra (Azure AD) tenant metadata. The management API and `/login` must agree on “Entra available” and avoid lockouts when policy hides local sign-in from the primary page.

## Decision

1. **Shared snapshot** — `AuthSignInSnapshotBuilder` builds `AuthSignInOptionsSnapshot` plus `EffectiveAuthExperienceKind` for both `GET /api/auth/sign-in-options` and the authenticated overview.
2. **Entra registrations** — New table `EntraInstanceRegistrations` with at most one `IsActive` row (filtered unique index). Non-secret fields only; **v1 uses deployment `Entra:ClientSecret` (or KeyVault)** when `UsesDeploymentSecret` is true (the only supported mode in API/UI).
3. **Effective Entra options** — `IPostConfigureOptions<EntraAuthOptions>` overlays the active registration onto bound configuration. **Precedence:** active enabled registration wins for authority, client ID, and optional callback path; `Enabled` is forced true when an active registration applies. If there is no active row, legacy `Entra` configuration is used unchanged.
4. **Host OIDC** — Management host configures OpenID Connect from `IOptionsMonitor<EntraAuthOptions>` so the same effective options drive OIDC middleware. **Restart** (or process recycle) is required for authority/client changes to fully apply to the OIDC handler; there is no hot reload in v1.
5. **Permissions** — `auth.configuration.read` (overview + list/read tenants), `auth.entra.manage` (CRUD + activate), existing `auth.ui_policy.manage` for chooser PATCH. Platform administrator role seeded with the new permissions via data migration. `GET config/auth-ui-policy` allows read permission **or** policy-manage permission.
6. **Lockouts** — `AuthenticationConfigurationGuard` centralizes checks: policy updates still use `AuthSignInOptionsMerger.ValidatePolicyUpdate`; Entra delete uses “fallback to deployment Entra only” when removing the active row; PATCH simulates effective Entra after update. Disabling an active registration clears `IsActive` on save.

## Consequences

- Single OIDC scheme remains; multiple rows support DR / switching active registration, not concurrent IdPs.
- Operators must keep `Entra:Enabled` and deployment secret aligned with registrations; UI surfaces “deployment secret configured” as yes/no only.
- Future work: optional OIDC options reload (`IOptionsMonitor` + handler reconfiguration), optional encrypted DB secret path.

## Routes (UI)

- `/config/auth` — overview  
- `/config/auth/sign-in` — chooser policy  
- `/config/auth/tenants` — Entra registrations  
- `/config/security/sign-in` — redirect to `/config/auth/sign-in`

## API

- `GET /api/config/auth-overview`  
- `GET|POST|GET/{id}|PATCH|DELETE /api/config/auth/entra-tenants`, `POST .../{id}/activate`

## Implementation summary

- Migrations: `AddEntraInstanceRegistrations`, `AddEpic44AuthConfigurationPermissionsToPlatformAdmin`.
- Anonymous `GET /api/auth/sign-in-options` returns `effectiveExperience` + description via `AuthSignInSnapshotBuilder`.
- Management host OIDC options are driven from `IOptionsMonitor<EntraAuthOptions>` (`EntraOpenIdConnectOptionsConfigurer` + `EntraAuthenticationOptionsConfigurer`).
- **Corrective pass (optional):** OIDC hot reload without restart; richer Entra row edit UI (PATCH with row version); API integration tests for Entra controllers when the test host can build without file locks.
