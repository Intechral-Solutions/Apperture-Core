# AUD-1.2 identity and governance: implementation record

Date: 2026-10-07

Status: **In progress on branch `aud-1.2-identity-governance-audit`.** This record holds the writer inventory and the package decisions; the sections after §4 are completed as each family lands.

Base: `origin/master` at `a300bcd` (merge of PR #15; post-merge CI run 37552614278 green).

Contract: [AUD-1.0 design gate](aud1-design-gate.md), revision 3, §22 "AUD-1.2 Identity and governance". Builds on the [AUD-1.1 foundation](aud1-1-foundation-record.md). Nothing here changes the contract. Where the gate leaves a choice to the package, §3 records it.

## 1. Scope

In: the identity and access-governance writers of gate §13.1 row 1 (user, role, assignment, permission, identity-link and credential administration; provisioning; access-request create, approve and reject; account services; Recovery Access administration; MFA; Entra primary-auth policy; Entra tenant registration and policy; bootstrap first administrator), the W2 sign-in and step-up successes, the W3 attempts and refusals of those families, and the new coverage the gate names (sign-out, Entra remote failure, tenant registration, sign-in policy, enrollment). Also the corrected actors, the verified external identity, `viaRecoveryAccess`, permission diffs, and invitation and mail as separate rows.

Out, with owner:

| Not in AUD-1.2 | Owner |
|---|---|
| Sign-in failure rows (`auth.local.login.failed`, `auth.emergency.login.failed`, `auth.break_glass.login.failed`, `auth.break_glass.rejected_disabled`) and `auth.unlinked_principal`. They keep their actor (`anonymous`, or the session id) and stay on the `append` bridge. Gate §10.4: "IAM-11.4 owns sign-in failure records" | IAM-11.4 |
| `audit.read.denied` and its actor | AUD-1.5 |
| `resource_context.*`, ownership moves, `governance.runtime.shutdown.request.created`, integration, schedule, settings, secret store, module lifecycle, connections, notification definitions | AUD-1.4 |
| `credential.password_set` as an administrator action is converted as it is. Its replacement by initiate-reset events | IAM-11.3 |
| Authorization changes, including the `role.manage` escalation finding. Recording permission diffs is attribution, not a fix or a disposition | IAM-10 |
| Session, throttling and lockout behaviour | IAM-11.4 |
| First-admin takeover window and race hardening | OPS-1.4 |

## 2. Writer inventory

W1 = required mutation, W2 = required security event, W3 = attempt or refusal. `CP` = `AuditActor.CurrentPrincipal`. "Unit" = one audited unit of work (`IAuditedUnitOfWork`). Status is updated as families land.

| Family and service | Actions | Class | Actor (correct) | Target | Boundary after conversion |
|---|---|---|---|---|---|
| Roles, permissions, assignments, users, links, credentials: `UserManagementService` | `governance.role.created`, `.metadata.updated`, `.deleted`; `governance.permission.assignment.updated` (adds and removes); `user.created`; `governance.role.assignment.created`, `.removed`; `identity_link.entra.pending`, `identity_link.added`, `identity_link.removed`; `credential.password_set`, `credential.disabled`, `credential.local.reenabled_strong_authentication` | W1 | CP | role, user or identity link | One unit per operation; marker row removed; `viaRecoveryAccess` from the principal |
| same | `governance.role.mutation_rejected`, `auth_admin.blocked.*`, `auth.strong_authentication.insufficient` | W3 | CP | role or user | Decided in the attempt, written after rollback through `TryWriteAsync` |
| Recovery Access administration: `EmergencyAccessService` | `emergency.access.designated`, `.provisioned`, `.revoked`, `credential.password_set` (command line); `emergency.access.canonicalized` (done in AUD-1.1) | W1 | `emergency-cli` (or CP in a session) | user | One unit; canonicalization joins as a secondary row |
| same | `emergency.auth.blocked.unsafe_operation` | W3 | CP; target is the protected user (corrects §15.3) | user | Own transaction, never the shared context |
| MFA: `NativeMfaService` | `auth.mfa.enrolled`, `auth.mfa.recovery_codes.generated`, `auth.mfa.reset` | W1 | `emergency-cli`; target the Recovery Access user (corrects §15.3) | user | One unit per verb |
| same | `auth.mfa.recovery_code.consumed` | W1 | the user being authenticated | user | Unit inside the sign-in flow; a wrong code is a no-op |
| Bootstrap: `BootstrapController`, `FirstAdminBootstrapService` | `bootstrap.first_admin.created` (W1); `bootstrap.first_admin.rejected_*` (W3) | W1, W3 | `anonymous` | user; instance | Creation and credential in one unit |
| Provisioning: `UserProvisioningService` | `user.provisioned.entra_directory`, `user.provisioned.entra_access_request`, `entra.access_request.approved`, `entra.access_request.reconciled_by_provisioning`; `user.provisioning.welcome.sent`, `.failed` (W3, external attempt) | W1, W3 | CP (administrator) | user; access request | One unit; welcome mail after commit |
| Access requests: `EntraAccessRequestService` | `entra.access_request.created`; `entra.access_request.rejected` | W1 | created: external identity (verified triple); rejected: CP | access request | One unit |
| Entra association: `EntraIdentityAssociationService`, `EntraPrimaryAuthenticationPolicy` | `identity_link.entra.proven`, `.linked`, `credential.local.disabled_entra_primary` (W1); `identity_link.entra.link_failed`, `.collision`, `.tenant_mismatch`, `auth.entra.login.rejected_unlinked`, `emergency.local.protected`, `credential.local.disable_deferred_last_entra_independent_admin` (W3) | W1, W3 | the owning or initiating user; external for rejected identities | identity link; external identity | Proof, link and policy changes each in a unit; the callback has no session |
| Account services: `AccountInvitationService`, `AccountContactService`, `AccountProfileService`, `ForgottenPasswordService` | `account.invitation.sent`, `.resent`, `.accepted`; `credential.password.setup_completed`; `account.email.*`; `account.profile.*`; `account.notification.*` (own preferences); `credential.password.reset_requested`, `.reset_completed` | W1 | invitations: the administrator (corrects §15.3); self-service: CP; token flows: the verified user; reset request: `anonymous` | user | Token and state in one unit; mail after commit |
| same | `account.security_mail.delivery_attempted` (new, replaces failure-only rows), `account.profile.avatar.upload_rejected`, suppressed reset request | W3 | as above | user | Own transaction, after commit |
| Sign-in successes | `auth.local.login.success`, `auth.emergency.login.success`, `auth.break_glass.login.success`, `auth.entra.login.success`, `auth.strong_authentication.step_up.satisfied` | W2 | the verified user | user | Row commits **before** the cookie or redirect |
| Step-up and Entra attempts | `auth.strong_authentication.step_up.*`, `identity_link.entra.*` failures | W3 | the initiating user | user | Swallowed on failure |
| `AuthController` password change | `auth.local.password_change.success` (W1), `auth.local.password_change.failed` (W3) | W1, W3 | CP | user | Unit |
| New: sign-out | `auth.sign_out` | W3 | CP; no row when no user resolves | user | Written before the sign-out call; never blocks it |
| New: Entra remote failure | `auth.entra.login.failed` | W3 | `anonymous` | none | Never blocks the single redirect |
| New: tenant registration | `entra.tenant_registration.created`, `.updated`, `.deleted`, `.activated`; `entra.access_request.policy.changed` | W1 | CP | tenant registration | One unit; secret-store effects outside it |
| New: sign-in policy | `auth.sign_in_policy.updated` | W1 | CP | auth policy | One unit |
| Enrollment: `EnrollController`, `SqlRuntimeEnrollmentService` | `governance.runtime.enrollment.invitation.created`; `governance.runtime.enrollment.completed` (new) | W1 | invitation: CP; enrollment: `anonymous` (token-authenticated, no identity) | runtime instance | One unit |
| Recovery restore: `RealBootstrapRecoveryService` (no production caller today) | `recovery.restore_platform_access.executed` (W1); `.denied`, `.failed` (W3) | W1, W3 | `recovery` | user; instance | Converted mechanically; verified at the service |

## 3. Decisions of the package

| # | Decision | Basis |
|---|---|---|
| D1 | A validated Entra identity that is rejected (unlinked, tenant mismatch) is recorded as the `external` triple. `anonymous` only when `oid` or `tid` is missing or invalid | Gate §10.4 names the OIDC callback as a source of the verified triple; "failed or unvalidated authentication" is `anonymous` |
| D2 | One public seam for an explicit actor: `AuditActor.AuthenticatedUser(UserId)` (credentials or a one-time token verified by this request's own flow) and `AuditActor.ValidatedExternalIdentity(AuditExternalActor)`. A test pins the call sites | A sign-in has no principal context yet; `CurrentPrincipal` cannot name the user being authenticated |
| D3 | `viaRecoveryAccess` is a reserved details property the recorder adds from the principal (`IRecoveryAccessSession`). Producers cannot declare or pass it. The pre-mutation marker call is removed; `governance.break_glass.admin_operation` stays in the catalog for history | Gate §22 |
| D4 | A one-time MFA recovery code consumed for a sign-in whose success row then fails stays consumed; the consumption has its own W1 row | Not undoing a consumed one-time secret; the user retries |
| D5 | An operation whose primary row depends on state read inside the transaction declares its possible primary actions; verification accepts any of them | Keeps both action names without a pre-read race |
| D6 | `emergency.local.protected` and `credential.local.disable_deferred_last_entra_independent_admin` mutate nothing, so they are W3 attempts (basis `attempt`), not W1. Catalog correction | They were declared W1 in AUD-1.1; no state changes |
| D7 | An operation that changes nothing declares itself a no-op and writes no row (already assigned role, absent link, unchanged policy, empty permission diff) | The row would assert a change that did not commit (§11.6) |
| D8 | Mail: the database change is a `change` row; the send is a separate W3 `external_attempt` row (`account.security_mail.delivery_attempted`, outcome `succeeded`, `failed` or `unknown`) written after commit with the last attempt's token. The row never claims delivery | Gate §11.6, §13.3 |
| D9 | Permission diffs list the identity of each permission (`action@resourceType:scopeKey`), computed by the SQL repository from the row it updates under its concurrency token | Gate §22; the diff is the committed one |
| D10 | A runtime enrolling with an invitation token is recorded as `anonymous`: the token is a capability, not an identity. The creator is in the invitation row | Closed actor vocabulary (§10.8) |
| D11 | The Part-1 matrix differs from the gate in two places; the gate governs: runtime shutdown request belongs to AUD-1.4 (gate §22), and permission diffs are recorded here | Gate §9, §22 |
| D12 | `AuditOutcomeUnknownException` and `AuditOperationNotCompletedException` map centrally to HTTP 500 problem details with `code` and `operationEventId`, never to a refusal | Gate §13.2 |

Further sections (acceptance mapping, authentication-result behaviour, external effects, validation results) are added when the package is complete.
