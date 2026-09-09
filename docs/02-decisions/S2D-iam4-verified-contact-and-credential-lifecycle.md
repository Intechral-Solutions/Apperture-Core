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
| Unlock Secured account | Recent account verification (below) |
| Add first contact email | Authenticated session. The Account UI requires recent account verification first. |
| Resend verification | Authenticated session. The Account UI requires recent account verification first. |
| Change verified email | Recent account verification, or enabled local credential → current password; else Entra → recent strong authentication, except `NotConfigured` (Entra session is enough) |
| Change password | Recent account verification, or current password |
| Consume setup / reset / verify token | Token only |
| Admin send/resend invitation | `user.manage` |
| Forgot-password request | Unauthenticated; always generic acknowledgement |
| Enable local sign-in (`credential.manage`) | Recent **strong** authentication. Password-only account verification is not enough. |

### Recent account verification

Ordinary Account mutations after unlock use a short-lived **recent account verification** window, not a client-only unlock flag.

Satisfied by any of:

- a freshly verified current local password (cookie claims `apperture.auth.account.verification.*`);
- existing recent strong authentication (Entra Authentication Context, native TOTP, recovery code).

Lifetime: **15 minutes**, the same `AuthenticationAssurance:RecentStrongAuthenticationLifetime` used for recent strong authentication (`AuthenticationAssuranceOptions.DefaultRecentLifetime`).

Password-only proof is **not** MFA and does **not** satisfy `IStrongAuthenticationGate` / `credential.manage`. Recovery Access does not use this unlock workflow.

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
- Forgot-password is eligible only for an ordinary user with a local identity, an **existing and enabled** `LocalCredential`, and a **verified** contact email.
- Recovery Access, Entra-only users, unverified emails, disabled local credentials, and unknown identifiers produce the same outward acknowledgement. No reset token is created and no mail is sent.
- Reset cannot re-enable a disabled local credential because those accounts are ineligible at the lifecycle boundary. Enablement stays on the existing `credential.manage` + recent strong-auth policy.

### Local password policy

All password-establishing paths share one application/core evaluator (`IPasswordPolicyEvaluator` / `PasswordPolicyEvaluator`) in `Apperture.Core.Domain`. The WASM client uses the same assembly for real-time feedback; the server and CLI independently enforce the same policy before accepting a password. There is no per-keystroke API.

Guessability and acceptance are separate:

| Profile | Minimum length | Maximum | Minimum score | Use |
|---|---|---|---|---|
| Ordinary | 15 | 128 | 3 (safely unguessable) | Invitation setup, reset, change password, first-admin, Governance initial password |
| Recovery Access | 20 | 128 | 3 | Emergency Access provision / set-password (CLI/operator tooling only) |

Rationale: NIST SP 800-63B-4 SHOULD of 15 characters for memorized secrets, with no composition rules. Ordinary local sign-in is password-only, so acceptance requires zxcvbn score 3+. Recovery Access already requires native MFA, but the password protects platform recovery, so length is stricter. Existing stored credentials are not weakened or rehashed by this change; the profiles apply to future create/change/reset.

Estimator: the `Zxcvbn` 0.1.1 NuGet package (MIT, net8.0, ~963 KB managed DLL, zxcvbn-ts 4.x port, published August 2026) sits behind `IPasswordPolicyEvaluator`. UI components do not reference the library. Older ports (`zxcvbn-core`, `zxcvbn-extra`, `zxcvbn.net`) were not used: they are unmaintained and do not track zxcvbn-ts. JavaScript/`zxcvbn-wasm` would duplicate scoring off the shared .NET path.

Additional acceptance rules (not composition):

- reject common/expected passwords;
- reject contextual terms (`apperture`, Recovery Access names, username, email local-part, display-name tokens, profile extra terms);
- context values stay inside evaluation; they are not rendered in operator-facing copy beyond a generic “account details” message.

Confirmation matching is a UI/CLI boundary check only.

The same evaluator is used by Emergency Access provision/set-password (CLI retries interactively), invitation setup, forgot-password completion, self-service change, first-admin bootstrap, Governance initial password, and local-credential set.

These remain distinct operations: change password, set/reset password, enable local authentication.

### Password UX (IAM-4 final pass)

Live values live in `PasswordSetupSession`. Each keystroke updates the field and confirmation match immediately. Zxcvbn scoring is coalesced and deferred (~150 ms after input is quiet) so typing never waits on the estimator. `OnParametersSet` does not rescore unless the password, profile, or context terms actually changed. Confirmation, visibility toggles, and unrelated fields do not call the estimator. Submit flushes any pending score before the client will treat the form as ready; the server remains authoritative. Progressive validation: pristine fields show no ✓/✕; touched fields and a submit attempt reveal field indicators and an inline form message. Submit stays enabled; invalid submit does not start `ActionButton` pending. Successful password mutations clear the session and refresh displayed account state without a reload.

### Recovery Access

Username `breakglass` / display name Recovery Access is excluded from contact email, invitation, forgot-password, ordinary password-change UI, and ordinary account/profile mutation. Its lifecycle stays on Emergency Access tooling (`EmergencyAccessIdentity.IsSystemManaged`).

### Entra coexistence

Entra-only users may have a verified contact email and no local credential. Entra-primary users whose local sign-in is disabled are not eligible for forgot-password. Entra mail/UPN never auto-verifies contact email.

## Consequences

- Operators can provision local users without knowing the permanent password.
- Contact email is a first-class verified destination, separate from Microsoft identity email and local username.
- Security mail is required infrastructure, not an optional notification.
- IAM-5 (directory browse, B2B invitations, SCIM, passkeys, ordinary-user MFA enrollment UX) remains out of scope.
