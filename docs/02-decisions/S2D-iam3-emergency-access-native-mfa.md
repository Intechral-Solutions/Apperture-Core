# IAM-3 — Emergency access and native MFA

## Status

Accepted.

## Context

IAM-2 preserves the last Entra-independent Platform Administrator local credential so an installation cannot lose its recovery path. That safeguard is implicit: whichever administrator happens to remain locally enabled is treated as recovery. Operators cannot see a designated break-glass account, and that account is not protected by a second factor native to Apperture.

`POST /api/auth/break-glass` remains disabled. It would grant all RBAC for the session. IAM-3 does not enable it. Emergency administrators remain `role-platform-administrator` and sign in through local password plus native MFA.

## Decision

### Exactly one designated emergency account (v1)

Emergency status is explicit on the existing `User` aggregate via `Users.IsBreakGlassEligible`. There is no second user table. Display names, usernames, roles, and email are not used to infer emergency status.

A unique filtered index `UX_Users_EmergencyAccess` allows at most one row with `IsBreakGlassEligible = 1`.

A designated emergency account is **healthy** when all of the following hold:

- it is a Platform Administrator (`role-platform-administrator`);
- it has a Local identity link and an **enabled** local credential;
- native TOTP MFA is enrolled;
- at least one unused recovery code remains.

An Entra link on that user does not satisfy the recovery guarantee. Local password plus native MFA does.

### Native MFA

RFC 6238 TOTP (HMAC-SHA1, 6 digits, 30-second period, ±1 step, replay via `LastUsedStep`). Secrets are stored only after `ISecretProtector` (`apperture.native-mfa.totp`). Enrollment requires a valid TOTP before `IsEnrolled` is set.

After a successful local password, if MFA is required, the host issues a short-lived pending ticket cookie (`apperture.mfa.pending`) instead of a session. `POST /api/auth/local/mfa` completes sign-in with TOTP or a recovery code.

Emergency accounts require MFA after setup. Password-only emergency login fails closed (401, no user-existence leak). Ordinary users without enrollment remain password-only. The model can later require MFA for any enrolled local user.

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

### Strong authentication

Cookie mechanisms:

- `entra-authentication-context` (IAM-2)
- `native-totp`
- `native-recovery-code`

Recent native TOTP or recovery-code assurance satisfies `IStrongAuthenticationGate` even when Entra Authentication Context is not configured. Password alone never does.

### Operator tooling

CLI/PowerShell (`apperture emergency …`) uses the bootstrap SQL connection. It does not require Entra. Secrets are read from stdin, never argv. There is no graphical setup wizard (deferred).

## Persistence

- `UserTotpCredentials` — protected TOTP secret, enrollment, last used step
- `UserMfaRecoveryCodes` — hashed one-time codes
- `UX_Users_EmergencyAccess` on `Users(IsBreakGlassEligible) WHERE [IsBreakGlassEligible] = 1`

## Consequences

### Positive

- Recovery is an intentionally provisioned account, not an accident of who last kept a local password.
- Ordinary proven-Entra Platform Administrators follow Entra-primary policy once emergency access is healthy.
- Native MFA and recovery codes work without Microsoft Entra.

### Negative

- v1 allows only one active emergency account.
- MFA enrollment and recovery-code display are CLI-first; Account Security shows status only.

### Neutral

- Column name remains `IsBreakGlassEligible`. Operator copy uses **emergency administrator**.
- Grant-all `/api/auth/break-glass` stays disabled.

## Deferred

IAM-4: verified contact email, invitation, forgotten-password reset.  
IAM-5: Entra directory browsing, provisioning, B2B invitations.  
Graphical emergency setup wizard. End-user MFA management for ordinary local accounts.
