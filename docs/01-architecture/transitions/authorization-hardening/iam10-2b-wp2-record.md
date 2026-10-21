# IAM-10.2b WP2: person-driven administrator, credential and link writers

| | |
|---|---|
| Package | IAM-10.2b, work package 2: the person-driven writers of administrator viability on the [WP1 foundation](iam10-2b-wp1-review-record.md) ([WP0 reconciliation](iam10-2b-wp0-reconciliation.md) §7, rows marked WP2). |
| Branch | `feat/iam-10.2b-administrator-viability`, from `746d5e7`. Not pushed, not merged. |
| Status | Implemented and validated; awaiting the independent security review. Nothing here reopens a decision of the [design gate](iam10-design-gate.md) or a ruling D-1 to D-6. |
| Not in scope | WP3 (registration and sign-in policy writers), WP4A (Entra-primary policy), WP4B (Recovery Access, bootstrap writers, the command line, F-1), WP5 (the full matrix and the closure record). |

## 1. What is integrated

One call shape in every writer, in the audited unit's delegate: the gate, then (for a reducer) the state before, the change, the state after and the verdict, or (for a raiser) the state before, the change and the assertion that nothing was reduced.

| Writer | Entry | Class | Gate position | Refusal (409, stable code) | Existing audit action on refusal |
|---|---|---|---|---|---|
| Remove the administrator role | `UserManagementService.RemoveRoleAsync` | Reducer | Before `LockAuthoritySnapshotAsync`, when the request names the role | `last_viable_administrator`, `entra_independent_fallback_required` | `auth_admin.blocked.last_platform_admin_removal` |
| Assign the administrator role | `AssignRoleAsync` | Gated raiser | Same | none (asserted) | none |
| Initial role of a provisioned person | `UserProvisioningService` (`DecideInitialRoleAsync`, `AssignInitialRoleAsync`) | Gated raiser | Before the authority locks, when the role is the administrator role | none (asserted) | none |
| Disable a Local credential | `SetLocalCredentialDisabledAsync` | Reducer | First statement of the delegate | `last_local_credential`, `entra_independent_fallback_required` | `auth_admin.blocked.last_admin_credential_disabled` |
| Re-enable a Local credential | same method | Gated raiser | Same | none (asserted) | none |
| Remove a Local or Entra link | `RemoveIdentityLinkAsync` | Reducer | First statement of the delegate | `last_viable_sign_in_method`, `entra_independent_fallback_required` | `auth_admin.blocked.last_admin_link_removed` |
| Add a link (administrator) | `AddIdentityLinkAsync` | Gated raiser | First statement of the delegate | none (asserted) | none |

For every reducer, a configuration that cannot be established is the refusal `administrator_viability_unavailable` (D-6 fail closed), recorded with the same action. A busy gate is `governance_operation_busy` (409, `retry: true`), no refusal row.

**Order inside a reducer** (P-9, F-9): authorization and the system-managed target refusals; the no-op (`DeclareNoOp`); the state before; the change, applied through the repository; the state after; the verdict; on a violation `Refuse` (which rolls the provisional change back and records the refusal after the rollback); otherwise the audit row is staged. The credential disable and the link removal now decide the no-op **before** the invariants (F-9 corrected: a repeated disable of an already disabled credential is a silent no-op, not a recorded refusal).

**What replaced what.** The assignment-count guard in `RemoveRoleAsync`, the `adminCount == 1 && HasViableAlternateSignIn` predicate in the credential and Local-link paths (which read `EntraConfigurationState.IsUsable` and ignored the tenant and the Restart view) and the bare `InvalidOperationException` of the credential refusal are gone. The Entra link removal, which had no guard, now has the same one.

## 2. New and changed code

| File | Change |
|---|---|
| `Security/AdministratorViability/AdministratorViabilityCoordinator.cs` | `IAdministratorViabilityCoordinator.EnterAsync` takes the gate and returns an `AdministratorViabilityScope` (`CaptureBeforeAsync`, `VerifyAfterAsync`, `AssertRaiseOnlyAsync`). Every read of a scope is issued after the gate is held. |
| `Security/AdministratorViability/AdministratorRoleGate.cs` | Takes the gate when the request names the administrator role (as typed or as the stored row it resolves to); fails the unit if the locked row is the administrator role and the gate was not taken. |
| `Security/AdministratorViability/ViabilityRefusal.cs` | The verdict-to-409 mapping: the code names the violated condition for the writer. |
| `Security/GovernanceRefusals.cs` | Codes `last_viable_sign_in_method`, `last_local_credential`, `entra_independent_fallback_required`, `administrator_viability_unavailable`. |
| `Security/UserManagementService.cs`, `UserProvisioningService.cs` | The integrations above. The unused `IOptionsMonitor<EntraAuthOptions>` constructor parameter is removed. |
| `Security/IdentityLinkException.cs` | `identity_link.last_viable_sign_in_method` removed (it never reached a client: the exception was unhandled). |
| `Management.Api/Controllers/UsersController.cs` | The link, credential and role endpoints map the governance outcomes (409 refusals, busy with `retry`) and no longer map an `AuditInvariantException` to a 400/404. |
| `Security/ServiceCollectionExtensions.Governance.cs` | Registers the coordinator (host and command line). |
| `docs/04-api/contracts.md` | The contract of the refusals. |

## 3. Deviations and decisions made in this package

1. **No new audit action or schema.** The refusal rows use the operation's existing `auth_admin.blocked.*` action, whose schemas are pinned. They therefore do not say *which* invariant was violated, nor that the cause was an unavailable configuration. The code is in the API response. Distinguishing them in the audit trail needs a catalog change and is left for an owner decision.
2. **`administrator_viability_unavailable` is a new stable code** (409, no `retry`), for the fail-closed outcome of D-6, which the design gate names but gives no code.
3. **The administrator role is recognised before the role is read.** The gate has to precede the authority row locks, which are what read the role, so the request's id is recognised as typed (case-insensitive, trimmed) and, if that does not match, by the id of the stored row. The unit fails (`AuditInvariantException`) if it reaches a locked administrator role without the gate. A removal of any other role takes no gate.
4. **The gate is taken for every credential disable/enable and link addition/removal**, not only those that concern an administrator. These are rare, and deciding whether the target is an administrator before the gate would be a read outside it.
5. **A raiser asserts that its own change reduced nothing** and throws `AuditInvariantException` if it did (a defect). *As first built* it compared two whole-system evaluations, which a configuration reload, a consumed recovery code or another writer's change could fail; that is corrected by the [WP2 review remediation](iam10-2b-wp2-review-record.md) (M-2), which attributes the assertion to the affected user under the configuration captured before the change.
6. **In-memory unit tests roll a refused attempt back** (`RecordingAuditedUnitOfWork.CaptureState`), as the SQL unit does, because the change is now applied before it is refused.

## 4. Pending writers (unchanged by this package)

Every Reducer and Gated raiser not listed in §1 stays *pending* in the writer guard and must not yet reference the gate or the coordinator: registrations and the sign-in policy (WP3), the Entra-primary policy (WP4A), the bootstrap writers, Recovery Access provisioning and designation, MFA reset and enrolment (WP4B). **The complete cross-writer guarantee cannot be claimed until those packages are integrated.** E-1, E-2 and C-14 are unchanged and still pinned by the guard.

## 5. Evidence

Real SQL Server, each case on a locking READ COMMITTED catalog and on a READ_COMMITTED_SNAPSHOT catalog; no case skipped.

| Area | Where | Result |
|---|---|---|
| Sequential matrix: last viable administrator with two assigned, stranded administrators, already-empty sets, no-ops (F-9), credential disable with a working, other-tenant, pending, absent or locally-off Entra alternative, one and two Entra-independent administrators, Recovery Access healthy and unhealthy and never counted, Local and Entra link removal, raise-only changes, fail closed, authorization first | `AdministratorViabilityWp2SqlServerTests` | passed |
| Every ordered pair of the four reducers (16, plus the same pair run sequentially) | `A1_*` | one commit and one refusal; the second writer is shown waiting on the application lock with no row lock held or awaited; the first writer is parked after its change and its state read |
| Control with the gate removed (three pairs) | `Control_*` | under row versioning both reductions commit and no administrator can sign in; under locking READ COMMITTED the second writer waits on a row lock, not the gate |
| Lock order | `A14_*` | the operation lock is the only application lock held when the gate is requested; no authority lock or write precedes it; one acquisition per attempt; capital and padded spellings of the role id; an ordinary role takes no gate |
| Busy, cancellation, deadlock rerun | `A15_*`, `A17_*` | `governance_operation_busy`, nothing changed and no refusal row, the same request then succeeds; cancellation is not busy; a row-lock deadlock victim reruns the whole unit and takes the gate again |
| M6 for each integrated unit (role removal, credential disable, Local link removal, role assignment, link addition) and for a refusal made after the write | `M6_*` | passed; gate taken once per attempt (twice after a confirmed rollback) |
| Host: stable codes, bodies, busy with `retry`, Entra-enabled host | `AdministratorViabilityHostHttpTests`, `RoleAuthorityHostHttpTests` | passed |

326 integration cases (the classes above, with the WP1 viability suites and the two audit suites that exercise these writers) and 5513 unit cases passed on the committed tree; the IAM-10.2a role authority suites (265 cases with the audit suites) and the remaining host and recorder suites that exercise these writers passed on the final code too. The whole solution builds. The multi-hour full integration suite was not run.

**Mutation checks** (source edited, built, run, restored with `git checkout`): bypassing the gate in the coordinator fails 92 of the 236 WP2 SQL and host cases, including all 32 concurrent pairs, the lock-order cases, the busy, cancellation and rerun cases and the M6 gate counts. Taking the gate after the authority locks in the role removal fails the lock-order cases and the writer guard; making the credential disable ignore its verdict fails the credential and Recovery Access cases.

## 6. Remaining findings

F-1 (WP4B, mandatory), D-6 (owner decision before WP4B), the secret-resolution uncertainty and the deployment-secret fallback (WP3), R-2 (WP4B) are unchanged. R-1: no new unsafe path. The handler's options are built by the request pipeline before any unit runs; were they built inside a unit they would be read through a scope of their own from the registration and secret tables, which no WP2 unit locks; an unreadable handler makes the Entra reading unknown and every reduction fails closed (`R1_*`). The full R-1 analysis belongs to WP3, where registration writers do hold those rows. The management client still shows a bare "409" for a refused role removal (the UI is IAM-10.4).
