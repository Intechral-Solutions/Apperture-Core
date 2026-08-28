# IAM-2 — Strong authentication and Entra-primary policy

## Status

Implemented.

## Decision

Once an Entra identity is **proven**, Microsoft Entra is the user's primary authentication method. The local password hash is retained; `LocalCredentials.IsDisabled` is set to `true` for ordinary users.

Recent strong authentication is a **server-derived** cookie claim set (`apperture.auth.assurance.*`) protected by ASP.NET Data Protection. A sliding 12-hour application cookie is not evidence of recency. The default window is 15 minutes (`AuthenticationAssurance:RecentStrongAuthenticationLifetime`).

Apperture requests a Microsoft Entra Conditional Access **authentication-context identifier** (`c1`–`c25`) on an explicit `/signin/step-up` OIDC round trip and verifies the returned `acrs` claim. That means Microsoft reported the configured context was satisfied. It does **not** mean Apperture verified a particular MFA technology.

Re-enabling another user's local credential requires `credential.manage` **and** recent strong authentication. Ordinary users cannot re-enable themselves.

Until IAM-3 provides an Entra-independent emergency Platform Administrator, IAM-2 will not auto-disable the last Platform Administrator local credential that can still sign in without Entra.

## Persistence choice

Assurance timestamps live in the signed authentication cookie, not in saved access tokens and not in a parallel session store. The UI cannot set these claims.

## Development simulation

`Entra:DevelopmentSimulateAuthenticationContext` is honored only when the host environment is Development. Production ignores a simulated cookie claim.
