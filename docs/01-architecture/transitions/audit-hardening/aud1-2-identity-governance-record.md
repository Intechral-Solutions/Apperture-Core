# AUD-1.2 identity and governance: implementation record

Date: 2026-10-06

Status: **Implemented on branch `aud-1.2-identity-governance-audit`, independently reviewed 2026-10-07 (§10), with the signed id_token policy adopted after the review (D50, §11); pushed for review as a pull request against `master`, not merged.** This record holds the writer inventory, the package decisions, the acceptance mapping, the failure and external-effect behaviour, the findings with owners and the scope left for later packages. Where the review corrected a claim, the decision or section says so and §10 lists the correction.

Base: `origin/master` at `a300bcd` (merge of PR #15; post-merge CI run 37552614278 green).

Contract: [AUD-1.0 design gate](aud1-design-gate.md), revision 3, §22 "AUD-1.2 Identity and governance". Builds on the [AUD-1.1 foundation](aud1-1-foundation-record.md). Nothing here changes the contract. Where the gate leaves a choice to the package, §3 records it.

## 1. Scope

In: the identity and access-governance writers of gate §13.1 row 1 (user, role, assignment, permission, identity-link and credential administration; provisioning; access-request create, approve and reject; account services; Recovery Access administration; MFA; Entra primary-auth policy; Entra tenant registration and policy; bootstrap first administrator, including the administrators the host applies from its `BootstrapPlatformAdmin` configuration at start), the W2 sign-in and step-up successes, the W3 attempts and refusals of those families (including the tenant-registration guard refusals), and the new coverage the gate names (sign-out, Entra remote failure, tenant registration, sign-in policy, enrollment). Also the corrected actors, the verified external identity, `viaRecoveryAccess`, permission diffs, and invitation and mail as separate rows.

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

W1 = required mutation, W2 = required security event, W3 = attempt or refusal. `CP` = `AuditActor.CurrentPrincipal`. "Unit" = one audited unit of work (`IAuditedUnitOfWork`). Status is as committed on the branch.

| Family and service | Actions | Class | Actor (correct) | Target | Boundary after conversion |
|---|---|---|---|---|---|
| Roles, permissions, assignments, users, links, credentials: `UserManagementService` | `governance.role.created`, `.metadata.updated`, `.deleted`; `governance.permission.assignment.updated` (adds and removes); `user.created`; `governance.role.assignment.created`, `.removed`; `identity_link.entra.pending`, `identity_link.added`, `identity_link.removed`; `credential.password_set`, `credential.disabled`, `credential.local.reenabled_strong_authentication` | W1 | CP | role, user or identity link | One unit per operation; marker row removed; `viaRecoveryAccess` from the principal |
| same | `governance.role.mutation_rejected`, `auth_admin.blocked.*`, `auth.strong_authentication.insufficient` | W3 | CP | role or user | Decided in the attempt, written after rollback through `TryWriteAsync` |
| Recovery Access administration: `EmergencyAccessService` | `emergency.access.designated`, `.provisioned`, `.revoked`, `credential.password_set` (command line); `emergency.access.canonicalized` (done in AUD-1.1) | W1 | `emergency-cli` (or CP in a session) | user | One unit; canonicalization joins as a secondary row |
| same | `emergency.auth.blocked.unsafe_operation` | W3 | CP; target is the protected user (corrects §15.3) | user | Own transaction, never the shared context |
| MFA: `NativeMfaService` | `auth.mfa.enrolled`, `auth.mfa.recovery_codes.generated`, `auth.mfa.reset` | W1 | `emergency-cli`; target the Recovery Access user (corrects §15.3) | user | One unit per verb |
| same | `auth.mfa.recovery_code.consumed` | W1 | the user being authenticated | user | Unit inside the sign-in flow; a wrong code is a no-op |
| Bootstrap: `BootstrapController`, `FirstAdminBootstrapService` | `bootstrap.first_admin.created` (W1); `bootstrap.first_admin.rejected_*` (W3) | W1, W3 | `anonymous` | user; instance | Creation and credential in one unit |
| Configured administrators (review): `BootstrapPlatformAdminAssignment`, at every host start | `bootstrap.configured_admin.created` (new) with `governance.role.assignment.created`; the assignment row alone for an existing user | W1 | `system:bootstrap-configuration` ("Startup configuration") | user | One unit per configured principal (D40) |
| Provisioning: `UserProvisioningService` | `user.provisioned.entra_directory`, `user.provisioned.entra_access_request`, `entra.access_request.approved`, `entra.access_request.reconciled_by_provisioning`; `user.provisioning.welcome.sent`, `.failed` (W3, external attempt) | W1, W3 | CP (administrator) | user; access request | One unit; welcome mail after commit |
| Access requests: `EntraAccessRequestService` | `entra.access_request.created`; `entra.access_request.rejected` | W1 | created: external identity (verified triple); rejected: CP | access request | One unit |
| Entra association: `EntraIdentityAssociationService`, `EntraPrimaryAuthenticationPolicy`, and (review) `SqlPrincipalToUserResolver` for a pending link first used by a live session (D46) | `identity_link.entra.proven`, `.linked`, `credential.local.disabled_entra_primary` (W1); `identity_link.entra.link_failed`, `.collision`, `.tenant_mismatch`, `auth.entra.login.rejected_unlinked`, `emergency.local.protected`, `credential.local.disable_deferred_last_entra_independent_admin` (W3) | W1, W3 | proof and link: the owning or initiating user (D24); rejected identities: external (D1, D22); policy: `system:entra-primary-policy` (D26) | identity link; external identity; user | Proof, link and policy changes each in a unit; the callback has no session |
| Account services: `AccountInvitationService`, `AccountContactService`, `AccountProfileService`, `ForgottenPasswordService` | `account.invitation.sent`, `.resent`, `.accepted`; `credential.password.setup_completed`; `account.email.*`; `account.profile.*`; `account.notification.*` (own preferences); `credential.password.reset_requested`, `.reset_completed` | W1 | invitations: the administrator (corrects §15.3); self-service: CP; token flows: the verified user; reset request: `anonymous` | user | Token and state in one unit; mail after commit |
| same | `account.security_mail.delivery_attempted` (new, replaces failure-only rows), `account.profile.avatar.upload_rejected`, suppressed reset request | W3 | as above | user | Own transaction, after commit |
| Sign-in successes | `auth.local.login.success`, `auth.emergency.login.success`, `auth.break_glass.login.success`, `auth.entra.login.success`, `auth.strong_authentication.step_up.satisfied` | W2 | the verified user | user | Row commits **before** the cookie or redirect |
| Step-up and Entra attempts | `auth.strong_authentication.step_up.*`, `identity_link.entra.*` failures | W3 | the initiating user | user | Swallowed on failure |
| `AuthController` password change | `auth.local.password_change.success` (W1), `auth.local.password_change.failed` (W3) | W1, W3 | CP | user | Unit |
| New: sign-out | `auth.sign_out` | W3 | CP; no row when no user resolves | user | Written before the sign-out call; never blocks it |
| New: Entra remote failure | `auth.entra.login.failed` | W3 | `anonymous` | none | Never blocks the single redirect |
| New: tenant registration | `entra.tenant_registration.created`, `.updated`, `.deleted`, `.activated`; `entra.access_request.policy.changed` | W1 | CP | tenant registration | One unit; secret-store effects outside it, ordered so the live credential changes only with a committed row (D35, corrected) |
| same (review) | `entra.tenant_registration.change_refused` (new): the sign-in lockout guard on update, activate and delete; activating a disabled registration | W3 | CP | tenant registration | Decided in the unit, written after rollback (D41) |
| New: sign-in policy | `auth.sign_in_policy.updated` | W1 | CP | auth policy | One unit |
| Enrollment: `EnrollController`, `SqlRuntimeEnrollmentService` | `governance.runtime.enrollment.invitation.created`; `governance.runtime.enrollment.completed` (new) | W1 | invitation: CP; enrollment: `anonymous` (token-authenticated, no identity) | runtime instance | One unit |
| Recovery restore: `RealBootstrapRecoveryService` (no production caller today) | `recovery.restore_platform_access.executed` (W1); `.denied`, `.failed` (W3) | W1, W3 | `recovery` (system kind) | user; instance | One unit resolved from a scope, as the host does; audit failure and lost acknowledgement proven against SQL Server |
| Found during the package: own email preference through `MeNotificationSubscriptionsController` | `account.notification.email.opted_in`, `.opted_out` | W1 | CP | notification preference row | Routed through the audited account service (D17); it wrote the preference with no row |

Status at the end of the package: every row above is converted as listed, with the exceptions and corrections recorded in §3. No converted writer reaches the append bridge (pinned by `Aud12ConvertedWritersTests`); the remaining bridge rows in the family are the sign-in failures that IAM-11.4 owns. Catalog: 147 actions from AUD-1.1 plus 12 added here (`AuditActionCatalogTests` pins the list): `account.security_mail.delivery_attempted`, `credential.password.reset_request.suppressed`, `governance.runtime.enrollment.completed`, `auth.entra.login.failed`, `auth.sign_out`, `entra.tenant_registration.created`, `.updated`, `.deleted`, `.activated`, `.change_refused` (review), `auth.sign_in_policy.updated`, `bootstrap.configured_admin.created` (review). The review also adds the actor literal `system:bootstrap-configuration`, labelled "Startup configuration". Historical entries stay declared (for example `account.security_mail.delivery_failed`, `governance.break_glass.admin_operation`).

## 3. Decisions of the package

| # | Decision | Basis |
|---|---|---|
| D1 | A validated Entra identity that is rejected (unlinked, tenant mismatch) is recorded as the `external` triple. `anonymous` only when `oid` or `tid` is missing or invalid | Gate §10.4 names the OIDC callback as a source of the verified triple; "failed or unvalidated authentication" is `anonymous` |
| D2 | One public seam for an explicit actor: `AuditActor.AuthenticatedUser(UserId)` (credentials or a one-time token verified by this request's own flow) and `AuditActor.ValidatedExternalIdentity(AuditExternalActor)`. A test pins the call sites | A sign-in has no principal context yet; `CurrentPrincipal` cannot name the user being authenticated |
| D3 | `viaRecoveryAccess` is a reserved details property the recorder adds from the principal (`IRecoveryAccessSession`). Producers cannot declare or pass it. The pre-mutation marker call is removed; `governance.break_glass.admin_operation` stays in the catalog for history | Gate §22 |
| D4 | A one-time MFA recovery code consumed for a sign-in whose success row then fails stays consumed; the consumption has its own W1 row, and no session is issued. The user continues with the authenticator or a different recovery code, never the consumed one (the form says so and clears it, D49). An unknown consumption outcome is `outcome_unknown`, never a refusal or a rollback. Corrected in review: "the user retries" invited the consumed code | Not undoing a consumed one-time secret |
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
| D22 | Which checks have completed when an Entra identity is recorded as `external` (corrected in review against the installed handler, `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.12, driven for real in `EntraStepUpCallbackTests`). Before `OnTokenValidated`: the state is unprotected and bound to this browser's correlation cookie; the code is redeemed over the back channel at the metadata's token endpoint with PKCE and the client secret; the id_token's issuer, audience and lifetime are validated, and its signature against the authority's keys **when it has one** (the handler accepts an unsigned id_token from the token endpoint in the code flow, relying on the TLS back channel; a probe confirmed it. Since D50 the event requires a signature first). The nonce cookie is read but **not validated**, and nor are the token response's required claims and at_hash: the handler does that only after the event returns, and never when the event handles the response itself. The review's fix (D42) runs that validation first in the event, so every check above has completed before anything is recorded or issued. The association service then requires GUID `oid` and `tid`; well-formed GUIDs are a format check, not the verification. A tenant mismatch is recorded only after all of this, with the token's own tenant (D1). A missing or malformed `oid`/`tid` is refused unrecorded; a token or response that fails validation goes through `OnRemoteFailure`, recorded as `anonymous` | Clarification 2; review |
| D23 | The access-request session cookie is issued only in `OnTokenValidated`, after D22's checks, the instance tenant check and the no-link result, in a data-protected cookie of its own scheme with a 20-minute lifetime. Its submission is recorded as `ValidatedExternalIdentity`; email and UPN are not recorded | Clarification 2 |
| D24 | A self-link's initiating user is set by `GET /signin/link` from the authenticated session (principal resolved to a user), and carried only in the OIDC handler's protected properties, which the handler unprotects and binds to the correlation cookie before the callback runs. That is the actor of the link, the proof and every refusal. `AuditActor.AuthenticatedUser` documents it; a test pins the call sites | Clarification 2 |
| D25 | Refused links (`identity_link.entra.link_failed`, `.collision`) have no link row to target; their target type is corrected from `identity_link` to `external_identity` (`entra/{tid}/{oid}`). Proven and linked rows target the persisted link id | Clarification 7 |
| D26 | The Entra-primary policy acts on its own after a link is proven; it is recorded as `system:entra-primary-policy` with the user as the target. Before, the user was recorded as the actor | Gate §15.3 (actor vs target) |
| D27 | The initial role given at provisioning commits in the provisioning unit with its own `governance.role.assignment.created` row; before it had none | Gate §13.4 |
| D28 | The welcome mail keeps its two W3 actions: `user.provisioning.welcome.sent` (`succeeded`) and `.failed` (`failed`, or `unknown` when the send threw), written after commit. Address and transport text are not recorded | D8 |
| D29 | Refreshing the sign-in time of an already proven link (sign-in or repeat self-link) changes no security state and writes no row; it stays outside any unit | D7 |
| D30 | Sign-in successes (local, Recovery Access, break-glass, Entra) and a satisfied step-up are W2 rows written as the authenticated user before the session is issued: before `SignInAsync` in the controller, and inside `OnTokenValidated` (the handler issues the cookie only after it returns), after the token-response validation of D42. A failed write throws; locally the request fails without a cookie, and for Entra the handler fails the sign-in through `OnRemoteFailure`, which redirects once to the login page. Two Entra paths issue their own session inside the event and end it there (a local step-up session, the access-request session); before D42 those never reached the handler's later checks | Acceptance: auth audit failure withholds the success result |
| D31 | Step-up rows are the initiating user's (D24). A callback whose protected state carries no initiating user is refused and logged, not recorded: only this host's challenge produces that state, and it always sets one | Clarification 2 |
| D32 | `auth.sign_out` is written before the session ends, so the principal still resolves through the user resolver; no row when no user resolves (an access-request session, an anonymous post). Resolution and write failures are swallowed; the sign-out always completes | Gate §22 |
| D33 | `auth.entra.login.failed` is anonymous with no target: the flow comes from the handler's protected state (`unknown` when the state did not validate) and the reason is `cancelled` (`access_denied`) or `failed`. Provider text and error descriptions are never recorded | Gate §22; D22 |
| D34 | The self-service password change is one unit (checks, password, revocation of the account's password links, row); each refusal is a W3 `auth.local.password_change.failed` row with a stable reason, written after nothing changed | Clarification 6 |
| D35 | Tenant registrations: the secret store runs its own transaction and cannot join a unit. Corrected in review: update used to add **and activate** a new version on the secret the live registration already names, before the unit, so a refused, conflicted, rolled-back or unknown update still changed the credential the registration resolves (the stored ref takes precedence over the deployment secret for sign-in, directory discovery and access requests). Now create and update both store a supplied secret as a **secret of its own** before the unit, and the registration row that names it commits in the unit. Afterwards exactly one secret is unused and is retired: the replaced one after a confirmed or verified commit, the new one when nothing committed (refusal, concurrency conflict, audit failure, confirmed rollback). After `outcome_unknown` neither is retired, since either may be named by a committed row. Create keeps a secret whose commit was verified even when reading the registration back fails. Delete retires the secret after the delete committed. Retirement is best effort and logged; it never changes the outcome. Rows record that the client authentication was replaced, never the secret or its ref, and name the changed settings, never their values. Secret-store events through the recorder remain AUD-1.4 | Gate §13.3, §22; review |
| D36 | `SqlEntraInstanceRegistrationRepository.SetActiveAsync` joins the current transaction inside a unit (as the AUD-1.1 rename does) | Gate §13.4 |
| D37 | A registration update, activation or sign-in policy update that changes nothing writes no row and answers as before; a stale row version is still a conflict (unrecorded, as any concurrency conflict). The lockout guards are read inside the transaction. Corrected in review: their refusals are now recorded (D41); a missing action constant was an implementation gap, not grounds to defer | D7; clarification 6; gate §13.1 W3 |
| D38 | First-administrator bootstrap is anonymous. The no-users check, role, identity checks, user, link, role assignment, credential and created row are one unit in the bootstrap service; the controller records the already-initialized and invalid-request refusals as W3 rows with a stable reason and never the request. Corrected in review: a duplicate identity (including a reserved or Entra request) and a missing role are refused unrecorded, as before (§8). The unit does not serialize two concurrent requests that both read zero users; closing the takeover window and that race is OPS-1.4 | Gate §22; D7 |
| D39 | Bootstrap recovery restore (no production caller today) records as `recovery`: the assignment and its row in one unit; denial and an unknown target are W3 rows. A target not found has no user to target, so `recovery.restore_platform_access.failed` targets the instance (catalog correction). A target that already holds the role changes nothing and writes no row. Corrected in review: after a verified commit the result comes from the committed row's user id, never from a fresh read that could report "target not found" (D48) | Gate §22; D7 |
| D40 | Review: the administrators the host applies from `BootstrapPlatformAdmin` at every start are W1 (user and role-assignment administration, bootstrap first administrator, OR-1). Each configured principal is one unit recorded as `system:bootstrap-configuration`: the lookup, the created user with its link and the Platform Administrator role, or the assignment to an existing user, and their rows commit together (`bootstrap.configured_admin.created` with `governance.role.assignment.created`, or the assignment row alone). Already holding the role changes nothing and writes no row. Configuration that cannot be applied is skipped and logged as before. No authoritative document assigns this coverage elsewhere: OPS-1.4 owns the first-admin mechanism (setup secret or CLI verb), and the 2026-03 decision note only said a follow-up "may add durable audit rows" | Gate §13.1, OR-1; review |
| D41 | Review: a tenant-registration change refused by a state guard read in its unit is a W3 `entra.tenant_registration.change_refused` row of the administrator (`change`: update, activate or delete; `reason`: `sign_in_lockout` or `registration_disabled`), written after rollback. Field validation (400 before the unit), not-found and concurrency conflicts stay unrecorded | Gate §13.1 W3; review |
| D42 | Review: `OnTokenValidated` first runs the handler's own `ProtocolValidator.ValidateTokenResponse` (nonce against this browser's nonce cookie, required id_token claims, at_hash when present) before stripping claims, associating, recording or issuing anything. A failure throws, so the handler fails through `OnRemoteFailure`: one redirect, an `anonymous` failed row, nothing associated, recorded or issued. The handler repeats the same check after the event; it is idempotent | D22; review |
| D43 | Review: one-time link lookups are untracked. The flows read the link before their unit (to name the actor) and again inside it; with tracked lookups EF returned the first read's instance, so a link consumed or revoked by another request in between looked active and was used again. Concurrent use remains the IAM-11 finding (§8) | Review |
| D44 | Review: the forgotten-password endpoint gives the generic acknowledgement whatever happened, logging a failed, not-completed or unknown eligible request with its operation id. A no-match request only writes a best-effort W3 row, so it acknowledges even when the database refuses writes; an eligible request answering 500 then named the account. The acknowledgement never says a link was issued | D19; enumeration protection; review |
| D45 | Review: the tenant checks of sign-in, self-link and step-up read the active registration strictly. A failed read fails the attempt (the unit may retry it) and then the sign-in, link or step-up, which stays denied, with no refusal row; it is never recorded as a tenant mismatch | Gate §13.4; review |
| D46 | Review: the per-request resolver proves a pending Entra link (re-added by an administrator while the session lived) only in its own unit, as the session's user, with `identity_link.entra.proven`; refreshing a proven link's time stays bookkeeping (D29). Binding does not depend on the proof, so a proof that cannot be recorded leaves the link pending and the request resolved; without a unit nothing is proven. Whether such a session should prove the link at all is IAM-11's (§8) | D29; review |
| D47 | Review: an access-request submission reads the registration, its member and guest policy and the rejection cooldown again inside its unit; only the directory's member or guest type, an external read, comes from before it | Gate §13.4; review |
| D48 | Review: the recovery restore's result after a verified commit is built from the committed row's user id | Gate §13.5; review |
| D49 | Review: the local sign-in form tells a refusal (401: check the code or password) from a failure after the server may have accepted the code (5xx, `outcome_unknown`, no response: "a recovery code you entered may now be used up, so continue with your authenticator or a different recovery code") and clears the recovery code field. The password step no longer blames the credentials for a server failure | D4; review |
| D50 | **Authentication policy (after review):** an Entra id_token is accepted only with a signature that validates against the signing keys in the issuer's metadata, before the host links or proves an identity, attributes it as `external`, writes a W2 success row, or issues a platform, step-up or access-request session. The installed handler (10.0.12) validates the id_token it redeems from the token endpoint on a clone of its validation parameters with `RequireSignedTokens` set to false, so it checks a signature only when one is present; configuring the option does not reach that clone. `OnTokenValidated` therefore first validates the raw token-endpoint id_token with the framework's own `TokenHandler`, the handler's validation parameters and `RequireSignedTokens = true`, then the token response (D42), then everything else. Keys come from the handler's configuration manager: metadata refresh, signing-key rollover and last-known-good configuration behave as in the handler, and no key is pinned. Issuer, audience and lifetime are checked again under the same parameters. No custom cryptography. A failure goes through `OnRemoteFailure` as in D42. The option is also set explicitly for an id_token in the authorization response. Entra signs its id_tokens, so a real sign-in is not expected to change; none was performed | D22; §8; §11 |

## 4. Acceptance mapping

| Requirement | Where it is met | Evidence |
|---|---|---|
| Identity and governance W1 writers through the audited unit of work | Every W1 row of §2 is staged inside `IAuditedUnitOfWork.ExecuteAsync` | Per-family unit tests (recording double) and SQL suites `IdentityGovernanceAuditSqlServerTests`, `AccountServicesAuditSqlServerTests`, `EntraGovernanceAuditSqlServerTests`, `AuthenticationAuditSqlServerTests`, `AuthConfigurationAuditSqlServerTests` |
| W2 security events | Sign-in successes and a satisfied step-up through `IAuditEventWriter.WriteAsync`, before the session (D30) | `AuthControllerSessionIdentityTests`, `EntraStepUpCallbackTests`, `AuthenticationAuditSqlServerTests` |
| W3 attempts and refusals | Refusals decided in the transaction, written after rollback through `TryWriteAsync` | Refusal tests per family; SQL: last-admin refusal, Recovery Access refusal, self-link collision, password-change refusal |
| New events: sign-out, Entra remote failure, tenant registration, sign-in policy, enrollment | `auth.sign_out`, `auth.entra.login.failed`, `entra.tenant_registration.*`, `auth.sign_in_policy.updated`, `governance.runtime.enrollment.completed` | `ManagementAuthEndpointsTests`, `EntraStepUpCallbackTests`, `AuthConfigurationAuditSqlServerTests`, `RuntimeEnrollmentEnvironmentValidationTests`, `AccountServicesAuditSqlServerTests` |
| Actor versus target corrected | Invitations (administrator, D8), Recovery Access refusals (attempting person), MFA administration (`emergency-cli`), reset request (`anonymous`), token flows (the link's user), Entra policy (`system:entra-primary-policy`) | Actor assertions in each family's tests and SQL `Actor` columns |
| Verified external Entra triples | D1, D22 (corrected), D23, D42; `AuditActor.ValidatedExternalIdentity` | `AuditActorCallSiteTests`; SQL `ActorKind = external` with tenant and subject columns; real-handler negative paths in `EntraStepUpCallbackTests` (wrong nonce, missing nonce cookie: no association, no row, no session, for sign-in, unlinked identity, self-link and both step-up kinds). The handler runs in a TestServer against a fake token endpoint; no real Entra tenant was used |
| Recovery Access marker replaced by an event field | D3: `viaRecoveryAccess` added by the recorder | SQL: a Recovery Access session marks its rows and writes no marker row |
| Permission diffs | D9 | SQL: role diff rows hold the committed additions and removals |
| Database changes separated from mail and external attempts | D8, D13, D14, D28, D35 | SQL: rerun after a rolled-back commit mails once; lost acknowledgement mails the committed link once (invitation, contact verification, password reset); an audit failure mails nothing; failed and throwing sends. Secret store (review, real `SqlSecretStore` and `SqlSecretResolver`): commit, guard refusal, concurrency conflict, audit failure, verified lost acknowledgement and both kinds of unknown outcome, asserting which secret the registration resolves and which is retired |
| No competing recorder or retry | Only `IAuditedUnitOfWork` and `IAuditEventWriter`; no service retries | Code review; the unit owns reruns (confirmed rollback only) |
| Unknown outcomes never reported as refusal or rollback | D12; reconciliation refused only after a confirmed commit | SQL: an approval whose commit cannot be verified is `outcome_unknown`, never the refusal |
| Sign-out non-blocking | D32 | `ManagementAuthEndpointsTests`: a failed row never blocks the sign-out |
| Sensitive values excluded | Schema names refuse password, token, secret, credential and search names; producers take typed values only | `AuditDetailSchemaTests`; tests assert no plaintext link, password, code, address or provider text in rows |
| IAM-9 behaviour intact | Usernames, the ledger and the session binding are unchanged | Full unit and integration suites; SEC-1 browser suite (§7) |
| Fault injection per converted family; lost acknowledgement; red/green | §5; §7 targeted mutations M1 to M8 | SQL suites above; `artifacts`-local mutation run summarized in §7 |

## 5. Authentication results and transaction failures

- A W2 sign-in row commits before the session. When it cannot be written: local, Recovery Access and break-glass sign-in fail the request with no cookie; a Microsoft sign-in fails in the handler and redirects once to the login page with no session; a satisfied step-up issues no stronger session. A consumed recovery code stays consumed with its own committed row, and the form never invites it again (D4, D49); an unknown consumption outcome is `outcome_unknown` with no session. No code value reaches a row (SQL test) or a log (the services log none).
- A W1 change and its rows commit together. An audit insert failure rolls the change back. SQL fault injection covers every converted service family: user rename (and its retry, audited once), role permissions, Recovery Access provisioning, MFA recovery-code regeneration, invitations, contact verification requests, password reset requests, display name, runtime enrollment, provisioning approval, access request submission, self-link, the Entra-primary policy, password change, tenant registration update, the sign-in policy, the first administrator and the recovery restore.
- An uncertain commit is verified before anything reruns. A confirmed commit returns the reconstructed result; a confirmed rollback reruns the attempt (bounded); anything else is `outcome_unknown` (HTTP 500 with `code` and `operationEventId`, D12), never a refusal or a rollback. Lost acknowledgements are proven against SQL Server for role delete, Recovery Access revocation, recovery codes, invitations, contact verification, password reset, display name, setup completion, enrollment, provisioning approval, access request submission, self-link, pending-link proof, the Entra-primary policy, tenant registration update and activation, the sign-in policy, the first administrator and the recovery restore. In each, the operation ran once and reported success, never "already done", "not found" or a refusal. The password change's lost acknowledgement and unknown outcome are proven with the recording double.
- A request that changes nothing writes no row and answers as before (D7), decided inside the transaction.
- A refusal changes nothing and is recorded after the transaction released its locks.

## 6. External effects and retries

- Mail (setup invitation, contact verification, password reset, welcome) is sent at most once by the executing request, only after a confirmed or verified commit, outside the retry delegate; a rerun cannot reach it and `outcome_unknown` sends nothing. "Once" is per executing request: it is not a delivery guarantee (SMTP acceptance is not delivery) and not exactly-once across a crash after the commit or across separate requests (a resend is a new operation with a new link). The link's id and plaintext are chosen before the unit (D13), so the link held after a verified commit is the committed one. Each send is a W3 row: `succeeded`, `failed`, or `unknown` when the send threw; a failed or unknown send never reports the committed change as undone.
- Bounded recovery when a committed request's link is not in hand (D14): nothing is sent, the caller is told `account.security_mail.link_unavailable`, and sending again issues a new link and revokes the unsent one. By D13 this cannot occur after a verified commit; it stays as a guard. An unverifiable outcome sends nothing.
- The secret store runs its own transaction (D35, corrected): a supplied secret is stored as a secret of its own before the unit, so the live registration's credential changes only when the row naming the new secret commits. The replaced secret is retired after a confirmed or verified commit, the new one when nothing committed, neither after `outcome_unknown`; a deleted registration's secret is retired after the commit. Before the review, an update activated the new secret on the live one before the unit.
- Directory (Graph) reads run before the unit and are never repeated by a rerun.

## 7. Validation

Local run on 2026-10-06 against the branch with the test commit applied. Builds used an isolated artifacts path (`dotnet build … --artifacts-path <repo>/artifacts/aud12-build -m:1`); SQL Server is the local `apperture-test-sql` container.

| Check | Result |
|---|---|
| Full solution build (`Apperture.slnx`) | Succeeded, 0 errors. The warnings are in files AUD-1.2 did not change (`AuthMeResponseJsonTests`, `NotificationToastWatcherTests`, `RoleDetailHoldersUiTests`, `ManagementHostEntraLinkingHttpTests`) |
| `Apperture.UnitTests` | 3842 / 3842 passed |
| `Apperture.ArchTests` | 38 / 38 passed |
| `Apperture.Management.Ui.Shared.Tests` | 1483 / 1483 passed |
| `Apperture.Management.Ui.Client.Tests` | 511 / 511 passed |
| `Apperture.AppHost.Diagnostics.Tests` | 7 / 7 passed |
| `Apperture.IntegrationTests` (full) | 464 / 465 passed. The one failure, `ManagementHostGovernanceHttpTests.Post_sftp_test_connect_…`, needs the test SFTP server at `127.0.0.1:2222`, which was not running (environmental; unrelated to this package). In review the existing `apperture-test-sftp` container was started unchanged and the test passed (§10) |
| AUD-1.2 SQL classes (`IdentityGovernance…`, `AccountServices…`, `EntraGovernance…`, `Authentication…`, `AuthConfiguration…AuditSqlServerTests`) | 52 / 52 passed |
| The same 52 with every SQL source pointed at an unreachable endpoint | 52 failed, 0 passed, 0 skipped: they fail, never skip, without SQL Server |
| SEC-1 browser suite (`APPERTURE_BROWSER_TESTS=1`, headless Playwright) | 330 / 330 executed passed in 17 m 55 s. 5 skipped: the IAM-9.5 UI evidence tests, which need the second opt-in `APPERTURE_UI_EVIDENCE` and are not part of SEC-1 |

Targeted mutations: each was applied alone, its test project rebuilt, the named test run, and the file restored (`git checkout`); the source tree was clean afterwards.

| Id | Mutation | Caught by | Result |
|---|---|---|---|
| M1 | Invitation mail sent inside the retryable transaction | `An_invitation_rerun_after_a_rolled_back_commit_issues_one_link_and_mails_it_once` | Failed (mail sent twice) |
| M2 | Session issued before its W2 sign-in row | `When_the_sign_in_row_cannot_be_written_no_session_is_issued` | Failed (a session was issued) |
| M3 | Duplicate-identity refusal thrown inside the transaction | `Approving_an_identity_that_already_has_a_user_commits_the_reconciliation_and_refuses_only_after_a_confirmed_commit` | Failed (reconciliation rolled back) |
| M4 | Reset request recorded as the principal, not `anonymous` | `A_reset_request_is_anonymous_and_one_matching_no_account_records_only_that_it_was_suppressed` | Failed (no principal to resolve) |
| M5 | Notification preference row written when nothing changed | `Setting_a_channel_to_its_current_value_answers_the_same_and_records_nothing` | Failed. Indirectly: the double refused the extra row's unresolved principal before the "records nothing" assertion. Superseded in review: that unit test runs against a stub that decides "changed" itself, so a mutation of the real operator service cannot reach it. Re-run against the new SQL test (§10) |
| M6 | Unchanged registration update treated as a change | `A_registration_is_created_changed_activated_and_deleted_each_with_its_row_and_an_unchanged_update_writes_none` | Failed (the producer refuses an empty change list) |
| M7 | Validated Entra identity recorded as `anonymous` | `A_refused_sign_in_is_recorded_as_the_validated_external_identity_never_as_a_user` | Failed (actor mismatch) |
| M8 | Registration activation opening its own transaction inside the unit | Same registration lifecycle test | Failed (nested transaction refused) |

Not performed: the remote CI run (nothing is pushed); the SFTP-dependent integration test; the IAM-9.5 UI evidence opt-in; a sign-in against a real Entra tenant (the harness has no Entra credentials; Entra flows are proven with the OIDC handler events and test doubles).

## 8. Findings, follow-ups and owners

Resolved in review (kept here so the earlier dispositions stay traceable):

| Earlier finding | Resolution |
|---|---|
| `BootstrapPlatformAdminAssignment` created users and assigned Platform Administrator with no audit row; proposed to OPS-1.4 for lack of an action | In scope (gate §13.1, OR-1); covered (D40). OPS-1.4 keeps the first-admin mechanism and race |
| A refused or failed tenant registration update "leaves a newly supplied client secret active" (AUD-1.4) | Understated: it changed the credential the live registration resolves. Fixed (D35) |
| Lockout-guard refusals of tenant registration changes stay unrecorded (AUD-1.4) | Recorded (D41) |
| Eligible versus ineligible forgotten-password requests answered differently on a database failure, "unchanged from before" (IAM-11.3) | Not unchanged: before AUD-1.2 both failed alike. Fixed (D44). Timing and other reset-initiation behaviour stay IAM-11.3 |
| Unsigned id_token accepted from the token endpoint in the code flow (left as a decision for the security owner, proposed IAM-11) | Resolved by an explicit authentication policy: a signature from the issuer's published keys is required on every callback path (D50, §11). The review probe and the tests show that the handler accepts such a token **if the token endpoint returns one**. They do not show that anyone can make it return one: that needs control of the TLS back channel to the metadata's token endpoint, or of the metadata. No remotely exploitable bypass is claimed |

Open, with owner:

| Finding | Severity | Owner |
|---|---|---|
| **Concurrent single use is not guaranteed.** One-time account links (setup, contact verification, reset) and MFA recovery codes are consumed by read-then-update with no row lock or conditional update; the per-operation lock does not serialize two requests. Two concurrent requests presenting the same link or code can both succeed: a reset link can set two passwords, a recovery code can complete two sign-ins. This package does not make them reliably single-use. It briefly widened the link window (a tracked pre-unit read let any later use succeed); that is fixed (D43), so the window is back to truly concurrent requests. Recovery-code consumption was not widened (no pre-unit read). **Acceptance test:** two concurrent completions with the same unused link or code, against SQL Server: exactly one succeeds and commits one consumption row; the other is refused with nothing changed | Medium (security: needs the secret and a concurrent window, but defeats single use) | IAM-11 |
| **Last-administrator guards race** (F-10A, §8.1). Removing Platform Administrator, removing the last viable link and disabling the credential read an unlocked count; two concurrent operations can both commit and leave no Platform Administrator, or none who can sign in. Not introduced by AUD-1.2; the earlier claim that reading inside the transaction closes it is withdrawn | Medium | IAM-10. Open: an owner is not a disposition |
| The Entra-primary policy's "last Entra-independent administrator" count has the same race (F-10B, §8.1) | Low to Medium | IAM-10. Open |
| Two concurrent first-administrator requests can both read zero users | Low | OPS-1.4 (takeover window and race) |
| **A live session proves a re-added identity link** (F-11A, §8.1): a session that outlived its link's removal proves the link an administrator re-adds, on its next request, without a fresh sign-in (now with its row, D46) | Low | IAM-11 (session revalidation). Open |
| Controllers that catch `InvalidOperationException` for business errors (`UsersController` at six sites, the batch provisioning endpoint) also catch the unit's own invariant failures and answer 400 or 404 with internal text; the host maps any `ArgumentException` to 400. Nothing committed in these cases (they precede commit), but a server fault reads as a client refusal. Pre-existing pattern | Low | Proposed AUD-1.6 (enforcement) |
| Approving an access request whose user or request can no longer be read after a verified commit throws instead of reporting success (`UserProvisioningService` reconstruct). The directory path is fixed in review | Low | AUD-1.3 |
| First-administrator refusals for a duplicate identity (including reserved or Entra requests) and a missing role stay unrecorded (D38 corrected) | Low | OPS-1.4 |
| A refusal check runs before the "already in that state" check in role removal and credential disable, so a no-op request for the last administrator is refused and recorded instead of being a silent no-op (pre-existing order) | Low | IAM-10 |
| A role whose stored permissions cannot be parsed records "all added, none removed" on its next change (`SqlRoleRepository` treats malformed JSON as empty) | Low | IAM-10 |
| After a committed preference change, the account endpoints build their response with `.First()` over at most 200 enabled definitions; an edge case answers a generic 500 for a committed change | Low | AUD-1.3 |
| Contact verification stamps `DateTimeOffset.UtcNow` instead of the injected clock (pre-existing) | Low | AUD-1.3 housekeeping |
| Platform and integration setting secrets use the same "activate a new version on the live secret before the change" pattern the review removed for Entra (`PlatformSettingsService`, `IntegrationSettingsService`) | Medium | AUD-1.4 (secret store family) |
| An unknown outcome of the self-link or proof unit reaches `OnRemoteFailure` and is recorded as `anonymous` `auth.entra.login.failed` although the token validated; the user can retry (a no-op) | Low | IAM-11.4 (sign-in failure records) |
| Rows of `emergency.local.protected` and `credential.local.disable_deferred_last_entra_independent_admin` already classified before D6 keep their earlier basis | Low | AUD-1.5 (query and read model) |
| `EmergencyAccessService` keeps unused `_time` and `_logger` fields | Low | AUD-1.3 housekeeping |

### 8.1 Findings tracked to IAM-10 and IAM-11

Each stays open until its acceptance criteria are met by shipped and verified code, or the security and product owners record a disposition. Naming an owner is neither. None was introduced by this branch, so none blocks the AUD-1.2 merge. The [RC1 program gate](../../rc1-program-gate.md) §8 register lists each one with its owner.

**F-10A. Last-administrator guards race (IAM-10, Medium).**
- *Where.* `UserManagementService` decides three refusals from `IRoleRepository.CountUserAssignmentsAsync` for Platform Administrator: removing an identity link (line 731), removing the role (line 837) and disabling the local credential (line 946). The count is `SqlRoleRepository.CountUserAssignmentsAsync` (`SqlRoleRepository.cs:55`), a plain `COUNT` with no lock hint, read inside the operation's unit at the default isolation. The role delete path in the same repository already reads with `UPDLOCK, HOLDLOCK` (lines 182 and 198).
- *Security impact.* Two requests that each pass alone can both commit. Removing Platform Administrator from each of the last two holders leaves none. Removing one holder's role while the other holder's last viable sign-in method is removed or disabled leaves one administrator who cannot sign in. Either way, ordinary administration of the instance is lost until Recovery Access or the configured-administrator path restores it. This needs two privileged requests inside the same short window, from two administrators or one administrator sending two at once. An unauthenticated caller cannot reach it. Each committed change still has its own row, so the audit trail records what happened.
- *Origin.* Before AUD-1.2 these checks ran with no transaction at all. The units added here read the count inside the transaction but do not lock it.
- *Evidence limits.* Established by reading the source and the query the count issues. Not reproduced with concurrent requests against SQL Server. RC1 audit N-07 (the guard skipped for Entra link removal, IAM-11) is a different defect: a missing guard, not this race.
- *Acceptance.* (1) Against SQL Server, for each pair of the three operations (and two of the same kind on different users), two concurrent requests that together would leave no Platform Administrator with a viable sign-in path: exactly one commits, and the other is refused with its `auth_admin.blocked.*` row and nothing changed. (2) The guard read takes a lock that serializes these operations: a locking read of the Platform Administrator assignments, as the role delete does, or one application lock shared by all three. The test fails with that lock removed. (3) Non-concurrent behaviour, refusal codes and rows are unchanged.

**F-10B. Entra-primary "last Entra-independent administrator" count race (IAM-10, Low to Medium).**
- *Where.* `EntraPrimaryAuthenticationPolicy` disables a Platform Administrator's local credential after their Entra link is proven, unless Recovery Access is unhealthy and `CountOtherEntraIndependentPlatformAdministratorsAsync` (line 178, used at lines 86 and 147) finds no other administrator with an enabled local credential. The count reads assignments, users and credentials with no lock.
- *Security impact.* When Recovery Access is not healthy, the last two Entra-independent administrators who prove their links at the same moment can each count the other and both lose their local credential. Administration then depends entirely on Entra being available and correctly configured. The protection that the policy exists to keep is lost without any refusal or deferral row.
- *Evidence limits.* From the source only. Not reproduced concurrently.
- *Acceptance.* Against SQL Server, two concurrent applications for the last two Entra-independent administrators while Recovery Access is unhealthy: exactly one credential is disabled, and the other user records `credential.local.disable_deferred_last_entra_independent_admin`. The decision is serialized, by the F-10A lock or its own, and the test fails without it.

**F-11A. A live session proves a re-added identity link (IAM-11, Low).**
- *Where.* `SqlPrincipalToUserResolver.ResolveUserIdAsync` (lines 64 to 75). A host-issued Entra session carries the tenant and object id it was issued through, and the resolver requires that exact link to still belong to the session's user on every request. When an administrator removes the link, the session stops resolving, but its cookie is not revoked. If the administrator later re-adds the same tenant and object id to the same user, the link is pending. On the old session's next request, the resolver proves it (`TryProvePendingAsync`, in its own unit with `identity_link.entra.proven` as that user, D46) and then applies the Entra-primary policy, which can disable the user's local credential.
- *Security impact.* A link proof is meant to show that the person controls the directory identity now. Here, a cookie issued before the removal stands in for that proof, with no fresh Entra sign-in. An administrator who removed a link to cut off a session, and re-adds it expecting a new sign-in, instead brings the earlier session back and gets a recorded proof. Exploiting this needs a still-valid session cookie for that user issued through that identity, plus an administrator re-adding the identical link to the same user. A link re-added to a different user is not proven: the session's user id does not match.
- *Origin.* Pre-existing ("proven by its first authenticated use, as before"). This package added the unit and its row, not the behaviour.
- *Evidence limits.* `SqlPrincipalToUserResolverTests.Pending_entra_link_is_proven_with_its_row_as_the_sessions_user_then_the_policy_applies` shows the resolver proving a pending link from a session, with a mocked repository. The full sequence (sign in, remove, re-add, reuse the cookie over HTTP against SQL Server) was not run.
- *Acceptance.* IAM-11's session-revalidation design rules whether a session may prove a link. If it may not: (1) through the host against SQL Server, a session issued through a link that is then removed and re-added stays unresolved; the link stays pending, with no `identity_link.entra.proven` row and no Entra-primary policy change, until a fresh Entra sign-in or self-link proves it. (2) That fresh sign-in proves it exactly as today. (3) Sessions issued after the re-add are unaffected. If IAM-11 decides a session may prove a link, the security owner records that decision and its basis instead.

## 9. Scope remaining for AUD-1.3 and later

Package scope is the design gate's (§22); this lists what AUD-1.2 leaves to each.

- **AUD-1.3, operator action attribution:** the Part 1 AUD-1.3 actions as W1 with their request rows; replacing the `system` and `unknown` actors; real outcomes in `RecoveryController`; actor columns on request rows where missing; the `EmergencyAccessService` housekeeping above.
- **AUD-1.4, configuration lifecycle:** the remaining configuration writers, both deletes with verification and the lost-acknowledgement delete case; Part 1 AUD-1.4 coverage; secret-store events through the recorder (the Entra tenant secret's consistency and the lockout refusals were settled here in review; the platform and integration setting secrets' live-version pattern remains, §8); the runtime-shutdown duplicate case. Team events stay in LST-1.4.
- **AUD-1.5, query contract (server):** cursor, §16 parameters and DTO; the first `IUserReferenceResolver` consumer; prospective transition-only blocked writing; module discovery as routine; the dashboard count; the denial actor; the basis of rows classified before D6.
- **AUD-1.5b, timeline UI:** §17 and the AUD-1 rows of §18. Depends on AUD-1.5, LST-1.1 and AUD-1.3.
- **AUD-1.6, enforcement and T-B:** M2 to M5 (including the source scan that no W1 or W2 action reaches `AppendAsync`; the sign-in failure rows still use the bridge as W3), the T-B privileged startup connection, and the retention document.
- IAM-10 (authorization and the `role.manage` finding; the last-administrator guard races F-10A and F-10B, §8.1), IAM-11 (sign-in failure rows, session, throttling, lockout, concurrent single-use consumption, reset initiation; session proof of a re-added link F-11A, §8.1), OPS-1 (first-administrator window and race) and RCV-1 keep their findings. IAM-9 stays closed. RC1 is incomplete.

## 10. Independent review (2026-10-07)

Reviewed at `636481a` against the gate (revision 3) and this record. Source and tests were read directly; the handback was treated as evidence to check. Three read-only reviewers covered the governance, account-service and Entra families; every finding below was re-verified in source before it was acted on.

### 10.1 Findings and resolutions

| # | Finding | Severity | Source | Resolution |
|---|---|---|---|---|
| R1 | The OIDC handler validates the token response (nonce, required claims, at_hash) only after `OnTokenValidated`, and never when the event handles the response. Association, link and proof commits, external attribution and the W2 row ran before the nonce check; a local step-up and the access-request session were issued with no nonce check at all (pre-existing for the sessions; D22 claimed otherwise) | High | `EntraOpenIdConnectOptionsConfigurer.cs` `OnTokenValidatedAsync` | `7ea6137` (D42); real-handler tests red before, green after |
| R2 | A supplied tenant secret was activated as a new version of the live secret before the unit: a refusal, conflict, rollback or unknown outcome still changed the credential the registration resolves | High | `ConfigEntraTenantsController.Patch`, `EntraClientSecretMaterialProvisioner.ReplaceAsync` | `cd6d890` (D35 corrected) |
| R3 | Tenant-registration guard refusals unrecorded | Medium | `ConfigEntraTenantsController` | `cd6d890` (D41) |
| R4 | Configured administrators applied at host start unrecorded | Medium | `BootstrapPlatformAdminAssignment` | `0041de1` (D40) |
| R5 | A tracked pre-unit read let a one-time link consumed or revoked in between be used again (widened IAM-11) | Medium | `SqlAccountActionTokenRepository` | `6c3f06f` (D43); SQL test red before |
| R6 | New forgotten-password enumeration signal on database write failures | Medium | `ForgottenPasswordService`, `AuthController.ForgotPassword` | `c7d7b1f` (D44) |
| R7 | The sign-in form showed a server failure after recovery-code consumption, or an unknown outcome, as "Verification failed. Check the … recovery code", inviting the used code | Medium | `LocalLoginFormSection.razor` | `ca6b2cb`, `4526524` (D4, D49) |
| R8 | A failed registration read was recorded as a W3 tenant mismatch (and skipped the unit's retry) | Medium | `EntraIdentityAssociationService`, `EntraStepUpService` | `f633602` (D45) |
| R9 | The per-request resolver proved a pending link with no row (a missed W1 writer) | Medium | `SqlPrincipalToUserResolver` | `0182fbf` (D46) |
| R10 | Access-request submission checked registration, policy and cooldown only before its unit | Low to Medium | `EntraAccessRequestService.SubmitAsync` | `7d36ebf` (D47); unit test red before |
| R11 | A verified recovery restore could report "target not found" | Low | `RealBootstrapRecoveryService` | `4e97428` (D48); SQL test red before |
| R12 | One person's unknown or not-completed outcome turned a provisioning batch into 500 "Nothing was changed" after earlier people committed; a verified directory provisioning whose user could not be read became "failed" | Low to Medium | `UsersEntraProvisioningController`, `UserProvisioningService` | `e06adad`: per-person `outcome_unknown` and `operation_not_completed` rows; reconstruct from the committed row |
| R13 | A throwing security-mail send was no longer logged | Low | `AccountSecurityMailDelivery` | `5996f70` |
| R14 | An unknown policy outcome was logged as "could not be applied" | Low | `EntraIdentityAssociationService`, resolver | `f633602`, `0182fbf` |
| R15 | The M5 mutation could not reach its assertion (stubbed service) | Test gap | `AccountProfileServiceTests` | `032f2ab`: SQL test through the real service |
| R16 | Last-administrator guard races; unsigned id_token acceptance; concurrent single use; and the other open items | See §8 | | Recorded with owners in §8. Unsigned id_token acceptance later resolved by policy (D50, §11); the races and the re-added link are tracked in §8.1 |

### 10.2 Scope dispositions

- **Configured administrators (`BootstrapPlatformAdminAssignment`):** in AUD-1.2 scope. The gate classifies user and role-assignment administration and the bootstrap first administrator as W1 (§13.1) and converts every required mutation writer (OR-1). No authoritative contract assigns this coverage elsewhere: the program plan's OPS-1.4 owns the first-admin *mechanism* (a setup secret or CLI verb), and the 2026-03 decision note only allowed that "a follow-up may add durable audit rows". A missing action constant was an implementation gap. Covered (D40); the first-admin takeover window and its race stay OPS-1.4.
- **Tenant-registration refusals:** in scope (gate §13.1 W3 "refusals … validation rejections"; §22 "tenant registration" coverage). Recorded after rollback (D41). No contract assigned them to AUD-1.4.

### 10.3 Tenant client secret: precise behaviour

- **Before review.** `Patch` called `ReplaceAsync` before the unit. With a usable existing ref it called `AddVersionAsync` then `ActivateVersionAsync` on **the same secret id the committed registration names**, so the registration's ref resolved to the new value at once (`SqlSecretStore.TryGetPayloadAsync` reads `ActiveVersion`). The stored ref takes precedence over the deployment secret (`EntraClientSecretAccess`), for sign-in options (`EntraAuthOptionsDatabasePostConfigure`), directory discovery and access requests. A lockout refusal (400), a row-version conflict (409), an audit failure, a confirmed rollback or `outcome_unknown` therefore all changed the live credential while the response said the change was refused or failed. The earlier version cannot be reactivated through the store (`ActivateVersionAsync` requires a pending version). With no usable ref, a new secret was created and left unused on failure. Mutation M-A (the pre-review behaviour) fails three SQL tests: the first secret no longer resolves.
- **After review (D35).** A supplied secret becomes a secret of its own; the registration resolves its first secret until the row naming the new one commits. Proven against SQL Server with the real store and resolver: commit (new resolves, old retired); guard refusal, concurrency conflict and audit failure (old still resolves, new retired); verified lost acknowledgement (new resolves, old retired, one row); unknown outcome with the change committed and with it rolled back (both secrets still active; the live one resolves). Mutations M-B (retire on unknown) and M-C (never retire the replaced secret) are caught.
- **Not changed.** Secret-store events still go to `SecretAudits` only (AUD-1.4); retirement after a commit is best effort and logged.

### 10.4 MFA and authentication results

- A recovery code is consumed in its own unit with its W1 row as the user being authenticated; if the W2 sign-in row then fails, no session is issued, the consumption and its row stay committed, and the next attempt with that code is refused (SQL, through the controller). An unknown consumption outcome propagates as `outcome_unknown` (500 with code and operation id), never a refusal or rollback, with no session (SQL). No code value appears in any row (SQL scan) or log (the services log none).
- The form no longer invites the consumed code (D49).
- **Single use is not guaranteed under concurrency** (§8, IAM-11). This package did not introduce that for recovery codes; it widened it for one-time links and the review restored the earlier window.

### 10.5 OIDC validation boundaries

Established against the installed handler (10.0.12) by driving it in a TestServer with a fake token endpoint, not by invoking events by hand: see D22 (corrected) and D42. Every check that the handler performs before or after the event has completed before an identity is attributed as `external`, a W2 success row is written, or a session is issued. At review the handler required a signature only when one was present (§8). Since D50 the event requires one first (§11). Well-formed GUIDs are not treated as verification. A validated identity refused admission is still `external`; a response that fails validation is `anonymous`.

### 10.6 Core review conclusions

- Transaction membership: every converted unit reads and validates inside its transaction except R10 (fixed); the last-administrator counts are inside but unlocked (§8).
- Primary action selection: chosen before the unit from input or declared as alternates; reconstruct handles each primary; secondary rows cannot become primary.
- No-op and refusal after uncertain commits: no path reports one without a confirmed rollback, except the reconstruct reads fixed in R11 and R12 and the approval read left in §8.
- Permission diffs: computed from the tracked row under its row version in the transaction (one malformed-JSON edge case in §8).
- Explicit actors: every `AuthenticatedUser` and `ValidatedExternalIdentity` call site comes from verified credentials, a one-time link checked by its flow, the handler's protected state, a validated token (now including the token-response checks), or the host-issued session (resolver, new and pinned).
- Token preparation and mail: as D13, D14 and §6 (qualified "once").
- Exception mapping: the host maps `AuditOutcomeUnknownException` and `AuditOperationNotCompletedException` to 500 with code and operation id and nothing upstream converts them, except the batch endpoint (fixed, R12). Programming failures can still read as 400 or 404 through pre-existing catches (§8).

### 10.7 Validation in review

Builds: `--artifacts-path artifacts/aud12`, `-m:1`. SQL Server: the local `apperture-test-sql` container. The SFTP server: the existing `apperture-test-sftp` container, started unchanged (stopped since its last run; no new configuration or data).

| Check | Result |
|---|---|
| Full solution build (`Apperture.slnx`) | Succeeded, 0 errors; no warning in any file this branch changed |
| `Apperture.UnitTests` (full) | 3862 / 3862 passed (before the last fix; its affected classes, 22 tests, passed after it) |
| `Apperture.ArchTests` | 38 / 38 passed |
| `Apperture.Management.Ui.Shared.Tests` | 1489 / 1489 passed (one source-guard failure on the first run, fixed by `4526524`) |
| `Apperture.Management.Ui.Client.Tests` | 511 / 511 passed |
| `Apperture.IntegrationTests` (full, SQL Server and SFTP container up) | 478 / 478 passed in 19 m 41 s, including `ManagementHostGovernanceHttpTests.Post_sftp_test_connect_…`; run before the last fix, whose `EntraGovernanceAuditSqlServerTests` (12) passed after it |
| `EntraStepUpCallbackTests`, `EntraStepUpSecurityGuardTests`, `ManagementAuthEndpointsTests` (real handler) | 112 / 112 passed after D42, including the 10 new negative paths |
| SEC-1 browser classes that drive Entra sign-in (`OidcFlowBaselineTests`, `StepUpCallbackBaselineTests`, `EntraReturnTargetBrowserTests`, `HarnessIsolationGuardTests`; headless Playwright against the stub provider, which echoes the nonce) | 61 / 61 passed in 5 m 11 s, after the last commit |
| SEC-1 `SessionRecoveryBrowserTests` (drives the changed local sign-in form, including its 401 wording) | 7 / 7 passed |
| Rest of the SEC-1 browser suite | Not rerun: the other review changes (tenant secrets, configured administrators, account services, resolver, provisioning) have no browser-facing path those classes exercise; they are covered by the unit and integration runs above. PR CI runs the whole suite |

Targeted mutations in review (each applied alone, the named tests rebuilt and run, the file restored with `git checkout`):

| Id | Mutation | Caught by | Result |
|---|---|---|---|
| M-A | A supplied secret becomes a new active version of the registration's live secret (the pre-review behaviour) | Refused, conflicting and audit-failing update SQL tests | 3 failed: the first secret no longer resolves |
| M-B | Secrets retired after an unknown outcome too | `An_update_whose_outcome_is_unknown_retires_neither_secret` (both cases) | 2 failed |
| M-C | The replaced secret never retired | Commit and lost-acknowledgement SQL tests | 2 failed |
| M5 (re-run) | The operator service writes an equal preference row and reports a change | `Setting_an_email_preference_to_its_current_value_writes_neither_the_preference_nor_a_row` | Failed directly on "nothing is recorded" (2 rows, 1 expected) |

Red before green (the test written first, run against the unfixed code): the ten OIDC nonce tests (all failed: association called, or a session issued for the local step-up), the one-time link reuse test, the access-request recheck test and the recovery-restore test.

Evidence classes:
- **SQL Server:** the `*AuditSqlServerTests` classes and the SFTP and HTTP integration tests ran against real SQL Server; recording doubles prove which rows a service decides to record, not transactions.
- **Handler, not Entra:** the OIDC tests drive the real handler with a fake authority and token endpoint; no real Entra tenant was used.
- **Local, not CI:** everything here ran locally on Windows; nothing is pushed, so PR CI has not run.

Not performed: a sign-in against a real Entra tenant; the remote CI run; the IAM-9.5 UI evidence opt-in.

## 11. Signed id_token policy (after review, 2026-10-07)

### 11.1 Policy and handler path

The policy is D50. The handler path, read from the `v10.0.12` source of `OpenIdConnectHandler.HandleRemoteAuthenticateAsync` and confirmed by the tests below:

1. The handler checks the protected state and correlation, redeems the code, and runs `OnTokenResponseReceived`.
2. It sets `validationParameters.RequireSignedTokens = false` on its own clone of `TokenValidationParameters` ("no need to validate signature when token is received using code flow"), then validates the token-endpoint id_token through `ValidateTokenUsingHandlerAsync`. That validates a signature that is present, against keys from the configuration manager, and accepts a token with none (an empty signature segment, whatever `alg` the header names).
3. It reads the nonce cookie and raises `OnTokenValidated`.
4. If the event did not end the response, the handler runs `ProtocolValidator.ValidateTokenResponse` and completes.

Setting `RequireSignedTokens` on the options cannot change step 2. The host therefore enforces the policy as the first act of the event (step 3), on every path: the normal sign-in, self-link and Entra step-up complete through step 4; the access-request session and the local step-up end the response in the event. A signed token with a bad signature or an untrusted key never reaches the event, because step 2 rejects it.

With a real configuration manager, a key the handler has not seen fails that sign-in in step 2 (`SecurityTokenSignatureKeyNotFoundException`) and causes a metadata refresh. In the test, the key set had been read again but that sign-in still failed. The next sign-in with the new key succeeded without a restart. Last-known-good configuration is unchanged: a key that the issuer has just retired can still validate for that configuration's lifetime, in the handler and in the host's check alike. That is the framework's rollover behaviour, deliberately kept.

### 11.2 Tests (real handler, `EntraStepUpCallbackTests`)

| Test | Proves |
|---|---|
| `An_id_token_without_a_signature_from_the_issuers_published_keys_is_rejected_on_every_path` (25 cases) | Unsigned (`alg: none`), signature stripped from an RS256 token, an untrusted key under the published key id, an untrusted key under an unknown key id, and an altered signature, each on all five paths: no association, link or proof, no `UpdateLastAuthenticatedAt`, no session or access-request cookie, no row other than an `anonymous` failure, the failure names the signature |
| `An_id_token_signed_with_a_key_from_the_issuers_metadata_completes_every_path` (5) | A token signed with the configured metadata's key completes each path, with its success row or access-request cookie |
| `The_handlers_own_token_response_validation_still_runs_after_the_early_check_with_the_same_nonce` (5) | The D42 early check does not break the handler's own later validation: the token response is validated twice on the completion paths and once on the early-response paths, each time with this browser's challenge nonce |
| `During_a_signing_key_rollover_a_token_signed_with_either_published_key_is_accepted` | Both published keys are trusted (real `ConfigurationManager`) |
| `After_the_issuer_rolls_its_signing_key_the_new_key_is_trusted_from_refreshed_metadata` | The rolled key is trusted after a metadata refresh, with no restart and nothing pinned |
| The D42 nonce tests (10) | Unchanged and green. `An_unlinked_identity_whose_nonce_fails_gets_no_access_request_session` was corrected: its `AccountNotLinked` setup ran before the rig's own setup and was overridden, so it exercised normal sign-in, not the access-request path |

Red before green: against the unfixed configurer (the policy commit reverted locally, tests kept), the 10 unsigned cases failed on all five paths, so the handler had accepted an unsigned token-endpoint id_token everywhere. The 15 signed-but-untrusted cases passed, because step 2 already rejects them. Everything else passed.

### 11.3 Validation

Commits: `6eb0959` (policy), `0ead747` (tests). The results below ran on `0ead747`. This section's documentation commit changes no code. Builds: `--artifacts-path artifacts/aud12`, `-m:1`. SQL Server: the local `apperture-test-sql` container.

| Check | Result at `0ead747` |
|---|---|
| Full solution build (`Apperture.slnx`) | Succeeded, 0 errors; no warning in either changed file |
| `Apperture.UnitTests` (full) | 3900 / 3900 passed |
| Real-handler classes `EntraStepUpCallbackTests`, `EntraStepUpSecurityGuardTests`, `ManagementAuthEndpointsTests` | 149 / 149 passed (112 before, plus 37 new) |
| `Apperture.ArchTests` | 38 / 38 passed |
| Integration classes that touch the OIDC configuration and Entra governance (`AuthConfigurationAuditSqlServerTests`, `BrowserRequestTrustHttpTests`, `EntraGovernanceAuditSqlServerTests`) | 46 / 46 passed |

Not rerun locally at this head: the full `Apperture.IntegrationTests` run (478 / 478 in §10.7 ran on the review commits, not on `0ead747`), the UI test projects (no UI change), and the SEC-1 browser suite. The pull request's CI on the final head is the complete build-and-test and browser-security checkpoint. Its browser stub signs RS256 id_tokens with a key it publishes, so the policy applies to it unchanged. No sign-in against a real Entra tenant was performed.
