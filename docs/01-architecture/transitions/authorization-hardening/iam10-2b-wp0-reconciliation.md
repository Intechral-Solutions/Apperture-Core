# IAM-10.2b WP0: design-to-code reconciliation

| | |
|---|---|
| Package | IAM-10.2b, work package 0: the reconciliation of the [IAM-10 design gate](iam10-design-gate.md) §3.2 (P-8 to P-11, Amendment A, rulings C-13 and C-14) and acceptance A10.2b.1 to A10.2b.17 with the code at `master` `6175874`. |
| Branch | `feat/iam-10.2b-administrator-viability`, from `origin/master` at `6175874` (the merge of IAM-10.2a, PR #36). Not pushed, not merged. |
| Status | The owner ratified the rulings D-1 to D-6 below on 2026-10-19. Nothing here reopens a decision of the gate. Where this record and the gate differ, this record says so under "Departures and corrections". |
| Not in scope | IAM-11 (user lifecycle, credential recovery), IAM-10.3 (declarative step-up), IAM-10.4 (role editor), IAM-10.5 (legacy break-glass removal), context-scoped authorization. |

## 1. Rulings

| # | Ruling |
|---|---|
| **D-1** | **Strict Recovery Access revocation (C-13-R).** Revoking Recovery Access requires at least one viable Entra-independent ordinary Platform Administrator **other than the account being revoked**. This clarifies C-13; it does not change the general I-FALLBACK definition. The former Recovery Access account keeps its ordinary identity (role, credential, MFA enrolment, the `breakglass` username and "Recovery Access" display name, as [IAM-9](../identity-foundation/iam9-design-gate.md) already records for a revoke) and counts under the general invariants after the revoke. The typed refusal, the audited W3 event and the command-line behaviour are those of C-13. |
| **D-2** | **Recovery Access designation.** Both I-VIABLE and I-FALLBACK are applied to the complete state before and after the designation, including the previous designated account: the designee leaves VOA and EI, and the account that is demoted enters them. |
| **D-3** | **Entra tenant binding.** A proven Entra link counts as a sign-in method only when its tenant equals the tenant the authentication accepts, by the sign-in's own comparison (`EntraTokenClaims.TenantMatches`) and its own tenant rule (`EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId`). |
| **D-4** | **Two views.** Every invariant is applied independently to a **Now** view (the authentication the running host accepts, including the live tenant check) and a **Restart** view (the authentication the persisted configuration yields after a restart). Production secret resolution and validation rules are used. The direct Local login route is available whenever Local sign-in is enabled, whatever the chooser flags say. A persisted change is never assumed to reach the running middleware. |
| **D-5** | **Writer coverage.** The writer inventory (§7) and the explicit global-gate coverage of A10.2b.14 stand. Only two narrow departures are approved (§6). Neither extends to the Entra-primary policy: its credential disable takes the gate. |
| **D-6** | **Command-line configuration.** The evaluation reads the deployment's authentication configuration through one interface. The emergency command line uses configuration that corresponds to its target installation; unknown, missing or untrustworthy configuration fails closed for viability-reducing operations with an actionable message, and never blocks an operation that can only restore viability or recovery-code regeneration. |

## 2. The global gate

`IPlatformAdministratorSetGate` (`Apperture.Core.Application.AdministratorViability`), implemented by `SqlPlatformAdministratorSetGate`.

| | |
|---|---|
| Mechanism | `sp_getapplock`, resource `apperture:platform-administrator-set`, `@LockMode = 'Exclusive'`, `@LockOwner = 'Transaction'`, a bounded wait of 15 seconds (the ownership gate's value). Released by the owning transaction's commit or rollback. Database-scoped, so it serializes across processes (the emergency command line is one). |
| Preconditions | An ambient transaction on the writer's context. Without one the gate throws `InvalidOperationException` (a programming error): a transaction-owned lock outside a transaction guarantees nothing. |
| Return codes | `0` granted. `1` granted after waiting. `-1` timeout, `-2` cancelled, `-3` deadlock victim: `PlatformAdministratorSetBusyException` (code `governance_operation_busy`, mapped to HTTP 409 with `retry`, a transient contention outcome distinct from a business refusal and from audit-infrastructure failure). `-999`, any other value or no value: `PlatformAdministratorSetGateException` (`administrator_set_gate_failed`), an internal failure, not a retry. Client cancellation surfaces as `OperationCanceledException`. A row-lock deadlock (error 1205) is **not** this gate's concern and is rerun by the unit's execution strategy. |
| Order | The audited unit's operation lock, **then this gate**, then the authority row locks of P-5, then every other row. A writer that holds only row locks never waits for the gate, and no writer requests the gate after a row lock, so no cycle forms. It is never taken together with the resource ownership gate. |
| Read discipline | Every invariant read (the state before, the state after, the recovery posture, the effective configuration) is issued after the gate is held. The gate is acquired again on every attempt of a unit, because a rerun is a new transaction. No transaction-level SNAPSHOT isolation is used in these units. |
| Procedure | Under the gate: read the state before; apply the mutation through the repositories (not staged); read the state after; if an invariant is newly violated, refuse through the unit's refusable path (rollback, then the operation's refusal row) or, for a system-driven mutation, return its existing deferred outcome. The "already in that state" check runs before the invariants. A refusal is decided before anything is staged, because `DeclareNoOp` refuses once an audit row is staged. |
| Registration | The Management Host (`AddAppertureGovernance`) and the emergency command line (`AddAppertureEmergencyOperatorServices`). |

## 3. The two views (D-3, D-4)

| Input | Now | Restart |
|---|---|---|
| Local sign-in enabled | `LocalAuth:Enabled` in the options the host's login routes read (`IOptions<LocalAuthOptions>`) | `LocalAuth:Enabled` in the deployment configuration |
| Entra options | The options the sign-in, link and step-up endpoints gate on (`IOptions<EntraAuthOptions>`) **and** the options the OpenID Connect handler was built from (`IOptionsMonitor<EntraAuthOptions>`). Usable only if both are. | The deployment's `Entra` section with the persisted active registration overlaid by `EntraAuthOptionsOverlay` (the rule the host applies when it builds its options) |
| "Usable" | The merged sign-in snapshot's `EntraSignInEnabled` (enabled and valid) | the same |
| Secret | As resolved when the host built its options | `EntraClientSecretAccess` (the registration's stored secret, then the deployment secret) |
| Accepted tenant | The live active registration's tenant (`ResolveAcceptedTokenTenantId`), because the sign-in reads the database on every attempt, unless the running handler is bound to a different directory, in which case no Entra token is accepted | The persisted active registration's tenant |
| Chooser and policy flags | Ignored | Ignored |

A **viable ordinary administrator (VOA)** holds `role-platform-administrator`, is not the designated Recovery Access user, and has a usable sign-in method: an enabled Local credential (with a Local link) while Local sign-in is enabled, or a proven Entra link whose tenant is the accepted tenant while Entra is usable. An **Entra-independent administrator (EI)** is a VOA whose Local method is usable. Recovery Access is never counted in either. `RecoveryHealthy` is the existing posture exactly.

**I-VIABLE** is refused iff `VOA(pre) ≥ 1` and `VOA(post) = 0`. **I-FALLBACK** is satisfied when `RecoveryHealthy` or `EI ≥ 1`, and is refused iff it was satisfied before and is not after. Each is evaluated in each view; a violation in either view refuses. A state that already violates an invariant is never a reason to refuse an unrelated or repairing change.

The post-state is read from the database after the mutation is applied in the writer's transaction, so the Restart view is the *proposed* persisted configuration and no simulation of a change is needed. This replaces the hand-built configuration simulation of the registration controller, which read only the deployment secret and so disagreed with the runtime about a registration that holds its own secret.

## 4. Strict revocation (C-13-R)

`emergency revoke --confirm-fallback` takes the gate, clears the designation of the designated account X, and then requires, in each view, an EI administrator who is not X. It is refused whatever the state before, whatever `RecoveryHealthy` was, and whether or not `--confirm-fallback` is given. X is excluded because revoking only clears the flag: X keeps its role and enabled credential and would otherwise count as the fallback the rule requires. The refusal is audited (`emergency.auth.blocked.unsafe_operation`, operation `recovery_access.revoke`), carries the code `entra_independent_fallback_required` (409) and makes the command exit 1 with an actionable message. After a revoke, X is an ordinary administrator and counts under the general invariants (D-1).

## 5. Command-line configuration (D-6)

`IDeploymentAuthenticationConfiguration` returns the deployment's Local setting, its Entra options before any registration is overlaid, whether the reader can see the host's secrets, and, in the host only, the running options.

| Reader | Behaviour |
|---|---|
| Management Host (`HostDeploymentAuthenticationConfiguration`) | Authoritative. Reads its own configuration and its running options. If the running options fail validation the Entra reading is untrusted. |
| Emergency command line (`ConfiguredDeploymentAuthenticationConfiguration`) | Reads the configuration the command line was started with. `LocalAuth:Enabled` missing is **unknown**; present and not a boolean is **untrusted**. It cannot observe a running host, so its Now view is the Restart view, marked as an estimate. It can never claim that a client secret is absent (it cannot see the deployment secret or the secret store): an active registration whose secret cannot be established makes the Entra reading **unknown**. |

`AdministratorViabilityGuard.Evaluate` returns `ConfigurationUnavailable` when the Local reading (or, for I-VIABLE, the Entra reading) is not trusted, before it considers the states. The strict revocation needs only the Local reading. A raise-only writer asserts with `Detect`, which never fails closed. Recovery-code regeneration and any operation that can only restore viability do not call the fail-closed path.

## 6. Approved departures from the literal text of A10.2b.14

A10.2b.14 requires every writer in its categories to take the gate and call the guard. Two narrow departures are approved. Each is enumerated by file and call count in the writer guard, and each is mechanically checked.

| | Scope | Why it is safe | How it is checked |
|---|---|---|---|
| **E-1** | `DevTestUserBootstrapHostedService`: development-only bootstrap. | It runs only under `IsDevelopment()` and `DevTestUser:Enabled`, and only adds (a user, the role, a password). It has no audited unit and no transaction, so it cannot take a transaction-owned gate. | The environment check precedes the first write; the file contains no removal, disable or delete; the write counts are pinned. |
| **E-2** | The Entra link addition and the pending-link proof of sign-in: `EntraIdentityAssociationService` (one link addition, four proofs) and the three `LastAuthenticatedAt` writes of `SqlPrincipalToUserResolver` (the refresh of a proven Entra link, the proof of a pending one, and the refresh of the Local link, which no viability input reads). | Adding or proving an Entra link can only add a sign-in method. Taking the global administrator lock on every Entra sign-in would make an administrator's change delay, and a busy result deny, a user's sign-in. | The two files call no link removal, credential disable or role removal; the repository's proof writes set `LastAuthenticatedAt` to a time and never to null; the call counts are pinned. |

**Not covered by E-2: the Entra-primary policy.** It runs after the proof and disables a Local credential; it takes the gate independently (WP4A). How its contention and failure are surfaced is in §9.

## 7. Final writer inventory

Classes: **Reducer** (takes the gate, applies, verifies, refuses or defers), **Gated raiser** (takes the gate and asserts that nothing was reduced; never refuses), **Exception** (§6), **C-14** (non-refusable, gate-free), **Out of category** (cannot change a viability input; pinned). The status of every Reducer and Gated raiser is *pending* until its package integrates it; the guard fails if a pending writer already references the gate (WP1 changes no production mutation path).

| Writer | Production entry | Class | Package |
|---|---|---|---|
| Remove the administrator role | `UserManagementService` `RemoveFromUserAsync` | Reducer | WP2 |
| Assign the administrator role | `UserManagementService` `AssignToUserAsync` | Gated raiser | WP2 |
| Initial role of a provisioned person | `UserProvisioningService` | Gated raiser | WP2 |
| Disable or enable a Local credential | `UserManagementService` `SetDisabledAsync` | Reducer (disable), raiser (enable) | WP2 |
| Add an identity link (administrator) | `UserManagementService` `AddIdentityLinkAsync` | Gated raiser | WP2 |
| Remove an identity link, Local or Entra | `UserManagementService` `RemoveIdentityLinkAsync` | Reducer | WP2 |
| Registration create | `ConfigEntraTenantsController` | Gated raiser | WP3 |
| Registration edit or disable | `ConfigEntraTenantsController` | Reducer | WP3 |
| Registration delete | `ConfigEntraTenantsController` | Reducer | WP3 |
| Registration activate or switch | `ConfigEntraTenantsController` | Reducer | WP3 |
| Sign-in policy | `ConfigAuthUiPolicyController` | Gated (cannot reduce) | WP3 |
| Entra-primary policy: disable a Local credential | `EntraPrimaryAuthenticationPolicy` | Reducer (defers) | WP4A |
| Configured administrator bootstrap | `BootstrapPlatformAdminAssignment` | Gated raiser | WP4B |
| First administrator | `FirstAdminBootstrapService` | Gated raiser | WP4B |
| Bootstrap recovery | `RealBootstrapRecoveryService` | Gated raiser | WP4B |
| Recovery Access provisioning | `EmergencyAccessService` | Gated raiser | WP4B |
| Recovery Access designate or revoke (the designation flag) | `EmergencyAccessService` | Reducer (designate: D-2; revoke: C-13-R) | WP4B |
| Recovery Access MFA reset | `NativeMfaService` | Reducer | WP4B |
| MFA enrolment and recovery-code regeneration | `NativeMfaService` | Gated raiser | WP4B |
| Recovery-code consumption at sign-in | `NativeMfaService.ConsumeRecoveryCodeAsync` | **C-14** | none |
| Development bootstrap | `DevTestUserBootstrapHostedService` | **Exception E-1** | none |
| Entra link addition and proof at sign-in | `EntraIdentityAssociationService`, `SqlPrincipalToUserResolver` | **Exception E-2** | none |
| Set a password | `SetPasswordAsync` and its callers | Out of category: it never writes `IsDisabled` on an existing credential, and a new credential is enabled | none |
| Change a username | `UsernameService` | Out of category: the Local link is kept | none |
| Record a TOTP verification step | `NativeMfaService` | Out of category | none |
| A generic user update that carries the designation flag | `AccountContactService`, `AccountInvitationService`, `AccountProfileService` | **Finding F-1** (§8) | WP4B |

## 8. Departures and corrections

| | Design gate | Code at the baseline | Resolution |
|---|---|---|---|
| **F-1** | The designation flag is written by designate and revoke. | `SqlUserRepository.UpdateAsync` writes `IsBreakGlassEligible` from whatever `User` the caller holds. The contact, invitation and profile services read a user, change a field and call it, so a stale copy can restore a designation that a revoke has just cleared, or clear one. | Not changed in WP1 (behaviour neutral). The guard counts the callers so none can be added unnoticed. WP4B gives the flag one writer: `UpdateAsync` stops writing it, and a dedicated repository method, called under the gate, sets it. |
| **F-2** | The merged sign-in snapshot and `EntraConfigurationState.IsUsable` are reconciled by a differential test, the snapshot used. | They differ in one place: `IsUsable` ignores `AuthenticationContextId`, the snapshot rejects an invalid one. The endpoints gate on `IsUsable`. | The snapshot (the stricter) is used. A difference can only add a refusal, never permit a change. The provider reports the disagreement in the availability's notes and a test pins it. |
| **F-3** | One merged snapshot. | Three readers of the running Entra options exist: the endpoints (`IOptions`), the OpenID Connect handler (`IOptionsMonitor`) and the existing last-administrator guard (`IOptionsMonitor`). The registration controller reads none of them. | The Now view requires the first two to agree and reports when they do not. |
| **F-4** | Entra counts when a link is proven and Entra is usable. | Sign-in also requires the token's tenant to equal the **live active registration's** tenant, so a link proven for another tenant is not a sign-in path, and with no active registration no Entra token is accepted. | D-3. |
| **F-5** | The guards use the merged snapshot. | `AuthenticationConfigurationGuard.ValidateNoLockout` simulates a change from the deployment secret only. | Replaced in WP3 by reading the applied persisted state (§3). |
| **F-6** | The deployment configuration is visible to the viability queries. | The emergency command line binds neither `LocalAuth` nor `Entra`. | D-6, §5. A durable alternative is for the host to record its effective Local setting in the database at start, which the command line could read. It needs a schema change and is a decision for WP4B. |
| **F-7** | `TryAssignRoleAsync`, `SqlUserRepository.CreateAsync`. | They are `AssignInitialRoleAsync` and `SqlUserRepository.AddAsync`. | Names corrected in this record. |
| **F-8** | Codes `last_local_credential`, `last_viable_sign_in_method`, `entra_independent_fallback_required`. | Only `last_viable_administrator` exists as a governance code; a Local link refusal carries `identity_link.last_viable_sign_in_method`; the credential refusal is a bare `InvalidOperationException` that the API maps to a 400 without a code. | WP2 and WP3 introduce the codes and the 409 mapping. |
| **F-9** | The no-op check precedes the invariants (P-9). | In `SetLocalCredentialDisabledAsync` the last-administrator guard runs before the no-op check, so a repeated disable of the last administrator's credential is refused and recorded. | Corrected in WP2. |

## 9. Entra-primary policy: contention and failure

The policy runs after the sign-in proof has committed, from three callers that catch and log every exception. Under the gate it can meet contention. Contention or failure is **never** reported as enforcement:

- **Busy** (`PlatformAdministratorSetBusyException`) is returned as a distinct outcome, `NotAppliedBusy`, never as `Disabled`, a deferral, or a no-op. The Local credential stays enabled (the safe direction), no `LocalDisabledEntraPrimary` row is written, and the caller records the user and the reason at warning level.
- An **unknown audit outcome** keeps its existing handling and log.
- The policy **runs again at the next sign-in**, as it does today.
- A **deferral** for I-FALLBACK keeps its existing outcome and event.

Implemented in WP4A.

## 10. Packages

| Package | Content | Depends on | Review |
|---|---|---|---|
| **WP0** | This record. | rulings | scope |
| **WP1** | Gate and return-code mapping; shared viability reads and guard predicates; effective sign-in configuration provider (Now and Restart); tenant-aware Entra viability; recovery posture agreement; the writer guard with pending status; registration in the host and the command line; focused unit and SQL tests. **No production mutation path changes.** | WP0 | independent security review of the gate |
| **WP2** | Role removal, credential disable, link removal and addition; typed 409s; the `IdentityLinkException` mapping; the no-op ordering. | WP1 | refusal semantics |
| **WP3** | Registration and sign-in-policy writers on the applied persisted state; replace the simulation. | WP1 | independent security review of proposed-state viability |
| **WP4A** | Entra-primary policy: the gate, shared queries, `NotAppliedBusy`, caller surfacing. | WP1 | policy semantics |
| **WP4B** | Recovery Access (designate, revoke, MFA reset, provisioning), bootstrap writers, the command line (catches, exit codes, a test seam), F-1. | WP1 | C-13-R and C-14 |
| **WP5** | The full pair matrix on both isolation modes; the guard enforced for every writer; mutation checks; the closure record. | WP2 to WP4B | independent security review of the integrated implementation |

One branch, one pull request, one commit per package, no early merge of the foundation. No package touches IAM-11, IAM-10.3, IAM-10.4 or IAM-10.5.
