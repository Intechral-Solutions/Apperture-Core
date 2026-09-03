# IAM-3 — Emergency access and native MFA

## Status

Accepted.

## Context

IAM-2 preserves the last Entra-independent Platform Administrator local credential so an installation cannot lose its recovery path. That safeguard is implicit: whichever administrator happens to remain locally enabled is treated as recovery. Operators cannot see a designated recovery account, and that account is not protected by a second factor native to Apperture.

`POST /api/auth/break-glass` remains disabled. It would grant all RBAC for the session. IAM-3 does not enable it. Emergency Access remains `role-platform-administrator` and signs in through local password plus native MFA.

## Decision

### Capability vs principal

- **Emergency Access** is the recovery capability (CLI, API, posture, audit).
- **Recovery Access** is the system-managed principal that provides that capability.

Canonical identity (not administrator-configurable):

| Field | Value |
|---|---|
| Username | `breakglass` (normalized trim + lowercase) |
| Display name | Recovery Access |
| Role | Platform Administrator (`role-platform-administrator`) |

Constants live in `EmergencyAccessIdentity`. Do not scatter these literals. The database `UserId` is not hard-coded.

### Exactly one designated emergency account (v1)

Emergency status is explicit on the existing `User` aggregate via `Users.IsBreakGlassEligible`. There is no second user table. Display names and usernames are not used to infer emergency status.

A unique filtered index `UX_Users_EmergencyAccess` allows at most one row with `IsBreakGlassEligible = 1`.

A designated emergency account is **healthy** when all of the following hold:

- it is a Platform Administrator (`role-platform-administrator`);
- it has a Local identity link and an **enabled** local credential;
- native TOTP MFA is enrolled;
- at least one unused recovery code remains.

An Entra link on that user does not satisfy the recovery guarantee. Recovery qualification depends only on local password + native TOTP + unused recovery code(s) + Platform Administrator role. Supported flows must not create or associate an Entra identity for Recovery Access. An existing Entra link in another installation is reported as an invalid operator condition; it is not deleted automatically.

### System-managed identity

Once designated, Recovery Access is not an ordinary Governance user:

- Username `breakglass` is reserved for Emergency Access whether or not the account has already been provisioned. Ordinary create, rename, import, first-admin, and bootstrap-principal paths reject it.
- Username and display name are not configurable through `apperture emergency provision`.
- The account is omitted from Config → Governance → Users list/search and from `GET /api/users/{id}` used by that UI. That is an operational boundary, not a secrecy guarantee. Assume attackers know the username. Authentication still resolves the user through `IUserRepository`. Audit history is not filtered.
- Ordinary user/account workflows must not mutate it (username, display name, avatar, disable/delete, local credential disable, role change, Entra linking, Account Security self-link, ordinary password change). Password/MFA/recovery-code management uses Emergency Access operator tooling.
- It must not be used as a daily administrator account and must not participate in IAM-4 ordinary account-recovery or onboarding flows.

### In-place canonicalization (no EF data migration)

Installations that already designated an emergency `User` before this refinement keep that same row. `EnsureCanonicalIdentityAsync` (Management.Host startup hosted service, CLI `status`/`provision`) updates display name and Local `IdentityLinks.ExternalId` in one `SaveChanges`. Credentials, TOTP (`LastUsedStep`), unused recovery codes, `UserId`, `IsBreakGlassEligible`, and audit history are not rewritten.

Application-level upgrade is used instead of an EF migration so a collision can fail closed: if another user already owns normalized `breakglass`, throw `emergency.access.username_conflict`, leave the designated recovery path unchanged, and do not take the host down. Operators must resolve the conflict explicitly. Do not delete or rename the other user automatically.

### Native MFA

RFC 6238 TOTP (HMAC-SHA1, 6 digits, 30-second period, ±1 step, replay via `LastUsedStep`). Secrets are stored only after `ISecretProtector` (`apperture.native-mfa.totp`). Enrollment requires a valid TOTP before `IsEnrolled` is set.

After a successful local password, if MFA is required, the host issues a short-lived pending ticket cookie (`apperture.mfa.pending`) instead of a session. `POST /api/auth/local/mfa` completes sign-in with TOTP or a recovery code.

Emergency accounts require MFA after setup. Password-only emergency login fails closed (401, no user-existence leak). Unauthenticated login must not disclose whether `breakglass` exists, whether it is the emergency account, whether MFA is enrolled, or emergency posture. Ordinary users without enrollment remain password-only. The model can later require MFA for any enrolled local user.

### Recovery codes

Ten high-entropy codes (`XXXX-XXXX-XXXX`) are shown only at generation or regeneration. Persist PBKDF2 hash+salt via `ILocalPasswordHasher`. Consuming a code invalidates it. Regeneration invalidates unused codes. Recovery codes are not password reset (IAM-4).

### Policy transition

`EntraPrimaryAuthenticationPolicy.ApplyAfterEntraProvenAsync`:

1. Already disabled → idempotent, no audit.
2. Designated emergency user → never auto-disable (`ProtectedEmergencyAccount`).
3. If emergency posture is **healthy** → ordinary Entra-primary disable, even for the last ordinary Platform Administrator.
4. Else if this is the last Entra-independent ordinary Platform Administrator → IAM-2 defer.
5. Else → disable.

The designated emergency user is excluded from the “other independent administrators” count so an incomplete emergency account cannot lift the fallback and lock out the installation.

Explicit `emergency revoke --confirm-fallback` returns the installation to IAM-2 last-administrator protection. Do not introduce a transition in which both recovery mechanisms are unavailable.

### Strong authentication

Cookie mechanisms:

- `entra-authentication-context` (IAM-2)
- `native-totp`
- `native-recovery-code`

Recent native TOTP or recovery-code assurance satisfies `IStrongAuthenticationGate` even when Entra Authentication Context is not configured. Password alone never does.

### Operator tooling

CLI/PowerShell (`apperture emergency …`) uses the bootstrap SQL connection. It does not require Entra. Secrets are read from stdin, never argv. `emergency provision` creates or recognizes the canonical Recovery Access account (`breakglass` / Recovery Access); it does not accept `--username` or `--display-name`. Re-running provision against an existing designated account is idempotent and does not change the password. There is no graphical setup wizard (deferred).

Authorized operators see posture on Config → Authentication → Overview (Emergency Access). Secrets, hashes, TOTP material, and recovery-code values are never returned.

## Persistence

- `UserTotpCredentials` — protected TOTP secret, enrollment, last used step
- `UserMfaRecoveryCodes` — hashed one-time codes
- `UX_Users_EmergencyAccess` on `Users(IsBreakGlassEligible) WHERE [IsBreakGlassEligible] = 1`

## Consequences

### Positive

- Recovery is an intentionally provisioned, system-managed account, not an accident of who last kept a local password.
- Ordinary proven-Entra Platform Administrators follow Entra-primary policy once Emergency Access is healthy.
- Native MFA and recovery codes work without Microsoft Entra.

### Negative

- v1 allows only one active emergency account.
- MFA enrollment and recovery-code display are CLI-first; Account Security shows status only.
- Omitting Recovery Access from Governance Users is not a security control; the username is assumed guessable.

### Neutral

- Column name remains `IsBreakGlassEligible`. Operator copy uses **Emergency Access** (capability) and **Recovery Access** (principal).
- Grant-all `/api/auth/break-glass` stays disabled.

## Deferred

IAM-4: verified contact email, invitation, forgotten-password reset. Recovery Access must not join those flows.  
IAM-5: Entra directory browsing, provisioning, B2B invitations.  
Graphical emergency setup wizard. End-user MFA management for ordinary local accounts.
