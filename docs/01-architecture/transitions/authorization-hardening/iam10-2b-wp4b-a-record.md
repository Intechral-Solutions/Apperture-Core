# IAM-10.2b WP4B-A: foundation (the designation writer, stale generic writers, pending enrolment, the gate-only entry, the first-administrator early read, the startup contract)

| | |
|---|---|
| Package | IAM-10.2b, work package 4B-A ([decision record](iam10-2b-wp4b-decision-record.md) §5.5, §6, §7; [handoff](iam10-2b-wp4b-a-handoff.md)), on the verified [WP4A](iam10-2b-wp4a-record.md) foundation. |
| Branch | `feat/iam-10.2b-administrator-viability`, from `5a77e24`. Not pushed, not merged. |
| Status | Implemented and validated (§9). Awaiting the focused independent security review. |
| Changes no behaviour of | Recovery Access designate/revoke/provision as an operation (no gate, no viability, same audit), MFA reset/enrol/regenerate, bootstrap writers, the emergency command line, the Entra-primary policy, E-1, E-2, C-14, the legacy deployment-secret fallback. |
| Not in scope | The attestation (WP4B-B), designate/revoke/reset integration and the replacement candidate (WP4B-C), bootstrap writer integration (WP4B-D), IAM-10.4, WP5. |

## 1. What changed, in one table

| Area | Before | Now |
|---|---|---|
| Designation writer (F-1) | `SqlUserRepository.UpdateAsync` copied `IsBreakGlassEligible` from the caller's record, so a copy read earlier could set or clear the designation with no service, gate or row | `UpdateAsync` never writes the flag. One conditional setter, `TrySetDesignationAsync(userId, expected, designated)`, one statement, called only by `EmergencyAccessService` |
| Stale generic writers | The system-managed restriction was decided on a copy read earlier; the write that followed could reach an account designated since | The stored designation is read **under a row lock in the writer's own transaction** at the write; a different value refuses the write with the refusal the site already has (or a retryable stale refusal) |
| Pending enrolment | `UpsertPendingAsync` rewrote an existing row and set `IsEnrolled=false`; a begin racing a completed enrolment un-enrolled it | One conditional statement; reports whether it wrote; `BeginEnrollmentAsync` throws its existing "already enrolled" error when it did not |
| Gate-only entry | None; every gate user also read viability, which can build the Entra options | `EnterGateOnlyAsync`: the gate and nothing else; no reading, no options. No production caller yet; the set of callers is closed and pinned |
| First administrator | An anonymous request for a set-up installation entered the audited unit | One inexpensive read answers `AlreadyExists` before the unit; the in-unit decision is unchanged and remains the authority |
| Startup | Order and "no early options build" held by convention | Pinned against the real host and real SQL Server (§7); a build before the migrations completed is recorded as degraded |

## 2. F-1: the designation has one writer

**Persistence correction.** `SqlUserRepository.UpdateCoreAsync` no longer names the flag. It reads the stored designation under `UPDLOCK, ROWLOCK` (untracked, so a rerun after a rolled-back attempt reads afresh), compares it with the designation of the caller's copy, and raises `UserDesignationChangedException(stored)` on a mismatch with nothing written. A matching copy proceeds exactly as before, so ordinary updates are unchanged. An update outside any unit gets a transaction of its own (through the execution strategy), because a lock released before the write guards nothing.

**Conditional setter.** `TrySetDesignationAsync(userId, expected, designated)` is `UPDATE ... WHERE Id = @id AND IsBreakGlassEligible = @expected`; true when one row changed. `EmergencyAccessService.SetDesignationAsync` is its only caller: designate demotes the previous account first (`expected: true`), then designates (`expected: false`); revoke sets `expected: true`. A request that changes nothing (re-designating the designated account) makes no call, so a legitimate no-op is not an invariant failure. A `false` result throws `AuditInvariantException`: the unit rolls back, no row is written, and the failure is an internal error, never a business refusal. The filtered unique index `UX_Users_EmergencyAccess` is untouched and is still the arbiter of one designated account (a test shows a second designation hits it).

**Not done here (WP4B-C):** `DesignateAsync`/`RevokeAsync` do not take the gate and do not evaluate viability. Their behaviour is otherwise as it was.

## 3. Stale generic writers: every site, what it does now

The requirement (decision record §6.2): an established restriction is enforced against the **persisted** designation at the write, in the same transaction, with the row held against a concurrent designation. The recheck takes **no** gate (a profile or password change must not queue behind administrator changes; a source test pins that the files take no gate), and adds no restriction, no code for an already-refused case and no changed message for it.

| Site | Check made on | Recheck at the write | Refusal when designated at write time | When revoked in between |
|---|---|---|---|---|
| `AccountProfileService` (display name, avatar x3) | copy read at the start of the unit | `UpdateAsync` (lock + compare) | the established "Recovery Access is system-managed. Display name and avatar cannot be changed here." (`UnsafeOperation`) | `StaleUpdate` (repeatable) |
| `AccountProfileService.UpdatePreferencesAsync` (time zone; allowed for Recovery Access) | copy | `UpdateAsync` | not a restriction: a mismatch is `StaleUpdate` either way | `StaleUpdate` |
| `AccountContactService` (request, verify) | copy | `UpdateAsync` | "Recovery Access does not use contact email." (`SystemManaged`) | `StaleUpdate` |
| `AccountInvitationService.StageSetupInvitationAsync` | copy | `UpdateAsync` | "Recovery Access cannot receive an onboarding invitation." | `StaleUpdate` |
| `AccountInvitationService.CompletePasswordSetupAsync` | copy | locked read of the designation **before `SetPasswordAsync`** (the password is the protected write; the address update after it also goes through `UpdateAsync`) | "Recovery Access cannot use an onboarding invitation." | n/a (read as ordinary) |
| `ForgottenPasswordService.CompleteAsync` | copy | locked read before `SetPasswordAsync` | the established invalid-link refusal (`TokenInvalid`) | n/a |
| `AuthController.ChangeLocalPassword` (`CheckPasswordChangeAsync`, the last check before the write) | copy | locked read | the established `system_managed` refusal | n/a |
| `UsernameService.RenameWithResultAsync` | copy read **outside** any transaction | `ChangeUsernameAsync(expectedDesignation: false)`: locked read in the rename's own transaction | the established "Recovery Access is system-managed. Its username cannot be changed through ordinary user administration." | n/a |
| Recovery Access canonicalization (`ApplyRecoveryAccessIdentityAsync`) | the designation set in the same unit | `ChangeUsernameAsync(expectedDesignation: true)` | n/a | `UsernameException(ConcurrentChange)` |
| `EntraIdentityAssociationService.CompleteLinkAsync` (E-2) | copy | locked read before the link write | the established `emergency_account` refusal and row | n/a |

Rejections roll the whole unit back: a consumed link, a staged credential or a staged address from before the refusal does not stay committed (SQL tests show the link usable again and no password set).

**Lock order.** Every writer above takes the user's row lock (`UPDLOCK, ROWLOCK`) as its first contended lock, or, for the password and link completions, takes it after rows that no designation touches (the link and credential rows); the designation takes the previous designee's row, then the target's, then the target's link and ledger rows. A writer holds the one row it checks and the designation waits for it, so no cycle forms between an ordinary account operation and a designation: neither waits for the gate after holding the row (the recheck takes no gate), and the designation never writes the token, credential or preference rows the ordinary writers hold. The suite ran every interleaving listed in §9 on both isolation modes and no case hung or failed on a lock error. (The unit retries a deadlock victim, so a retried victim would not have failed a case: the lock-order argument above is the claim, and the absence of failures is only consistent with it.)

**Limitations, stated plainly.**

1. **`UserManagementService`** (role, credential, link, set-password, username paths) decides the restriction after the global gate, which closes the window **only once designation takes the gate** (WP4B-C). Until then a CLI designation is not serialised with them. This package does not widen them.
2. **Link issuance with no user write.** `AccountContactService.ResendAsync` and an invitation whose pending address is unchanged issue a one-time link for an account that may be designated since. Nothing about the account changes, and completing the link goes through the recheck above and is refused, so the link is inert. Not closed separately.
3. **E-2 is not widened.** The Entra link completion keeps its place outside the gate; the recheck is the same refusal that account already had, made at the write.
4. **Outside a transaction** the generic update opens its own, so its check and its write are one unit; the only such caller is the time zone.

## 4. Pending enrolment

`SqlNativeTotpCredentialRepository.UpsertPendingAsync` is now: `UPDATE ... WHERE UserId = @id AND IsEnrolled = 0` (replace a pending row); if no row was updated and a row exists, it is enrolled and **nothing is written** (returns false); otherwise insert, and on a primary-key violation (another begin created it first) decide again. It returns whether it wrote. `NativeMfaService.BeginEnrollmentAsync` keeps its advisory check and throws its existing "An authenticator is already enrolled" error when the write reports false; the secret it generated is neither stored nor returned, and it records no audit row (it never did).

Behaviour kept: first enrolment; a retry while pending replaces the pending secret. Two begins that both find nothing end with one pending row holding the later secret (the earlier caller's code no longer matches; that is last-writer-wins on a pending credential and cannot reduce anything). **Not built:** the replacement candidate store (WP4B-C). C-14 is untouched (consumption takes no gate).

## 5. The gate-only foundation

`IAdministratorViabilityCoordinator.EnterGateOnlyAsync()` returns an `AdministratorGateOnlyScope`. It calls `IPlatformAdministratorSetGate.AcquireAsync` and nothing else: it reads no administrator fact, no deployment configuration and no sign-in configuration, so it cannot build the Entra or OpenID Connect options. It has no parameter that switches a check off.

- **Cannot be used to judge a reduction.** The scope has no members (no capture, no verdict). A test pins that by reflection.
- **The set of callers is closed.** The writer guard has `GateOnlyCallers` (empty today: **no production writer has been moved**) and a method-level pin (`GateOnlyViolations`): the method calls `EnterGateOnlyAsync`, calls none of `CaptureBefore*`, `VerifyAfterAsync`, `AssertRaiseOnlyAsync`, `PrepareAsync`, `.EnterAsync`, names no viability or deployment service, and says in a `// Cannot reduce: ...` comment why it cannot; the file must hold no reducer. The pin is shown to fail a reader, a missing entry and a missing or empty reason.
- **Order.** Used inside an audited unit it takes the gate after the unit's operation lock (a SQL test shows two application locks held, the gate held to the commit, a shared-entry writer queued behind it on the application lock and not on a row, and a busy gate is the same typed busy outcome).
- **Options not built.** A SQL test counts reads of the registration table (zero) and shows, as a control, that building the options does read it.
- Preserved: the shared guard and the coordinator's other members are unchanged; the WP3 sign-in policy writer keeps its existing form (it prepares the options and is not a startup writer).

## 6. The first-administrator endpoint

`FirstAdminBootstrapService.TryCreateFirstAdminAsync` reads the user count once before it opens the audited unit and returns `AlreadyExists` when it is not zero: no operation lock, no gate-to-be, no wait behind administrator changes. The controller's contract is unchanged (it already did this read for its status and maps `AlreadyExists` to the same conflict and refusal row, after the request validation, as before). The count is **only a reason to answer early**: the unit decides again where the user is written (a unit test plays an installation that is empty at the early read and not empty in the unit: nothing is created).

**Finding (pre-existing, not introduced here, not closed here).** The in-unit decision is not serialised: the operation lock is per operation, so two concurrent requests with **different names** both pass the count in their own units and both commit. Reproduced deterministically on locking READ COMMITTED and on READ_COMMITTED_SNAPSHOT: two administrators. The instruction for this package asks that two first administrators cannot be created; that needs the writer to take the global gate and recheck the count under it, which is the bootstrap writer integration (WP4B-D) and is excluded here. The case that must pass then is committed, **skipped**, in `FirstAdministratorConcurrencySqlServerTests` (both modes; WP4B-D removes the skip). Until then the window is the existing one (anonymous, before any user exists; hardening of that window itself is the existing OPS-1.4 item).

## 7. R-2: the startup contract, against the real host

Everything below runs the real Management Host (`Program.cs`) over real SQL Server on both isolation modes, on a **new** installation (no migration applied), an **upgrade** (one migration behind) and a **restart** (current).

**What the framework actually does.** `HostOptions.ServicesStartConcurrently` is false, so hosted services start one after another in registration order, and the web host is itself a hosted service, `GenericWebHostService`, the **last** one registered. The test records the order in which the real host *starts* them (each registered hosted service except the framework's own web host is wrapped in place by a recorder) and compares it with the order of the registered services. The real host registers 28 hosted services and starts them in this order: `TelemetryHostedService`, `HealthCheckPublisherHostedService`, `BootstrapValidationHostedService` (the bootstrap connection check), `PersistenceMigrationHostedService`, `SecretProtectorStartupValidationHostedService`, the five governance steps (`EmergencyAccessCanonicalizationHostedService`, `ModulePermissionRegistrationHostedService`, `PlatformAdminRoleRepairService`, `BootstrapPlatformAdminAssignmentHostedService`, `DevTestUserBootstrapHostedService`), sixteen later services (connector seeding and verification, module composition and slot bootstrap, platform-event, notification and attention services, the module migrations), `DataProtectionHostedService`, and `GenericWebHostService` last. That is the ratified sequence of the decision record (§7.1, steps 2 to 6). The test fails if the migrations are not before the secret validation, if the secret validation is not before every governance step, if the web host is not the last registration, if there is more than one web host registration, or if the started order differs from the registered order. **The ratified design assumption held:** the web host starts last, in the real composition, on the framework's actual behaviour; there is no discrepancy to report. (The order rests on the registration order in `Program.cs` and the service-collection extensions; the test is what pins it. Reordering `AddAppertureSqlSecrets` after `AddAppertureGovernance` fails it, §9.)

**No early build.** After the host has started, a recording build-health seam holds no record: no startup step (migrations, secret validation, the five governance steps) built the Entra or OpenID Connect options. The first request's authentication builds them, and every record is `Complete` with no reason, on all three installation states and both modes, including a new installation with an empty registration table.

**Build before ready is degraded.** `EntraAuthOptionsDatabasePostConfigure` takes `MigrationCompletionState` optionally; a build made while `IsComplete` is false is recorded `Degraded` with the new fixed reason `built_before_ready`, whatever else it found, because what it read is then frozen for the life of the process. A test inserts a hosted service ahead of the migrations that asks for the options: the record is degraded with that reason, the migrations then complete, the running reading still says degraded, and the viability reading says Microsoft sign-in is **unknown**. A host that registers no state builds exactly as before.

**Hazards that remain, named.** (a) The options are cached for the life of the process, so a build that is degraded for any reason stays degraded until a restart (unchanged R-1 behaviour). (b) Nothing prevents a future hosted service from asking for the options early; the tests above are what catch it, not a technical barrier. (c) The unavailable-secret-store case is covered by the WP3 suites (`EntraOptionsInitializationSqlServerTests`: a secret that cannot be read gives a degraded build and an unknown reading, a restart repairs it), which are in the validation set; startup writers read nothing and are unaffected.

**Dormant interface.** `IBootstrapRecoveryService` is referenced only by its contract, the stub, the real service, the two registrations and `BootstrapCliService`; `_recoveryService` is called only there; `AddAppertureRecovery` is called only by the Management Host; and the command line composition (`AddBootstrapCli`) resolves the stub service and stub authorizer. All pinned.

## 8. Audit and transaction behaviour

- Every guarded write is inside the caller's audited unit; a refusal thrown inside it rolls the unit back (no partial commit; a consumed link is usable again; no password set; no row). `AuditInvariantException` is raised only for a conditional-set mismatch and is never mapped to a refusal.
- No new audit actor and no new audit action. New operator-visible texts: "The account changed while this was being processed. Try again." (two codes, `emergency.access.stale_update` and `account.update.stale`) and the existing refusals unchanged; none carries a package, ruling or finding label.
- A rerun after a confirmed rollback re-reads the designation under the lock (the read is untracked). An unknown commit outcome is handled by the unit as before.

## 9. Validation

### 9.1 Provenance

- SDK `10.0.402` (`global.json` pins `10.0.100`, roll forward to the latest feature band). Started from `5a77e24`, clean tree; the stash and `artifacts/v94` untouched.
- SQL Server: the `apperture-test-sql` container, running and unrestarted from before the first run to after the last (started `2026-10-22T00:34:49Z`); every mutation summary records its state before and after.
- Clean builds, no incremental state, no shared compilation: `dotnet build <project> --artifacts-path artifacts/wp4ba-final --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo` for `tests/Apperture.UnitTests`, `tests/Apperture.IntegrationTests` and `src/Apperture.Cli/Apperture.Cli.Host` (0 errors each).
- Staged-tree hashes (`git add -A; git write-tree`, then the subtrees): production `src` is the same tree throughout validation, `2e619c72714b9c9e349fc220b0a9caf69e64f178`; `tests/Apperture.UnitTests` is `1d85eca6289fc31adcfa00b8d4469e83ac87fa89` throughout. `tests/Apperture.IntegrationTests` moved three times, all test-only: `7b1e1532735fdece4f06e9948730c70a712e081c` (the full focused run), `f4f1cdced8ad4a7611d6eb72472e4bc2b4bcadcd` (the startup test no longer wraps the framework's own web host registration; a recording wrapper had made the framework register a second web host), `454a16f45e6eb1fad5647728efc6ffa1987c177c` (one more designation case, below). After each change the integration project was rebuilt clean and the five new classes were rerun; the full focused run was **not** repeated for these test-only changes. The commit's own tree hashes are in the commit message.

### 9.2 Results

| Run | Result |
|---|---|
| Unit project, whole (`dotnet test --no-build`) | **5,778 passed, 0 failed, 0 skipped** (5,741 before; 37 new) |
| Focused integration set: the 27 classes of the WP4A filter plus the five new classes, both isolation modes | **1,243 passed, 0 failed, 1 skipped** (1,183 passed before; 60 new). The one skip is the first-administrator concurrency case, skipped on purpose (§6) |
| The five new classes again on the final clean build | **62 passed, 0 failed, 1 skipped** (63 cases: designation 32, pending enrolment 14, gate-only 6, startup 10, first-administrator concurrency 1 skipped) |
| Architecture tests | 38 passed |
| **Not run** | the full IntegrationTests suite and the Browser/UI suites (feature-level pre-merge requirements). `ManagementHostGovernanceHttpTests` and the other classes outside the filter were not run |

Every SQL case runs on locking READ COMMITTED and on READ_COMMITTED_SNAPSHOT (a `Theory` over both, on a catalog created with the mode, checked by the fixture). The first-administrator case is the only case that does not execute.

### 9.3 What the new tests establish (the cases the instruction names)

- **Designation / stale writers (32 SQL cases, 16 per mode):** the setter's expected-value contract; the unique index still arbitrates; a generic update never writes the designation; a display-name change, a contact request, a contact verification, an invitation, an invitation completion (with and without an address to write), a password-reset completion and a rename, each read before a designation that commits, are refused with the established refusal, change nothing, leave no row, and (for a link or token consumed in the refused unit) leave the link usable; a time-zone change read before a revocation is refused as stale and does not restore the designation; a designation waits (on the user's row, shown in the server's lock list) for a generic update and for a rename that hold the row, and both then apply; an ordinary profile update is unchanged; a designation and a revocation that overlap never leave two designated accounts, and the revocation either waits and applies in order or fails as an invariant failure with no row (both outcomes are asserted, per mode).
- **Pending enrolment (14 SQL cases):** first enrolment; retry while pending; two begins finding nothing; an enrolment completing between a begin's check and its write (the begin reports "already enrolled", the credential, its secret, the ten recovery codes and the audit rows are untouched); a begin that creates the row while an enrolment completes; the repository's return values; and a **control** that runs the original unconditional rewrite through the same interleaving and shows it un-enrol (so the setup does reach the defect).
- **Gate-only (6 SQL cases, 3 unit cases, guard pins):** §5.
- **Startup (10 SQL cases against the real host, 4 unit cases for the recording):** the order and the actual start sequence (§7, 2); no early build and a complete first build for new, upgrade and restart on both modes (6); the degraded early build (2).
- **First administrator (2 unit cases, 1 skipped SQL case):** the early answer; the early count is not authority.

### 9.4 Mutation checks

Each row mutates one line of production source, with unaffected controls in the same run, the SQL container shown running before and after, SHA-256 of the file before, mutated and after (restored byte-identically in all 15 runs), a TRX and the console output kept under `artifacts/wp4ba-logs/` (untracked). Every failure is an assertion; none is an infrastructure failure. The baseline (a no-op replacement through the same harness) passed everything.

| # | Mutation | Failed (caught by) | Controls that passed |
|---|---|---|---|
| M0 | none (text replaced with itself) | 0 of 45 | all |
| M1 | restore the flag write in the generic update | 1 unit (the source pin) | the ordinary update; "never writes the copy's designation" (2 modes) |
| M2 | drop the designation comparison | 10 SQL (display name, time zone, contact request, contact verification, invitation; 2 modes) | the setter, the ordinary update, "never writes" (6) |
| M3 | drop `UPDLOCK` from the designation read | 1 unit (pin) and 4 SQL (a designation does not wait for a generic update or a rename; 2 modes) | the stale display-name case; the setter (4) |
| M4 | the setter ignores the expected value | 1 unit and 3 SQL (the setter, 2 modes; designation against revocation under READ_COMMITTED_SNAPSHOT only: the revocation reads the pre-designation snapshot, its unconditional write then clears the wrong account's flag and leaves the new designee, with a revocation row for a change that did not happen; under locking READ COMMITTED the revocation waits at its read and the mutation is not visible) | the unique-index case (2) |
| M5 | the pending write no longer requires an unenrolled row | 6 SQL (the repository contract; the begin that creates the row; the completing enrolment; 2 modes) | first enrolment, retry, and the original-rewrite control (6) |
| M6 | a build before the migrations completed is not recorded | 2 unit and 2 SQL (the real host built early) | the two "complete / no state" unit cases; the six no-early-build host cases |
| M7 | the secret store is registered after the governance steps | 2 SQL (the real host's order; 2 modes) | the early-build case and the six no-early-build host cases |
| M8 | the reset completion drops its recheck | 2 unit and 2 SQL | the invitation completion (2) |
| M9 | the gate-only entry builds the deployment configuration | 2 unit and 2 SQL | the busy-gate case (2) |
| M10 | the first-administrator early read removed | 2 unit | one unaffected case; the "early count is not authority" case also fails, because it pins the early read too, so it is **not** an unaffected control |
| M11 | the rename drops its designation comparison | 2 SQL | the designation-waits-for-a-rename case (2) |
| M12 | the invitation completion drops its recheck | 1 unit; **the SQL invitation-completion case did not fail** | the invitation case (2) |
| M12b | as M12, after adding the SQL case in which the address is not written | 2 SQL (the new case; 2 modes) | the invitation case (2) |
| M13 | the Entra link completion drops its recheck | 2 unit (the source pin; the unit case) | none in this run |

M12 is the one survivor, reported rather than hidden: the existing SQL completion case still passed because an invitation normally writes the verified address through the generic update, whose own comparison refused it and rolled the password back. M12b closed that: the new case arranges that no address is written, so the recheck before the password is the only protection, and it fails without it. The reorder (M7) was made by moving one registration in `Program.cs`. M3's SQL cases are keyed on the hint's presence (`FROM [Users] WITH (`), so a mutation that removed the whole statement would reach no pause and fail the cases as "finished without reaching the pause"; the mutation used here removes only the lock mode, so they fail on the behaviour (no wait).

### 9.5 Limitations of this validation

- The Browser/UI suites and the full IntegrationTests suite were not run. The focused set was not repeated after the three test-only changes (§9.1); the five new classes were.
- The password change in `AuthController` and the contact `Resend` are covered by source pins and unit tests, not by a SQL interleaving (the password change needs the controller's request context). The Entra link completion is covered by a unit case and a source pin, not a SQL interleaving.
- The recorded start order is the order the framework starts the services this host registers; it does not prove that a future registration cannot start earlier, which is what the pinned order test is for.
- The first-administrator concurrency case does not execute (§6).

## 10. What the later packages must do with this

- **WP4B-D** (bootstrap writers): move the configured-administrator, first-administrator and bootstrap-recovery writers onto `EnterGateOnlyAsync` where the affected user holds no administrator facts (adding each to `GateOnlyCallers` with its `// Cannot reduce:` reason), or onto the capture-and-assert form where that cannot be shown; unskip `FirstAdministratorConcurrencySqlServerTests` and make it pass on both modes; recheck eligibility under the gate. The writer guard still reports those writers as pending gated raisers, accurately.
- **WP4B-B** (attestation): nothing here forces the options to be built; keep it so.
- **WP4B-C** (designate/revoke/reset): put `DesignateAsync`/`RevokeAsync` behind the gate and viability; that also closes limitation 1 of §3 (designation serialised with the gate-holding `UserManagementService` writers). The conditional setter and its single caller are the seam.

## 11. Documentation still needed before RC1

The decision record now states the owner's exceptional-recovery policy (§3.6): supported restorative and host-restoration procedures first; a controlled manual database intervention only as a last resort, with the offline window, verified database identity, backup, viability assessment, independent review, integrity verification and external incident record; no direct-repair logic, bypass flag or `--local-sign-in` override; automated offline recovery is a possible post-RC1 improvement and not a commitment. **Still to write before RC1:** the operator-facing runbook for that exceptional procedure (the checklist above, the integrity queries, the incident-record template). It belongs in the operations documentation, not in the product.

## 12. Post-RC1 candidates (not commitments)

Automated offline incident recovery; a technical barrier to early options construction (for example a guard that fails fast if the options are built before the migrations complete); closing the first-administrator window beyond the gate.
