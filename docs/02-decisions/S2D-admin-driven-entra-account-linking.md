# S2D — Admin-driven Entra account linking

## Status

Implemented (stabilization epic S2D).

## v1 identity model

- One stable **platform user** per operator.
- **Local** and **Entra** sign-in methods are `IdentityLink` rows attached to that user.
- Roles, permissions, audit ownership, account data, and future subscriptions attach to the **platform user**, not to a separate Entra shadow identity.
- **Unlinked Entra** authentication succeeds at the auth layer but does not resolve to a platform user and grants **no** permissions.

## Admin-driven linking flow

1. Administrator opens **Config → Governance → Users → {user}**.
2. **Linked sign-in methods** lists current links (Entra labels masked as `Entra · …abcd`).
3. With `user.manage`, **Add Entra link** accepts the Entra **object id (oid)**.
4. Optional convenience: **Config → Authentication → Entra directory** → **Copy** object id, then paste on the user detail page.
5. API: existing `POST /api/users/{id}/identity-links` with `{ "identitySource": "Entra", "externalId": "<oid>" }`.

No user-initiated self-linking and no JIT provisioning in v1.

## API error codes

Structured `IdentityLinkErrorResponse` (`message`, `code`):

| Code | HTTP | Meaning |
|------|------|---------|
| `identity_link.missing_external_id` | 400 | Empty external id |
| `identity_link.invalid_source` | 400 | Source not Entra/Local |
| `identity_link.invalid_entra_external_id` | 400 | Missing oid or email/UPN supplied |
| `identity_link.user_not_found` | 404 | Target platform user missing |
| `identity_link.external_id_in_use` | 409 | Oid already linked to another user |
| (idempotent) | 204 | Same Entra link already on this user |

Caller without `user.manage` → **403**.

## Unlinked Entra behavior (S2C + S2D)

- `GET /api/auth/me` → **403** (audits `auth.unlinked_principal`).
- Shell banner + account menu **Account not linked** (not **Sign in**).
- `/account` explains contacting an administrator.
- RBAC-protected APIs deny access; capabilities return deny-all.

## Deferred

- User-initiated Entra self-linking (OIDC verification in browser)
- Automatic broad JIT provisioning
- Avatar / profile photo sync
- Notification subscription ownership UI

## Related

- [S2C minimum account page](S2C-minimum-account-page-and-password-change.md)
- Epic 3 auth-admin (`POST /api/users/{id}/identity-links`)
