# Entra directory provisioning and access requests live smoke test

Use only supported UI paths. Do not open SQL Server to insert users, identity links, or access requests. Do not put client secrets, Graph tokens, or limited-session cookies in tickets, chat, or screenshots.

This is **not** a Microsoft Entra B2B invitation test. It does not create guests in Entra and does not send IAM-4 password setup invitations.

## Prerequisites

- IAM-1 through IAM-4 remain healthy: local sign-in, Microsoft sign-in/sign-out, Recovery Access local + MFA, Account / Secured account.
- Management UI (dev default `http://localhost:9001`) and Management.Host are running.
- Restart Management.Host (or the Aspire app) so migrations through `20260909180000_Iam5_EntraTenantScopedIdentityUniqueness` apply. The earlier IAM-5 table/policy migration remains required. Entra identity uniqueness is now tenant + object id; the same object id in another tenant is a different person.
- An enabled Entra registration exists with a working confidential client secret and existing `User.Read.All` application permission (admin consent). IAM-5 does not add Graph permissions.
- SMTP: optional welcome/access-granted mail uses the same transactional mail transport as account-security mail. If SMTP is unavailable, complete every step that does not depend on delivery and note the gap. Do not treat Graph `mail` as verified Apperture contact email.

If you do not have two unprovisioned directory Members plus one existing Entra-linked Apperture user, skip the labeled steps and record which identities were unavailable. Do not introduce debug identity bypasses.

## 1. Admin directory provisioning

1. Sign in as Platform Administrator.
2. Open **Config → Governance → Users**. Confirm **New user** still creates a local account.
3. Open **Add from Microsoft Entra**.
4. If more than one enabled registration exists, select the intended directory.
5. Search by display name, mail, or user principal name.
6. Confirm people who already have an Apperture Entra identity **in this tenant** show **Already in Apperture**. Status must not flip because email or UPN matches a different object id, and must not flip because the same object id exists in a different tenant.
7. Confirm disabled directory accounts cannot be selected.
8. Select an unprovisioned **Member**. Guest may be selected only if you intend to provision a guest.
9. Choose an initial role if you have role-assignment permission; otherwise leave None.
10. If Graph `mail` is present, the welcome checkbox is available. This is not a password setup invitation and must not verify contact email.
11. Add the person. Confirm they appear on Users with Entra = Yes and Local = No.
12. Open the user. Confirm there is no local password / local identity.
13. If welcome mail was attempted and SMTP is configured, confirm delivery or the operator warning that mail failed while the user still exists.
14. Sign in as that person with Microsoft. Confirm the same User is resolved and the Entra identity is now proven (Account / identity shows a successful Microsoft sign-in). Pending-to-proven is the existing IAM-1 proof path.

## 2. Access request

1. As Platform Administrator, open **Config → Authentication → Entra tenant setup**.
2. Enable **Allow directory members to request Apperture access**. Leave guest requests off unless you are explicitly testing guests.
3. Sign out.
4. Sign in with Microsoft as a **different** unprovisioned tenant Member.
5. Confirm they land on **Request access**, not the management console. They must not open Dashboard, Governance, or Account as an authorized user.
6. Submit **Request access**. Confirm the pending state (administrator approval required).
7. Attempt to navigate to `/dashboard` or `/config/governance/users`. Confirm they return to Request access or sign-in, not an authorized session.
8. Sign in as Platform Administrator. Open **Governance → Users → Requests**.
9. Confirm the request shows name, directory address, Microsoft Entra · registration name, Member, and requested date. Technical ids stay behind **Technical details**.
10. **Approve access** and assign an initial role if authorized.
11. Sign in again as the requester with Microsoft. Confirm normal access and the same immutable Entra identity. Approval must not have created a session by itself.

## 3. Rejection

12. As another eligible unprovisioned Member (or the same person after turning requests back on and waiting out any cooldown if you reused them), submit a request.
13. As administrator, **Reject**.
14. Confirm no new User was created.
15. As the requester, confirm they only see that access was not approved — no administrator names, roles, or directory of users.

## 4. Policy

16. Turn member access requests **off** for the registration.
17. Sign in with Microsoft as another unknown tenant Member. Confirm they cannot submit a request. The page may explain that requests are not enabled. They still must not receive an application session.

## 5. Regressions

18. An existing Entra-linked user signs in with Microsoft and reaches the console.
19. An existing local account signs in according to current Entra-primary / local policy.
20. Recovery Access local password + MFA still works. Recovery Access never appears in directory provisioning or access-request review.
21. Microsoft sign-out still returns to Apperture login and does not leave an authorized session.
22. Account / Secured account still requires recent account verification for contact-email management, including the first contact email (API and UI).

## Gaps when extra Entra users are unavailable

| Step | Narrowest supported alternative |
|---|---|
| Directory search identifies already-provisioned users | Search for the Platform Administrator's own Entra identity if it is linked. |
| Provision an unprovisioned Member | Use any unused directory Member in the configured tenant. If none exist, stop after search/status checks. |
| Access-request happy path | Requires a second unprovisioned Member. Unit tests cover eligibility, limited session, approve/reject, and races if a second human is unavailable. |
| Guest self-request | Leave guest requests off. Admin may still provision a Guest from the directory picker if one exists. |
| Welcome email | If Graph `mail` is empty, provisioning must still succeed. If SMTP is down, confirm the operator warning and that the User remains. |

Do not use Graph tokens in the browser, do not treat UPN as a mailbox, and do not copy Entra mail into verified `Users.EmailAddress`.
