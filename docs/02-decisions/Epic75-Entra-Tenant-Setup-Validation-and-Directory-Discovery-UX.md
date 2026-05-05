# Epic 75 — Entra tenant setup, validation, and directory discovery UX

## Status

Accepted — implemented.

## Why separate from managed-contact / import (Epic 76)

Managed contacts and import/linking need a stable foundation: persisted tenant metadata, trustworthy OpenID validation, visible provider readiness, and Microsoft Graph browse without pretending browse equals import. This epic delivers **configuration, validation, discovery, and admin UX only** so Epic 76 can focus on contact aggregates and idempotent import keys (`ObjectId`) without entangling platform `Users` / `IdentityLinks`.

## Model (product vs persistence naming)

- **Product term:** Entra tenant configuration (admin UX and API DTOs).
- **Persistence:** Existing table `EntraInstanceRegistrations` and domain type `EntraInstanceRegistration` (Epic 44) extended with:
  - Optional `AzureTenantId` for issuer alignment and Graph tenant resolution.
  - `OpenIdConfigurationStatus`, last validation timestamps and summary/detail (OIDC discovery path).
  - `DirectoryDiscoveryStatus`, last discovery timestamps and summary/detail (Graph browse path).
- **Secrets:** Unchanged from Epic 44 — confidential client secret remains in deployment (`Entra:ClientSecret`) when `UsesDeploymentSecret` is true. No raw secret in SQL.

Migration: `Epic75_EntraValidationAndDirectoryDiscovery` adds the new columns with defaults `OpenIdConfigurationStatus = NotValidated`, `DirectoryDiscoveryStatus = NotAttempted`.

## OpenID validation

- Service: `IEntraOpenIdConnectConfigurationValidator` — GET `{authority}/.well-known/openid-configuration`, validates required claims (`issuer`, `authorization_endpoint`, `token_endpoint`, `jwks_uri`), optional issuer vs `AzureTenantId` GUID check, and deployment secret presence when applicable.
- API: `POST /api/config/auth/entra-tenants/{id}/validate-openid` (`auth.entra.manage`) persists outcome and returns `ValidationResultResponse` + updated tenant row.
- Structured result types: `EntraOpenIdConnectValidationResult` (application), `ValidationResultResponse` (API JSON named **ValidationResult** in epic vocabulary).

## Lockout-safe semantics

- **No change** to `AuthenticationConfigurationGuard` / `AuthSignInOptionsMerger` gates on PATCH/delete/activate.
- Validation and directory browse **do not** toggle Entra enablement or UI policy; they only record diagnostics on the registration row.
- Local sign-in and break-glass paths remain governed by existing deployment and policy (Epic 2/4).

## Directory discovery (Graph)

- Interface: `IEntraDirectoryDiscoveryService` with `SearchUsersAsync` / `SearchGroupsAsync`, returning `DirectoryDiscoveryResult<T>` with `DiscoveryFailureCategory` (configuration vs permissions vs transient vs throttled).
- DTOs: `EntraDirectoryUser`, `EntraDirectoryGroup` — **stable `ObjectId`** for future Epic 76 import keys.
- Implementation: `EntraDirectoryDiscoveryService` uses `ClientSecretCredential` + `GraphServiceClient`, scopes `https://graph.microsoft.com/.default`.
- Tenant resolution: `EntraDirectoryTenantResolver` prefers persisted `AzureTenantId`, else first path segment of `login.microsoftonline.com` authorities.

### Entra app registration (operators)

Grant **Application** permissions (admin consent): **User.Read.All**, **Group.Read.All**. Without these, browse returns a permissions-class failure with an actionable message.

## Auth provider readiness (API)

- `GET /api/config/auth-overview` includes `authProviderAvailability`: Entra readiness, local availability, active registration OID/discovery status strings, optional `adminDiagnosis` when the active registration last failed validation or discovery.

## Admin UX

- `/config/auth` — overview links + provider readiness card.
- `/config/auth/tenants` — Entra tenant setup: create/edit, validate OIDC, links to directory browse.
- `/config/auth/entra/{registrationId}/users` and `/config/auth/entra/{registrationId}/groups` — browse/search; copy states results are **not** imported contacts.

## Operational checklist

1. Configure deployment `Entra:ClientSecret` and (if used) active registration authority/client id.
2. Optionally set **Azure tenant id** on the registration when authority uses `common`/`organizations` or you want explicit Graph tenant targeting.
3. Run **Validate OIDC** from Entra tenant setup; fix authority/secret until status is **Valid**.
4. Open **Users** / **Groups** browse; fix Graph permissions if failures are permission-class.
5. Restart management host after changing **active** registration authority/client id (Epic 44 behavior).

## Manual verification (recommended)

- End-to-end OIDC sign-in after activate + host restart against a real tenant.
- Graph browse after consenting application permissions.

## Out of scope (explicit)

- Managed-contact import, promotion, or linking to platform users.
- SCIM or enterprise provisioning.
- Storing Entra client secret in SQL / `ISecretStore` (future optional ADR).
- OIDC handler hot reload without process restart.

## Epic 76 seam

Import should call `IEntraDirectoryDiscoveryService` (or read persisted validation state) and persist selected **`ObjectId`** keys against the managed-contact model — **not** directly into `UserEntity` until product rules say so.
