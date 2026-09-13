# S2C — Minimum account page and self-service local password change

## Status

Implemented (stabilization epic S2C).

## Scope delivered

### API — enriched `GET /api/auth/me`

Additive fields on the existing endpoint (no separate `/account` route):

| Field | Purpose |
|--------|---------|
| `signInSource` | `Local`, `Entra`, or `Unknown` from the session identity-source claim |
| `identityLinks` | Linked sign-in methods with masked Entra labels (`Entra · …abcd`) |
| `localCredentialStatus` | `none`, `active`, or `disabled` |
| `roleNames` | Human-readable role names alongside role ids in `roles` |
| `canChangeLocalPassword` | `true` when local auth is enabled and credential is active |
| `deferred` | Flags/message for avatar, profile edit, and notification subscriptions (not in v1) |

Unauthenticated → **401**. Authenticated but unlinked → **403** (unchanged; audits `auth.unlinked_principal`).

### API — `POST /api/auth/local/change-password`

Self-service password change for the signed-in, linked user with an **active** local credential.

| Condition | Response |
|-----------|----------|
| Not authenticated | 401 |
| Unlinked principal | 403 |
| Local auth disabled | 400 (`auth.local_password.local_auth_disabled`) |
| No local link / credential row | 400 (`auth.local_password.no_credential`) |
| Disabled credential | 409 (`auth.local_password.credential_disabled`) |
| Wrong current password | 400 (`auth.local_password.wrong_current`) |
| New password too short | 400 (`auth.local_password.invalid_new`) |
| Success | 204 |

Password policy uses existing `LocalAuth:MinPasswordLength` (default 8). Hashing uses `ILocalPasswordHasher`.

Audit: `auth.local.password_change.success` / `auth.local.password_change.failed` (no secrets in details).

Admin password set/reset remains on governance APIs (`credential.manage`); not changed in S2C.

### UI — `/account`

Minimum account page: identity summary, linked methods, roles, local credential status, password change form when `canChangeLocalPassword`, otherwise an explanatory message, plus deferred-feature notice.

### UI — unlinked Entra session

- Header user menu shows **Account not linked** (links to `/account`) instead of **Sign in** when session state is `ForbiddenUnlinked`.
- `/account` shows administrator contact guidance for unlinked users.
- No permissions granted; linking not implemented (defer to S2D).

## Deferred (not in S2C)

- Avatar upload / default picker / Entra photo sync
- Profile editing beyond display summary
- Notification subscription management
- User- or admin-initiated Entra linking
- Visual overhaul / wizard framework

## Recommended next pass (S2D)

Entra account linking workflow and any shell/session refinements required for linked Entra operators who need platform access without local credentials.
