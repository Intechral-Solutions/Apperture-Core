# Verified contact and credential lifecycle live smoke test

Use only supported UI and API paths. Do not open SQL Server to insert tokens, hashes, or email rows. Do not put passwords or action tokens in tickets, chat, or screenshots.

This is an **Apperture account** lifecycle. It is not a Microsoft Entra B2B invitation test.

## Prerequisites

- IAM-1 through IAM-3 remain healthy: local sign-in, Microsoft sign-in/sign-out, Recovery Access local + MFA.
- Management UI (dev default `http://localhost:9001`) and Management.Host are running.
- Restart Management.Host (or the Aspire app) so migration `20260903180000_Iam4_VerifiedContactAndActionTokens` applies.
- SMTP: platform email host and from-address must be configured (`Notifications:Email` / platform settings). Security mail ignores the notification dry-run `Enabled` flag, but it still needs a reachable SMTP host.

If SMTP is unavailable in this development environment, complete every step that does not depend on delivery, then use the **SMTP gap** section. Do not weaken production token or mail design to work around the gap.

## 1. Existing user adds and verifies contact email

1. Sign in as an ordinary user (not Recovery Access).
2. Open Account → Profile.
3. Confirm **Microsoft identity email** / directory email (if present) is separate from **Contact email**.
4. Enter a contact address you control. Do not treat it as verified yet.
5. Status should be **Verification pending**.
6. Open the verification link from the email (or the captured link in the SMTP gap).
7. Status should become **Verified**.

## 2. Existing user changes contact email safely

1. From a verified contact email, enter a different address.
2. If local sign-in is enabled, confirm the current password. If local sign-in is disabled, the password field must not appear; Microsoft sign-in uses the current session instead.
3. The previous verified address must remain the active contact until the new address is verified.
4. Complete verification of the new address.
5. The previous address must no longer be the verified contact.

## 3–5. Admin invitation, password setup, local login

1. Sign in as a Platform Administrator.
2. Open Config → Governance → Users → New user.
3. Leave **Send account setup invitation** selected.
4. Enter display name, local username, contact email, and an optional role.
5. Do not enter a permanent password.
6. Create the user. Governance detail should show Contact email pending, Password setup pending, Invitation pending.
7. If the invitation expires, delivery fails, or the user still has no password, Governance → Users → that user must show **Resend setup invitation** (toolbar and user summary). Sending a new invitation invalidates the previous link.
8. Open the invitation link and choose a password.
9. Sign out and sign in locally with the new username and password.

## 6. Password change

1. While signed in locally, open Account → Security.
2. Change the password with the current password.
3. Sign out and sign in with the new password.
4. Local sign-in must remain enabled (this user was not Entra-primary-disabled).

## 7–9. Forgot-password reset, replay, anti-enumeration

1. From `/login`, open **Forgot password**.
2. Submit the verified contact email or local username.
3. The page must show the generic acknowledgement regardless of whether the account exists.
4. Open the reset link, choose a new password, and sign in.
5. Re-open the same reset link. It must fail with a generic expired/invalid message.
6. Submit an unknown username or email. The acknowledgement must be identical to a valid request. No UI or API difference may reveal whether the account exists, has a local credential, or is Entra-linked.

## 10. Entra-linked contact email stays independent

1. Sign in with Microsoft Entra.
2. Account → Profile must show the directory/Microsoft identity email as read-only.
3. Contact email stays `none` / pending / verified according to Apperture verification only.
4. Suggested directory mail may appear as a convenience. Saving it must still send verification. It must not become verified solely because Graph/OIDC supplied it.
5. Matching that address to another user must not create or link an `IdentityLink`.

## 11. Password reset does not re-enable an Entra-primary-disabled credential

1. Use an Entra-primary user whose local credential is **Disabled**.
2. If that user has a verified contact email, complete forgot-password reset.
3. Account → Security / Governance detail must still show local sign-in **Disabled**.
4. Enabling local sign-in remains the existing protected administrator action.

If the Entra-primary user has no local credential row, forgot-password must produce the generic acknowledgement and send no mail.

## 12. Recovery Access remains excluded and operational

1. Config → Governance → Users must not list Recovery Access.
2. `/login` local sign-in with `breakglass` plus authenticator (or recovery code) must still work.
3. Recovery Access Account → Profile / Security must not offer contact-email setup, invitation, or ordinary password change.
4. Forgot-password for `breakglass` or any recovery contact address must return the generic acknowledgement and send no reset mail.

## 13–14. Microsoft and Emergency Access remain healthy

1. Microsoft sign-in and sign-out still follow the IAM-3 sign-out rules (local session returns to `/login`; Entra session federates directory logout).
2. Emergency local sign-in + MFA remains healthy. Do not treat any IAM-4 failure as a reason to revoke Recovery Access.

## SMTP gap

If the development environment has no reachable SMTP host:

**Blocked:** actual inbox delivery for verification, invitation, and reset emails.

**Supported verification without weakening production:**

1. Keep tokens hashed in the database. Do not insert plaintext tokens.
2. Confirm the UI/API reports delivery failure for authenticated invitation/verification send (`account.security_mail.not_configured` or transport failure).
3. Confirm forgot-password still returns the generic acknowledgement when delivery cannot occur.
4. After configuring SMTP (or a local capture listener) on the existing platform email settings, resend the invitation or verification. Do not add a debug endpoint that returns bearer tokens.

Audit and logs must still contain no plaintext tokens, passwords, hashes, SMTP passwords, or full action URLs.
