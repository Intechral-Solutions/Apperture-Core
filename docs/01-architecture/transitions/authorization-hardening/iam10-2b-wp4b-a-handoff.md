# IAM-10.2b WP4B-A: implementation handoff

For a fresh implementer with no prior context. Read this file, then the [WP4B decision record](iam10-2b-wp4b-decision-record.md) (§5.5, §6, §7, §9), then the sections of the [WP0 reconciliation](iam10-2b-wp0-reconciliation.md) it points to. Do not reopen any ruling.

## 1. Checkpoint

- Repository `Intechral-Solutions/Apperture-Core`, branch `feat/iam-10.2b-administrator-viability`. Start from the commit that adds this file (its parent is `873da13`, WP4A verified). Check `git status` is clean, `git stash list` has one stash, `git worktree list` shows `artifacts/v94`. **Preserve** the stash, `artifacts/v94`, and every directory under `artifacts/` (earlier evidence is kept there). No push, merge or PR. One local commit at the end.
- Follow `AGENTS.md` and `.ai/playbooks/anti-patterns.md`. No planning labels (package names, ruling ids, finding ids) in anything an operator can see: messages, audit details, settings text. They belong in docs, tests and comments.
- Tools: use `git grep` and `git ls-files` in Bash (the Grep and Glob tools time out because of `artifacts/`). Use Edit/Write for source, not shell rewrites. Never put SQL credentials on a command line.

## 2. Goal

Foundational correctness before any Recovery Access, bootstrap or command-line writer is integrated. WP4B-A changes **no Recovery Access, MFA-reset, bootstrap or command-line behaviour** and integrates no reducer. It closes three defects and pins the startup contract.

## 3. Work items

### A1. F-1: the designation flag has one writer

Code today: `SqlUserRepository.UpdateAsync` (`src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlUserRepository.cs`, the line `entity.IsBreakGlassEligible = user.IsBreakGlassEligible`) writes the flag from the caller's record. Callers: `AccountContactService` (2), `AccountInvitationService` (2), `AccountProfileService` (4), and `EmergencyAccessService.SetDesignationAsync`.

Do:

1. Remove the designation write from `UpdateAsync`.
2. Add to `IUserRepository` and `SqlUserRepository` a conditional setter, for example `TrySetDesignationAsync(UserId, bool expected, bool designated, ct)` returning whether a row changed, implemented as one `ExecuteUpdate ... WHERE Id = @id AND IsBreakGlassEligible = @expected`. Give every `IUserRepository` fake in the tests the method.
3. Move `EmergencyAccessService.SetDesignationAsync` to the setter. A `false` result throws `AuditInvariantException`. **Do not add the gate here** (that is WP4B-C); keep the service's behaviour otherwise as it is. Keep the order "demote the previous designee first, then designate" (the filtered unique index `UX_Users_EmergencyAccess` stays).
4. Writer guard (`tests/Apperture.UnitTests/AdministratorViability/AdministratorViabilityWriterCoverageGuardTests.cs`, the `Finding` rows and the "F-1" test near the end): remove the three services as findings, pin that `UpdateAsync` never names `IsBreakGlassEligible`, and pin the setter's single production caller.

### A2. Stale generic writers must not bypass the system-managed restrictions

Decision record §6.2 has the table and the requirement. Implement:

1. In `UpdateAsync`: read the user row under an update lock inside the current transaction (for example a locking read of the row), compare its stored designation with `user.IsBreakGlassEligible` of the caller's copy, and refuse a mismatch with a typed, non-sensitive exception. A matching designation proceeds as today, so ordinary updates are unchanged.
2. Map the exception at the three account services so the established refusal is reused when the account is designated at write time, and a retryable stale-update refusal is used when it is not. Do not add a new restriction, a new error code for an already-refused case, or a changed message for it.
3. Apply the same persisted-designation recheck to `UsernameService.RenameWithResultAsync` (it reads the user **outside** any transaction; recheck inside the repository's transaction) and to the completion in `ForgottenPasswordService`. For `EntraIdentityAssociationService` (exemption E-2), add the recheck only if it is a local change; otherwise record the limitation in the commit message and the record. Do not widen E-2.
4. Do not take the global gate in any of these writers.

### A3. `UpsertPendingAsync` must not reduce

`SqlNativeTotpCredentialRepository.UpsertPendingAsync` (`SqlNativeMfaRepositories.cs`, near line 20) rewrites an existing row and sets `IsEnrolled = false`; `NativeMfaService.BeginEnrollmentAsync` checks first and writes later outside any unit. Make the pending write atomic and conditional on there being no enrolled row (one statement, or a row-version compare), report whether it wrote, and make `BeginEnrollmentAsync` throw its existing "already enrolled" error when it did not. Keep first enrolment working. **Do not** build the replacement candidate (WP4B-C); the decision record §5 lists its requirements for later.

### A4. R-2: startup contract

1. **Order pin.** A test enumerates the hosted services of the real Management Host composition (see how `EntraRegistrationHostSqlServerTests` and `EntraOptionsInitializationSqlServerTests` build a host) and fails unless: the persistence migration service is before the secret-protector validation service, which is before every governance startup service (`EmergencyAccessCanonicalizationHostedService`, `ModulePermissionRegistrationHostedService`, `PlatformAdminRoleRepairService`, `BootstrapPlatformAdminAssignmentHostedService`, `DevTestUserBootstrapHostedService`), and the web host service is the last registration. If the web host is not last, report it: do not guess a fix.
2. **No early build.** Start the real host against a fresh test catalog and against one with a pending migration; assert that no Entra or OpenID Connect options were built before the first request (the `IEntraOptionsBuildHealth` record is empty), and that the first request's build is complete, not degraded, on a fresh database.
3. **Build-before-ready is degraded.** In `EntraAuthOptionsDatabasePostConfigure` record a build that happens while `MigrationCompletionState` says the migration is incomplete as degraded (the reason must be a new value of the existing build-health vocabulary), so the Now view reads Entra as unknown. Inject the state optionally; a host without it behaves as today.
4. **Gate-only class.** Add to the writer guard a class for a writer that takes the gate and reads no viability state ("gated, cannot reduce"; the WP3 sign-in policy writer is the precedent and may be moved to it). Its method-level pin: calls `EnterAsync`, calls none of `CaptureBefore*`, `VerifyAfterAsync`, `AssertRaiseOnlyAsync`, `PrepareAsync`, and the source comment states why it cannot reduce. **Do not move any bootstrap writer into it yet** (WP4B-D does that).
5. **Dormant interface pin.** A test pins that `IBootstrapRecoveryService` is called only by `BootstrapCliService`, and that the command-line composition resolves the stub.

### A5. Out of scope (do not touch)

Recovery Access designate/revoke/provision behaviour and gating; MFA reset/enrol/regeneration behaviour; the replacement candidate; bootstrap writers; the first-administrator endpoint; the attestation; the command line; audit `configurationEvidence`; the Entra-primary policy; the guard, the gate, the evaluator; E-1 and E-2; IAM-10.4 UI; the legacy deployment-secret fallback.

## 4. Acceptance tests

Real SQL Server, **both isolation modes** (the `AuditIsolationCatalogs` fixture with `rcsi: false/true`; copy the structure of `EntraPrimaryPolicyViabilitySqlServerTests`, its partial files, `RoleAuthorityWorld` and `PauseAt`). Deterministic, with statement-level pauses, never sleeps.

- F-1 and the stale writers: decision record §6.3, every bullet.
- `UpsertPendingAsync`: begin parked after its check, an enrolment completed, begin resumed: the enrolment stays enrolled and begin reports "already enrolled"; first enrolment still works; a pending row is replaced by a new pending row.
- R-2: the order, no-early-build, degraded-before-ready, fresh-install-not-degraded cases of the decision record §7.2 that belong to A.
- Unit: the guard changes; the repository fakes; the account services' mapping of the stale refusal; the rename and password-reset rechecks.
- Mutation checks, each with a passing control in the same run and the SQL container shown running before and after: restore the flag write; drop the designation comparison; drop the lock; make the setter unconditional; remove the not-enrolled condition; remove the build-before-ready record; reorder two hosted services.

## 5. Validation protocol (learned in earlier packages)

- **SQL container first.** `docker ps -a`; if `apperture-test-sql` is not running, `docker start apperture-test-sql` (only that one). A SQL "failure" that takes ~30 ms in total is the database not being reachable, not an assertion.
- Builds: `dotnet build <project> --artifacts-path artifacts/wp4ba-final --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo` for `tests/Apperture.UnitTests` and `tests/Apperture.IntegrationTests` (about 5 minutes each). Use `--artifacts-path` directories under `artifacts/` (the Aspire host locks the default output). Never use the scratch directory for builds.
- Run: the whole unit project (about 3 minutes; 5,741 passed at the start) and the focused integration set. The ready-made filter of the focused set (27 classes, about 30 minutes, 1,183 passed at the start) is the single line in `artifacts/wp4a-rem-logs/focused-int-filter.txt` (pass it as `--filter`); add your new classes. Run long runs in the background and poll in PowerShell loops of at most 9 minutes. Do not run the full multi-hour suite or the Browser/UI suites.
- Mutation harness: `artifacts/wp4a-rem-logs/mutate.ps1` and `run-mutations.ps1` (keeps trx, console output, `docker inspect` snapshots, SHA-256 before/after, and takes `-Controls`). Copy and adapt into `artifacts/wp4ba-logs/`; do not edit the originals. A control must be unaffected by that mutation. A no-op "mutation" (text replaced by itself) is a good baseline.
- Record the tree hashes of the staged tree before committing (`git write-tree`, then `git rev-parse <tree>:src <tree>:tests`).
- Expect the earlier unit suites that fake `IUserRepository` or assert the old behaviour to need the new method; update them rather than loosen assertions.

## 6. Deliverables and stop

1. The code and tests above, one local commit on top of this file's commit (message in the style of the earlier packages, ending with the attribution line given in your session).
2. An addendum section in the [WP4B decision record](iam10-2b-wp4b-decision-record.md) (a new `## 12. WP4B-A evidence` with the commands, results, mutation table, tree hashes and any limitation), in the same commit.
3. Handback: start and final HEAD, files changed, clean tree, preserved state; the F-1 and stale-writer findings (what each site now does); the `UpsertPending` result; the R-2 results including what you found about the web host's position; validation numbers for both isolation modes; mutation results with controls; limitations. Verdict `WP4B-A READY FOR VERIFICATION` or `BLOCKED` with evidence.
4. Stop after the handback. WP4B-D is next and waits for the focused review of WP4B-A.
