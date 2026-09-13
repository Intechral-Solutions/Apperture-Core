# Verified contact and credential lifecycle live smoke test

Use only supported UI and API paths. Do not open SQL Server to insert tokens, hashes, or email rows. Do not put passwords or action tokens in tickets, chat, or screenshots.

This is an **Apperture account** lifecycle. It is not a Microsoft Entra B2B invitation test.

## Prerequisites

- IAM-1 through IAM-3 remain healthy: local sign-in, Microsoft sign-in/sign-out, Recovery Access local + MFA.
- Management UI (dev default `http://localhost:9001`) and Management.Host are running.
- Restart Management.Host (or the Aspire app) so migration `20260903180000_Iam4_VerifiedContactAndActionTokens` applies.
- SMTP: platform email host and from-address must be configured (`Notifications:Email` / platform settings). Security mail ignores the notification dry-run `Enabled` flag, but it still needs a reachable SMTP host.
- First contact email and resend now require recent account verification on the **API** as well as the UI. Token verification is unchanged. Recovery Access remains excluded.

If SMTP is unavailable in this development environment, complete every step that does not depend on delivery, then use the **SMTP gap** section. Do not weaken production token or mail design to work around the gap.

## 1. Account naming and locked Secured account

1. Sign in as an ordinary user with an enabled local credential (not Recovery Access).
2. Open the user menu. The former **Security** item is labeled **Account** and opens `/account`.
3. `/account/security` still opens the same Account page.
4. Breadcrumb on `/account` is a single **Account** crumb (not Account → Account).
5. Profile no longer hosts Contact email. Identity still shows Microsoft identity email / directory email separately, with a link to Account.
6. Account shows **Secured account** locked. Underlying contact and password controls must not be usable. Sensitive values must not be readable through the overlay.
7. Enabled-local users see **Current password** plus the arrow submit. Enter submits. `autocomplete="current-password"` is present.
8. A wrong password leaves the section locked, shows a non-enumerating error, and clears the field.
9. The correct password unlocks the section and clears the field. Password is not retained in the form, URL, or logs.
10. After unlock, **Contact email** appears with Verified / Verification pending / No contact email configured.
11. After unlock, Change password has no current-password field. New-password UX (empty start, strength, confirm, eyes, ActionButton) is unchanged.
12. Profile photo picker shows exactly three options labeled Option A, Option B, and Option C. Selection does not store gender. Custom upload still works.

## 2. Contact email add, verify, and change after unlock

1. From the unlocked Secured account section, enter a contact address you control. Do not treat it as verified yet.
2. Status should be **Verification pending**. Save / resend use ActionButton pending and success states without a page reload.
3. Open the verification link from the email (or the captured link in the SMTP gap).
4. Status should become **Verified**.
5. Enter a different address. Do not re-enter the current password if the section is still unlocked.
6. The previous verified address must remain the active contact until the new address is verified.
7. Complete verification of the new address.
8. The previous address must no longer be the verified contact.

## 3–5. Admin invitation, password setup, local login

1. Sign in as a Platform Administrator.
2. Open Config → Governance → Users → New user.
3. Leave **Send account setup invitation** selected.
4. Enter display name, local username, contact email, and an optional role.
5. Do not enter a permanent password.
6. Create the user. Governance detail should show Contact email pending, Password setup pending, Invitation pending.
7. If the invitation expires, delivery fails, or the user still has no password, Governance → Users → that user must show **Resend setup invitation** (toolbar and user summary). Sending a new invitation invalidates the previous link.
8. Open the invitation link. Confirm the new-password field is empty, the eye control works, strength updates while typing, and matching confirmation is detected. Choose an accepted passphrase (at least 15 characters, not built from the username or `apperture`) and matching confirmation.
9. Sign out and sign in locally with the new username and password.

## 6. Password change after unlock

1. While signed in locally, open Account (`/account`) and unlock Secured account.
2. New password fields must start empty (no value supplied by Apperture). Browser password-manager autofill is allowed.
3. Type a new password. Characters must appear immediately at normal typing speed, including past 15–30 characters. **Password strength may trail by a fraction of a second**; it must not block the input. No network call per keystroke. Confirmation must stay responsive after the new password is long.
4. Strength colors/labels follow 0–4: 0 Weak/red, 1 Weak/orange, 2 Moderate/yellow, 3 Strong/chartreuse, 4 Strong/green. An untouched empty field stays visually neutral (not Weak).
5. The eye control inside the field toggles visibility. Hidden = translucent eye, visible = opaque. Keyboard focus and “Show password” / “Hide password” labels must work.
6. Matching confirmation is detected immediately. A mismatch is detected immediately after the confirmation field is edited.
7. Pristine fields show no check or X. After edit, valid fields show a check; invalid fields show an X whose tooltip/focus text explains the problem.
8. Submit stays enabled while the form is incomplete. Pressing it must reveal remaining invalid fields, show the inline form message (“Some information needs your attention…”), and **must not** start the mutation or ActionButton pending state.
9. Strength can read Strong while policy still fails (for example too short, or contains the username). Those are different signals.
10. A valid change must **not** ask for the current password again while the verification window is valid. ActionButton shows pending, then success. Fields, strength, and validation reset without a page reload. Local sign-in status stays current.
11. Sign out and sign in with the new password.
12. Local sign-in must remain enabled (this user was not Entra-primary-disabled).
13. Wait until the 15-minute verification window expires (or call the API after expiry). Secured account must lock again. `POST /api/auth/local/change-password` without a current password and without a fresh verification cookie must fail. `POST /api/account/secured/verify` with the correct password unlocks again.

## 7–9. Forgot-password reset, replay, anti-enumeration

1. From `/login`, open **Forgot password**.
2. Submit the verified contact email or local username.
3. The page must show the generic acknowledgement regardless of whether the account exists.
4. Open the reset link, choose a new password, and sign in.
5. Re-open the same reset link. It must fail with a generic expired/invalid message.
6. Submit an unknown username or email. The acknowledgement must be identical to a valid request. No UI or API difference may reveal whether the account exists, has a local credential, or is Entra-linked.

## 10. Entra-primary unlock and independent contact email

1. Sign in with Microsoft Entra as a user whose local credential is disabled or absent.
2. Account → Profile must show the directory/Microsoft identity email as read-only.
3. Account → Secured account starts locked. The overlay shows **Verify with Microsoft**, not a local password field as the only path.
4. Complete Microsoft verification (step-up / recent auth). The section unlocks. Do not create or re-enable a local password merely to unlock.
5. Contact email stays `none` / pending / verified according to Apperture verification only.
6. Suggested directory mail may appear as a convenience. Saving it must still send verification. It must not become verified solely because Graph/OIDC supplied it.
7. Matching that address to another user must not create or link an `IdentityLink`.
8. Connect Microsoft account (when unlinked) stays outside the lock, centered, with the Microsoft-blue action.

## 11. Disabled local credentials are not eligible for forgot-password

1. Use an Entra-primary user whose local credential is **Disabled** and who has a verified contact email.
2. Submit forgot-password with that username or contact email.
3. The page/API must show the same generic acknowledgement as an unknown identifier.
4. No reset email is sent and no usable reset token is created. Do not treat “mail suppressed after token issue” as success.
5. Account / Governance detail must still show local sign-in **Disabled**.
6. Enabling local sign-in remains the existing protected administrator action.

If the Entra-primary user has no local credential row, the same generic acknowledgement and no-mail rule apply.

## 12. Recovery Access remains excluded and operational

1. Config → Governance → Users must not list Recovery Access.
2. `/login` local sign-in with `breakglass` plus authenticator (or recovery code) must still work.
3. Recovery Access Account → Profile / Account must not offer contact-email setup, invitation, ordinary password change, or the ordinary Secured account unlock.
4. Forgot-password for `breakglass` or any recovery contact address must return the generic acknowledgement and send no reset mail.

## 13–14. Microsoft and Emergency Access remain healthy

1. Microsoft sign-in and sign-out still follow the IAM-3 sign-out rules (local session returns to `/login`; Entra session federates directory logout).
2. Emergency local sign-in + MFA remains healthy. Do not treat any IAM-4 failure as a reason to revoke Recovery Access.

## 15. Password UX checklist (IAM-4 final pass)

Confirm all of the following on invitation setup, forgot-password reset, Account change-password (after unlock), and Governance “set an initial password now”:

1. Password characters appear immediately; strength updates shortly after typing pauses (no multi-second lag).
2. Strength colors/labels follow the 0–4 mapping above.
3. No password begins prepopulated by Apperture.
4. Password visibility eye works.
5. Matching passwords are detected correctly.
6. Mismatched passwords are detected correctly.
7. Pristine fields remain visually neutral.
8. Invalid touched fields show X + useful validation.
9. Valid fields show a checkmark.
10. Invalid submit reveals remaining errors and does not call the mutation.
11. Form-level feedback is visible inline.
12. Valid password submit uses ActionButton pending/success feedback.
13. Password-change UI refreshes immediately afterward (no manual reload).
14. Existing disabled-local forgot-password eligibility remains correct (section 11).
15. Recovery Access and Microsoft authentication regressions remain green (sections 12–14). Recovery Access must not gain the ordinary browser password UI.

## SMTP gap

If the development environment has no reachable SMTP host:

**Blocked:** actual inbox delivery for verification, invitation, and reset emails.

**Supported verification without weakening production:**

1. Keep tokens hashed in the database. Do not insert plaintext tokens.
2. Confirm the UI/API reports delivery failure for authenticated invitation/verification send (`account.security_mail.not_configured` or transport failure).
3. Confirm forgot-password still returns the generic acknowledgement when delivery cannot occur.
4. After configuring SMTP (or a local capture listener) on the existing platform email settings, resend the invitation or verification. Do not add a debug endpoint that returns bearer tokens.

Audit and logs must still contain no plaintext tokens, passwords, hashes, SMTP passwords, or full action URLs.
