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

No JIT provisioning in v1.

## Superseded by IAM-1 (self-service linking)

Self-service linking is no longer deferred. IAM-1 keeps everything above and adds:

- A Microsoft identity is keyed by **`oid` + `tid`** only. The token `tid` must equal the active Entra registration's tenant; email, UPN, `preferred_username`, `amr`, `acr` and `auth_time` are never used for identity or authorization, and `sub` is kept only as diagnostic provenance.
- **`GET /signin/link`** on Management.Host starts a linking challenge from an already authenticated local session, carrying the initiating platform user id in `AuthenticationProperties`. **Connect Microsoft account** on `/account/security` drives it.
- An admin association created from the directory picker or a pasted object id stays **pending** (`LastAuthenticatedAt` null) until the operator's first successful Entra sign-in proves it.
- Each platform user may hold **at most one** Entra link, and an Entra link only counts as an alternate sign-in path once it is proven and Entra is configured, so a pending link can never strand the last Platform Administrator.
- Linking **never** disables local login at association time. IAM-2 disables local password sign-in after the Entra identity is **proven**, with a temporary Platform Administrator recovery safeguard until IAM-3.
- An Entra sign-in for an unknown object id now **fails the OIDC sign-in** and returns the operator to `/login?error=account_not_linked` instead of issuing a durable unlinked session. The `403` + `auth.unlinked_principal` behavior below remains as defense in depth.

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

- Automatic broad JIT provisioning
- Native Apperture MFA and the dedicated emergency Platform Administrator (IAM-3)
- Contact email, verification, and password setup/reset modernization (IAM-4)
- Avatar / profile photo sync
- Notification subscription ownership UI

## Related

- [S2C minimum account page](S2C-minimum-account-page-and-password-change.md)
- Epic 3 auth-admin (`POST /api/users/{id}/identity-links`)
