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
| D13 | A one-time account link (setup, contact verification, reset) gets its id and plaintext once, before the unit (`IAccountActionTokenService.Prepare`). Every attempt issues that same token, so a rerun after a confirmed rollback mints no second link, and the plaintext held after a verified commit belongs to exactly the committed row (checked against the primary row's `requestId`). The plaintext is never recorded, logged or reconstructed | Clarification 5; gate §13.3 |
| D14 | Bounded recovery when a committed request's link is not in hand: nothing is sent and the caller is told `account.security_mail.link_unavailable` (invitation, contact) or gets the usual acknowledgement (reset); sending the request again issues a new link and revokes the unsent one. By D13 this cannot occur after a verified commit; it remains as a guard. An outcome that cannot be verified is `outcome_unknown` and sends nothing; the administrator or user resends | Clarification 5 |
| D15 | Every refusal of a token flow (policy, system-managed account, address in use) is decided before the link is consumed, so a refused attempt leaves the link usable. Before AUD-1.2 a policy refusal after consumption burned the link | Clarification 6 |
| D16 | Own notification preferences target the persisted preference row (`NotificationUserPreferences.Id`), the one record every change of that user, definition and channel writes. The operator services report whether they wrote anything (`Update*EnabledAsync`) and skip writing an equal row | Clarification 7, D7 |
| D17 | `PUT /api/me/notification-subscriptions/{id}` wrote the same email preference with no audit row; it now goes through the audited account service | Inventory finding |
| D18 | Choosing built-in artwork is recorded whenever it changes something (the avatar, or deleting stored images); before, only image deletion was recorded. A unit cannot commit an unrecorded change | Gate §13.4 |
| D19 | A forgotten-password request matching no eligible account writes the new W3 `credential.password.reset_request.suppressed` (anonymous, no target, no fields) instead of the W1 request row with `suppressed` text | D7; enumeration protection kept |
| D20 | Enrollment invitation creation records in the enrollment service's unit, not after it in the controller. The invitation's row id is recorded; the token never is | Gate §13.4 |
| D21 | A user created with a setup invitation stages the invitation in the creation unit: a refused invitation (address in use) creates no user. Before, the user committed and the invitation failed after | Clarification 4 |
| D22 | Which checks have completed when an Entra identity is recorded as `external`: the OIDC handler has validated the id_token (signature against the authority's keys, audience, lifetime, nonce) and the protected, correlation-bound state before `OnTokenValidated`; the association service then requires a GUID `oid` and `tid`. A tenant mismatch is recorded only after all of these, with the token's own tenant (D1). A missing or malformed `oid`/`tid` is refused unrecorded by the association service; the remote-failure path records those as `anonymous` | Clarification 2 |
| D23 | The access-request session cookie is issued only in `OnTokenValidated`, after D22's checks, the instance tenant check and the no-link result, in a data-protected cookie of its own scheme with a 20-minute lifetime. Its submission is recorded as `ValidatedExternalIdentity`; email and UPN are not recorded | Clarification 2 |
| D24 | A self-link's initiating user is set by `GET /signin/link` from the authenticated session (principal resolved to a user), and carried only in the OIDC handler's protected properties, which the handler unprotects and binds to the correlation cookie before the callback runs. That is the actor of the link, the proof and every refusal. `AuditActor.AuthenticatedUser` documents it; a test pins the call sites | Clarification 2 |
| D25 | Refused links (`identity_link.entra.link_failed`, `.collision`) have no link row to target; their target type is corrected from `identity_link` to `external_identity` (`entra/{tid}/{oid}`). Proven and linked rows target the persisted link id | Clarification 7 |
| D26 | The Entra-primary policy acts on its own after a link is proven; it is recorded as `system:entra-primary-policy` with the user as the target. Before, the user was recorded as the actor | Gate §15.3 (actor vs target) |
| D27 | The initial role given at provisioning commits in the provisioning unit with its own `governance.role.assignment.created` row; before it had none | Gate §13.4 |
| D28 | The welcome mail keeps its two W3 actions: `user.provisioning.welcome.sent` (`succeeded`) and `.failed` (`failed`, or `unknown` when the send threw), written after commit. Address and transport text are not recorded | D8 |
| D29 | Refreshing the sign-in time of an already proven link (sign-in or repeat self-link) changes no security state and writes no row; it stays outside any unit | D7 |
| D30 | Sign-in successes (local, Recovery Access, break-glass, Entra) and a satisfied step-up are W2 rows written as the authenticated user before the session is issued: before `SignInAsync` in the controller, and inside `OnTokenValidated` (the handler issues the cookie only after it returns). A failed write throws; locally the request fails without a cookie, and for Entra the handler fails the sign-in through `OnRemoteFailure`, which redirects once to the login page | Acceptance: auth audit failure withholds the success result |
| D31 | Step-up rows are the initiating user's (D24). A callback whose protected state carries no initiating user is refused and logged, not recorded: only this host's challenge produces that state, and it always sets one | Clarification 2 |
| D32 | `auth.sign_out` is written before the session ends, so the principal still resolves through the user resolver; no row when no user resolves (an access-request session, an anonymous post). Resolution and write failures are swallowed; the sign-out always completes | Gate §22 |
| D33 | `auth.entra.login.failed` is anonymous with no target: the flow comes from the handler's protected state (`unknown` when the state did not validate) and the reason is `cancelled` (`access_denied`) or `failed`. Provider text and error descriptions are never recorded | Gate §22; D22 |
| D34 | The self-service password change is one unit (checks, password, revocation of the account's password links, row); each refusal is a W3 `auth.local.password_change.failed` row with a stable reason, written after nothing changed | Clarification 6 |
| D35 | Tenant registrations: the secret store runs its own transaction and cannot join a unit. Create stores a supplied secret first under the registration id chosen before the unit and retires it when nothing committed (never on `outcome_unknown`); delete retires the secret after the delete committed (before, it was retired first); update replaces the secret before the unit, as before. Rows record that the client authentication was replaced, never the secret, and name the changed settings, never their values | Gate §13.3, §22 |
| D36 | `SqlEntraInstanceRegistrationRepository.SetActiveAsync` joins the current transaction inside a unit (as the AUD-1.1 rename does) | Gate §13.4 |
| D37 | A registration update, activation or sign-in policy update that changes nothing writes no row and answers as before; a stale row version is still a conflict. The lockout guards are read inside the transaction; their refusals stay unrecorded, as before (no approved W3 action) | D7; clarification 6 |
| D38 | First-administrator bootstrap is anonymous. The no-users check, role, identity checks, user, link, role assignment, credential and created row are one unit in the bootstrap service; the controller records refusals as W3 rows with a stable reason and never the request. Closing the takeover window is OPS-1.4 | Gate §22; D7 |
| D39 | Bootstrap recovery restore (no production caller today) records as `recovery`: the assignment and its row in one unit; denial and an unknown target are W3 rows. A target not found has no user to target, so `recovery.restore_platform_access.failed` targets the instance (catalog correction). A target that already holds the role changes nothing and writes no row | Gate §22; D7 |

Further sections (acceptance mapping, authentication-result behaviour, external effects, validation results) are added when the package is complete.
