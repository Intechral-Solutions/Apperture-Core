# IAM-4 — Verified contact identity and credential lifecycle

## Status

Accepted.

## Context

IAM-1 through IAM-3 established the authoritative `User`, `IdentityLink`, local credentials, Entra `oid + tid`, recent/strong authentication, and Recovery Access. Operators still lacked a verified contact destination and a local-credential lifecycle that does not require an administrator to know the user's permanent password.

Contact email must not become a second account model or an identity key.

## Decision

### Contact email is not login identity

`Users.EmailAddress` is the user's verified Apperture contact / notification / recovery destination.

It is not:

- an Entra identity key;
- a replacement for `oid + tid`;
- an implicit `IdentityLink`;
- a local username unless the local identity independently uses the same string;
- a lookup key that automatically links identities.

Accounts are never associated because email addresses match. An Entra `mail` or UPN snapshot may be offered as a suggestion and remains unverified until the Apperture verification flow completes.

### Verification state

```text
EmailAddress
EmailVerifiedAtUtc
PendingEmailAddress
```

Status is explicit:

| State | Meaning |
|---|---|
| `none` | No contact email configured |
| `pending` | A destination is awaiting verification |
| `verified` | `EmailAddress` + `EmailVerifiedAtUtc` are set |

A unique filtered index `UX_Users_VerifiedContactEmail` applies only when the address is verified. Addresses are stored normalized lowercase.

Changing a verified address keeps the current verified destination active and writes the candidate to `PendingEmailAddress` until the new address is verified.

### Account-action tokens

`AccountActionTokens` store hashed opaque bearers for:

- `VerifyContactEmail` (24 hours)
- `PasswordSetup` (72 hours)
- `PasswordReset` (1 hour)

Rules:

- cryptographically strong random plaintext, returned only at issuance;
- persist SHA-256 hex of the bearer, never the bearer;
- explicit purpose, target user, destination email, expiry, issued/consumed/revoked timestamps;
- newer issuance of the same purpose for the same user revokes older active tokens;
- consumption is one-time; replay and wrong-purpose use fail with a generic message;
- tokens, passwords, hashes, SMTP credentials, and full action URLs are never audited.

### Transactional security mail

`IAccountSecurityMailSender` reuses platform SMTP (`EmailNotificationOptions` + `ISecretStore` via `IEmailSmtpCredentialResolver`) but does not use notification subscriptions, event rules, opt-out, or the notification dry-run `Enabled` gate. If host and from-address exist, security mail is sent.

Delivery failure does not claim success. Unauthenticated forgot-password still returns a generic acknowledgement. Administrators see invitation delivery failure on resend. Retry issues a new token and revokes the prior one. Plaintext tokens are not persisted for retry.

### Assurance matrix

| Action | Proof |
|---|---|
| Add first contact email | Authenticated session |
| Resend verification | Authenticated session |
| Change verified email | Enabled local credential → current password; else Entra → recent strong authentication, except `NotConfigured` (Entra session is enough) |
| Change password | Current password (existing self-service) |
| Consume setup / reset / verify token | Token only |
| Admin send/resend invitation | `user.manage` |
| Forgot-password request | Unauthenticated; always generic acknowledgement |

Do not invent a second-factor requirement for local users who have none.

### Local password setup and invitation

Preferred Governance create-user path:

```text
Admin creates local user + contact email
        ↓
Apperture account setup invitation (not Entra B2B)
        ↓
User opens one-time link and chooses a password
        ↓
Local credential is created enabled; contact email is verified if free
```

Legacy administrator-supplied initial password remains an intentional alternate. Completing setup refuses if a credential row already exists (use change or reset). Resend invalidates the prior setup token.

### Password change and reset

- Change password requires the current password, uses `ILocalPasswordHasher`, does not flip `IsDisabled`, and revokes outstanding setup/reset tokens.
- Forgot-password is eligible only for an ordinary user with a local identity, an existing local credential row, and a **verified** contact email.
- Recovery Access, Entra-only users, unverified emails, and unknown identifiers produce the same outward acknowledgement.
- Reset **must not** enable a disabled local credential. Enablement stays on the existing `credential.manage` + recent strong-auth policy.

These remain distinct operations: change password, set/reset password, enable local authentication.

### Recovery Access

Username `breakglass` / display name Recovery Access is excluded from contact email, invitation, forgot-password, ordinary password-change UI, and ordinary account/profile mutation. Its lifecycle stays on Emergency Access tooling (`EmergencyAccessIdentity.IsSystemManaged`).

### Entra coexistence

Entra-only users may have a verified contact email and no local credential. Entra-primary users with a disabled local credential can reset that password without restoring local login. Entra mail/UPN never auto-verifies contact email.

## Consequences

- Operators can provision local users without knowing the permanent password.
- Contact email is a first-class verified destination, separate from Microsoft identity email and local username.
- Security mail is required infrastructure, not an optional notification.
- IAM-5 (directory browse, B2B invitations, SCIM, passkeys, ordinary-user MFA enrollment UX) remains out of scope.
