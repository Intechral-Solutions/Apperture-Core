# IAM-10.2b WP4B-C service implementation: Recovery Access and native MFA writers on the accepted foundation

| | |
|---|---|
| Package | IAM-10.2b, work package 4B-C, the service package on the independently accepted Foundation 1A to 1D ([persistence closure](iam10-2b-wp4b-c-persistence-remediation-record.md), accepted at `1c1030ad`). |
| Branch | `feat/iam10-2b-wp4bc-services` (worktree `wt-iam10-2b-wp4bc-services`), from `1c1030ad9b023aeba48518f4e2cc09881c1527ae`. Local only: not pushed, no PR. The protected IAM branch `feat/iam-10.2b-administrator-viability` was already at `1c1030ad` and was not changed. |
| Status | **Independently reviewed at `ec66ed4e`: ACCEPT WITH BOUNDED REMEDIATION, the one required item being SR-1.** SR-1 is remediated at `05ffe194` (section 15) and is **not yet closure-verified**. Nothing here declares acceptance. |
| Not in scope | The emergency command line (commands, exit codes, the mapping of busy, unavailable, conflict and unknown outcomes), the WP5 pair matrix, OPS-1, development-environment and tooling work, credential rotation, artifact cleanup, Configuration C. |

## 1. Identity

| | |
|---|---|
| Accepted baseline | `1c1030ad9b023aeba48518f4e2cc09881c1527ae` (`src` `2b7705a6035ff302c864e5cba0ad472b186454c9`) |
| Checkpoint A | `4dddb77a`: Recovery Access writers and L-P3 |
| Checkpoint B | `9b61d1a6`: native MFA integration (enrolment, reset, regeneration, replacement, the switch's persistence defences) |
| Recovery-code fix | `378086eb`: conditional consumption of a recovery code (section 9) |
| Test tightening | `0865e07a`: the D-1 bare-reset case decided under an attestation whose Microsoft reading is not established (section 10) |
| Preparation (U1) | `18d86e5e`: every writer of this package that reads viability prepares the reads before its unit (section 2) |
| Test tightening | `c1741d47`: the mis-cased replacement case raises the unused code count, after mutant M04b escaped it (section 11) |
| **Validated code commit** | **`c1741d47`** (`c1741d4786dde318cb8d7a1487fdcb086dc07bce`; see section 10 for the tree hashes). Later commits are documentation only and keep its `src` and `tests` trees |
| Independent review | `ec66ed4e` (records over `c1741d47`): ACCEPT WITH BOUNDED REMEDIATION (SR-1) |
| SR-1 correction | `27bc2968`: the switch refuses without the caller's open transaction; standalone test callers re-based (section 15) |
| SR-1 controls | `05ffe194`: the RP1 regression and the candidate-repository caller rules. **SR-1 code commit** (`05ffe1948ecc378535d8adef40e5cc6224d334e7`; trees in section 15). Later commits are documentation only |

`git diff --shortstat 1c1030ad c1741d47`: 11 production files (+796 / -107), 19 test files (+2993 / -115), nothing outside `src` and `tests`. Production files: `EmergencyAccessService.cs`, `NativeMfaService.cs`, `UserManagementService.cs`, `GovernanceRefusals.cs`, `AdministratorViability/ViabilityRefusalAudit.cs` (new), `INativeMfaService.cs`, `INativeMfaRepositories.cs`, `INativeMfaReplacementCandidateRepository.cs`, `SqlNativeMfaRepositories.cs`, `SqlNativeMfaReplacementCandidateRepository.cs`, `AuditActionCatalogEntries.cs` (two comments).

## 2. Obligation map

| Mutation | Service method | Coordinator path | Transaction | Audit (success / refusal) |
|---|---|---|---|---|
| Designate | `EmergencyAccessService.DesignateAsync` | `EnterAsync` → load → no-op → `CaptureBeforeAsync` → demote, designate, rename → transition read back → `VerifyDesignationAsync` last | one refusable audited unit, gate first | `emergency.access.designated` (+ `canonicalized`) with the decision's evidence / `emergency.auth.blocked.administrator_viability`, `recovery_access.designate` |
| Strict revoke | `RevokeAsync` | `EnterAsync` → load designated → `CaptureBeforeAsync` → setter → transition read back (D-2) → `VerifyRevocationAsync` last → decision checked to be about the revocation (D-2) | same | `emergency.access.revoked` + evidence / blocked, `recovery_access.revoke` |
| Provision | `ProvisionAsync` | `EnterAsync` → designation decided again → `CaptureBeforeForRaiseAsync` → account and password → `AssertRaiseOnlyAsync` | audited unit | `emergency.access.provisioned` |
| Set password (administrators) | `UserManagementService.SetLocalPasswordAsync` (L-P3) | `EnterAsync` → load → restriction → locked designation recheck → write | audited unit | `auth.password.set` |
| Bare reset | `NativeMfaService.ResetAsync` | `EnterAsync` → load → no-op → `CaptureBeforeAsync` → delete credential and codes → `VerifyBareResetAsync` last | refusable audited unit | `auth.mfa.reset` + evidence / blocked, `mfa.reset` |
| First enrolment | `CompleteEnrollmentAsync` (L-P2) | `EnterAsync` → load → code → `CaptureBeforeForRaiseAsync` → conditional enrolment → codes → `AssertRaiseOnlyAsync` | audited unit | `auth.mfa.enrolled` + `auth.mfa.recovery_codes.generated` |
| Regenerate codes | `RegenerateRecoveryCodesAsync` | gated raiser, as above | audited unit | `auth.mfa.recovery_codes.generated` |
| Stage, verify, abandon | `BeginReplacementAsync`, `VerifyReplacementAsync`, `AbandonReplacementAsync` | none: no gate, no unit, nothing held across operator input | the candidate store's own transaction | none |
| Switch | `CompleteReplacementAsync` | `EnterAsync` → load → `CaptureBeforeAsync` → `SwitchAsync` → `VerifyReplacementAsync` last → basis must be `PostureOnly` (I-R3-1) | refusable audited unit | `auth.mfa.replaced` + `auth.mfa.recovery_codes.generated` / blocked, `mfa.replace` |
| Recovery-code sign-in | `ConsumeRecoveryCodeAsync` (C-14) | none: no gate, never refused | audited unit, conditional consumption | `auth.mfa.recovery_code.consumed` |

Every writer above that reads viability first calls `PrepareAsync` outside any transaction, before its unit (U1, R-1): designation, revocation, provisioning, bare reset, first enrolment, regeneration and switch. In the command line's composition, the only one that runs these writers, the reading comes from the host's attestation, so preparing builds nothing. A failed preparation stops the writer before its unit. The administrator set-password takes the gate but reads no viability state, so, like the other `UserManagementService` reducers, it does not prepare.

## 3. Recovery Access writers (Checkpoint A)

- **Designation.** A reducer of both invariants: the gate is the first lock of the unit; the account is loaded and every later step names it by the loaded row's id (D-1); the re-designation of the designated, canonical account is a no-op decided before the invariants; the state before is read under the gate; the previous account is demoted and the account designated through the one conditional setter; the canonical rename is applied; the designation is read back (the account loaded is the one designated account, otherwise an invariant failure); `VerifyDesignationAsync` decides last, after every write including the rename (D-3). Rows are staged only after the decision (the canonicalized row is returned by the rename, not staged by it). A refusal is recorded after the rollback through `ViabilityRefusalAudit`.
- **Strict revocation.** The gate first; the designated account loaded (`revoked = designated.Id`); the state before; the one setter; the transition read back in the unit (the account read as designated no longer is, and nobody is); `VerifyRevocationAsync(revoked)` last; then the decision's own states are required to show the transition (captured: the revoked account designated; judged: nobody designated). Either check failing is an `AuditInvariantException`, never a refusal or a success (D-2).
- **Provisioning.** A gated raiser: the gate first; the designation decided again under it (a designation that committed while it waited is the typed `emergency.access.already_designated` refusal, not a unique-index failure); the raise-only assertion around the new account and its password. It needs no configuration evidence and records none.
- **The emergency set-password** writes the credential and the row under the loaded row's id (D-1).
- **L-P3.** `UserManagementService.SetLocalPasswordAsync` takes the gate before it reads the user, and reads the stored designation under the user's row lock (`GetDesignationForUpdateAsync`) immediately before the credential write, held to the end of the transaction. The designation and the revocation now take the gate too, so the two are serialised; the locked read also refuses a writer of the designation that did not take the gate.

## 4. Native MFA (Checkpoint B)

- **Every administrative operation** (begin and complete enrolment, regeneration, reset, stage, verify, switch, abandon) loads the user row first and names the account by its id (`AccountOfAsync`); generated recovery codes are hashed before any unit and given their owner only after the account is loaded (D-1, S-3).
- **First enrolment.** Beginning takes no gate (a pending secret is not a posture input and the WP4B-A write never replaces an enrolled one). Completion is a gated raiser; a completion whose pending secret was replaced since its code was checked is the typed, retryable `authenticator_changed` conflict and the unit rolls back (L-P2).
- **Bare reset.** A reducer: removal, then `VerifyBareResetAsync(owner)` last; allowed on the posture alone when it cannot reduce (another account, an unhealthy posture, still healthy), otherwise on the attested Local reading; refused with `entra_independent_fallback_required` or `administrator_viability_unavailable` and recorded with the decision's evidence.
- **Replacement, the normal workflow.** `BeginReplacementAsync` stages a candidate (its secret protected under the native MFA protector key id, as an active secret is; lifetime 10 minutes by the database's clock) and returns it once; `VerifyReplacementAsync` checks the code against the **candidate's** secret (unprotected with the key id recorded on the candidate), with replay protection against the active authenticator's last used step, and records the matched step through the conditional `MarkVerifiedAsync`; neither takes the gate, opens a unit or changes the active authenticator, which stays usable until the switch. `CompleteReplacementAsync` switches inside the refusable audited unit under the gate, decides last with `VerifyReplacementAsync(owner)`, and keeps the switch only when the decision's basis is `PostureOnly` (I-R3-1); a posture refusal is `recovery_access_posture_reduced`. The new codes are shown only from the committed result (or from verification of a lost acknowledgement).
- **Candidate outcomes (U2).** `NoActiveEnrollment` → `authenticator_not_enrolled` (never a fallback to a reset or an enrolment); `NotFound`, `Superseded`, `VersionMismatch`, `AlreadyVerified`, and `Consumed` by another → `authenticator_replacement_no_longer_valid`; `Expired` → `authenticator_replacement_expired` and the candidate is discarded after the unit; `ActiveEnrollmentChanged` → `authenticator_changed`; `NotVerified` at a switch → `AuditInvariantException` (a defect). Every one wrote nothing.
- **U3.** A tracked write's `DbUpdateConcurrencyException` in completion, regeneration or reset is translated once, at the service, into `authenticator_changed`, with the original kept as the inner exception; there is no automatic retry.

## 5. The switch's persistence defences (S-1, S-2, S-3)

`SqlNativeMfaReplacementCandidateRepository.SwitchAsync` changed in three bounded ways; its conditional transition, its lock order and every refusal outcome are unchanged.

- **Identity (S-1).** The new active secret takes the candidate's own id as its `CredentialId` (`t.[CredentialId] = c.[CandidateId]`), read back for the result. A `Consumed` outcome alone proves nothing; the service reports `AlreadyApplied` (with no codes) only when the candidate is consumed **and** the active credential's identity is that candidate's id, otherwise `no longer valid`.
- **Codes (S-2, S-3).** Refused before any statement: codes recorded as consumed, repeated ids (compared as the column does, without regard to case), and codes that do not fit the column lengths.
- **Owner (S-3).** The locked read of the active credential also returns the stored owner; a request naming the user other than as stored is an `ArgumentException` and nothing is written.
- **Not adopted.** Requiring an ambient transaction in `SwitchAsync` (S-1, "consider"): the accepted persistence suite calls it standalone. The single production caller is pinned instead (writer guard): inside the audited unit, after the gate. **Superseded by SR-1 (section 15):** the switch now requires the caller's open transaction.

## 6. Audit (U7, S-4, S-5)

- **One mapping.** `ViabilityRefusalAudit` builds every `emergency.auth.blocked.administrator_viability` row from a decision: `operation` from the mutation (`recovery_access.designate`, `recovery_access.revoke`, `mfa.reset`, `mfa.replace`; any other mutation is rejected), `code` from the decision's typed refusal or given explicitly, and `configurationEvidence` from the decision. The audit contract pins that only this function calls the producer.
- **Null evidence (S-5).** A decision without an evidence class was made by a host on its own options. Its **success** row leaves the detail out (the producers' optional parameter and `ViabilityDecision.Evidence` contract); its **refusal** row records `not_required`, the only one of the three canonical values that is true of it (no attestation was read; nothing was missing). A test covers every guarded mutation with every evidence value.
- **S-4.** A replacement whose decision was not made on the posture alone is refused with `administrator_access_changed` (a retryable `GovernanceConflictException`); the unit rolls back; a refusal row is written after the rollback with operation `mfa.replace`, code `administrator_access_changed` and the evidence class of the discarded decision (D-3: a kept refusal records the decision's evidence; the code says the configuration was not the cause).
- **Success rows.** `auth.mfa.replaced` with the decision's evidence (`not_required` for every kept replacement) and `auth.mfa.recovery_codes.generated` (`regenerated: true`); `auth.mfa.reset`, `emergency.access.designated` and `emergency.access.revoked` with the decision's evidence. Restoratives carry no evidence detail.

## 7. Disposition matrix

| Id | Disposition | Where | Tests | Negative controls |
|---|---|---|---|---|
| D-1 | Implemented | `DesignateAsync` (`target = user.Id`), `RevokeAsync` (`revoked = designated.Id`), emergency set-password, `AccountOfAsync` in every MFA operation, `SetLocalPasswordAsync` | SQL: mis-cased designation (success and refusal rows named exactly as stored), mis-cased bare reset decided on the stored id, mis-cased replacement decided on the stored id (the switch raises the unused code count) with codes and candidates owned as stored; unit: mis-cased designation; guard pins | S02, M04, M04b |
| D-2 | Implemented | `RequireRevocationTransitionAsync`, `RequireDecidedOnTheRevocation` | unit: a setter that lies, another account designated after the write, states of another transition, the transition shown | S03, S04 |
| D-3 | Implemented | every reducer verifies after its last write and stages after the decision; refusal rows carry the decision's evidence | unit: logged order of writes and reads (designation, revocation); guard order pins; evidence asserted on every refusal and success | S01, S05, M05, M11 |
| D-4 | Tracked, not widened | decision-time expiry only, as the foundation | none new | none |
| U1 | Implemented | section 4; `PrepareAsync` before the unit in every viability-reading writer (`18d86e5e`) | unit and SQL replacement cases; guard order pins; a failed preparation changes nothing (unit, both services) | M01, M02, S10, M12 |
| U2 | Implemented | section 4 | unit theory over every refused outcome; SQL expired, superseded, stale version, regenerated, reset | M03 |
| U3 | Implemented | `TranslatingConcurrencyAsync` | unit (cause kept); accepted SQL reset-against-switch case (cause in the chain) | none |
| U4 / I-R3-1 | Implemented | `CompleteReplacementAsync` basis check | unit: allowed and unavailable fall-through; SQL: an ungated Microsoft link proof while the switch holds the gate, then a successful rerun | M01 |
| U5 | Implemented | matched candidate step; `(candidateId, version)` returned; discard on expiry and abandonment; codes owned by the user | unit and SQL | M02 |
| U6 | Implemented | writer guard (stage, verify, switch, discard kinds; integrated; nothing pending); audit contract pins; catalog comments | unit guard | (guard) |
| U7 | Implemented | `ViabilityRefusalAudit` | unit over every mutation and evidence value | M11 |
| S-1 | Implemented (ambient-only not adopted) | sections 4, 5 | SQL: lost acknowledgement, unknown outcome then repeat, rolled-back commit, two switches; unit applied-earlier and not-active cases | M03, M09 |
| S-2 | Implemented | no catch around the switch in the unit (guard pin); codes validated before statements | SQL: a statement failing after the switch's first writes; unit partial switch | (guard) |
| S-3 | Implemented | D-1 plus the ordinal owner check and the code checks | SQL: the switch's own defences | M10 |
| S-4 | Implemented | section 6 | unit and SQL | M01 |
| S-5 | Implemented | section 6 | unit over every mutation and evidence value | M11 |
| L-P2 | Implemented | conditional completion as a gated raiser with a typed conflict; the switch applies only the verified candidate at its version | SQL: a begin replaces the pending secret while the completion holds the gate; accepted conditional-enrolment case (typed conflict) | M06 |
| L-P3 | Implemented | gate and locked recheck in `SetLocalPasswordAsync`; designation and revocation gated | SQL: the designation waits; a writer outside the gate is refused by the recheck; a control shows the defect on the same interleaving | S07, S08, S09 |
| Recovery-code concurrency | Demonstrated and fixed | section 9 | SQL 3 cases × 2 modes; unit | M07, M08 |

## 8. Transaction and outcome guarantees

- **Order in the unit.** The audited unit's operation lock, then the gate (`EnterAsync`), then every row; operator input and TOTP entry happen before any unit (staging and verification hold nothing; enrolment's code is a parameter).
- **Refusal.** A refusal declares the attempt a no-op: the unit rolls back everything the attempt applied (writes, rename, switch) and the refusal row is written afterwards on a context of its own (best effort, W3).
- **Failure inside a switch.** Never caught in the unit: the exception ends the attempt and the unit rolls back the half-applied switch (SQL case: the credential, the codes and the verified candidate are as before, and the candidate switches afterwards).
- **Commit outcomes.** Confirmed commit (the result); confirmed rollback (the attempt reran once, after verification, and applied once); unknown (`AuditOutcomeUnknownException`; a repeat verifies against the identity before it reports the replacement applied, and never claims codes); retryable conflict (typed, nothing written).

## 9. Recovery-code sign-in concurrency (the review's open item)

**Assessed and demonstrated.** `RecoveryCodeConsumptionConcurrencySqlServerTests` was run first against the unchanged consumption path (the code of Checkpoint B before the fix): **4 of 6 failed**, on both isolation modes and in both orders. Two sign-ins that read the code as unused before either wrote both returned true and wrote two consumption rows while one code was marked used (unused 10 → 9); a sign-in that waited on the first one's row overwrote it and succeeded too. Evidence: `trx/recovery-code-assessment-before-fix.trx`.

**Corrected** (`378086eb`), mirroring B-1: `MarkConsumedAsync(userId, codeId, at)` returns bool and is one `ExecuteUpdate` conditional on the id, the owner and `ConsumedAtUtc IS NULL`; the sign-in is accepted only when that write consumed the code, otherwise the attempt is a no-op and records nothing. C-14 is unchanged: no gate, never refused. After the fix the same class passes 6 of 6.

## 10. Validation

All steps ran at the validated code commit `c1741d47`:

* tree `a85fd1ad6acbeaf8481fa59b78fecdd2d43e2196`;
* `src` `c9348c106851b7e54aaf0bbed1b685ba33893128`;
* `tests` `4b5b681b72706c366c9ab20b16ca7e4d2502163d`;
* SDK 10.0.402.

In the worktree, `src` and `tests` were equal to HEAD; the only uncommitted files were these records. Evidence: `artifacts/svc-logs/final/status.json`, the TRX files, and the logs.

| Step | Result |
|---|---|
| Clean build | `dotnet build Apperture.slnx -c Release --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers --artifacts-path artifacts/svc-release`: **0 errors**, 73 warnings (the accepted baseline also had 73), 446 s |
| UnitTests | **6245 passed, 0 failed**, 1 skipped by design (the opt-in desktop password-timing diagnostic) |
| ArchTests | 38/38 |
| Management.Ui.Shared.Tests | 1497/1497 |
| Management.Ui.Client.Tests | 532/532 |
| AppHost.Diagnostics.Tests | 7/7 |
| **Focused SQL**, Release, real SQL Server | **1409/1409**: 634 cases with `rcsi: False`, 634 with `rcsi: True`, and 141 host and audit cases that take no mode parameter. Every filter term matched cases; the password appears in no output; 3041 s |
| Of which: the accepted foundation | The accepted persistence filter (21 terms) matches **1156 cases, all passed** (557 per mode, 42 without a mode). This is the count the accepted remediation recorded at `1c1030ad`, so the foundation's own cases pass unchanged in number. Changed assertions are listed in section 12 |
| Of which: new classes | `RecoveryAccessViabilityServiceSqlServerTests` 42 (21 per mode), `NativeMfaServiceViabilitySqlServerTests` 42 (21 per mode), `RecoveryCodeConsumptionConcurrencySqlServerTests` 6 (3 per mode) |
| Of which: classes the changed services reach | `AccountServicesAuditSqlServerTests`, the Management Host session identity, username rename, break-glass, account profile, bootstrap, Entra registration and authorization removal HTTP classes, `AdministratorViabilityHostHttpTests`, `PasswordChangeDesignationSqlServerTests`, `PreferencesAtomicitySqlServerTests`, `FirstAdministratorConcurrencySqlServerTests` |
| SQL honesty | **PASS, authoritative** (`scripts/dev/Test-SqlExecutionHonesty.ps1 -Configuration Release`, built by the script, in a clean clone at `c1741d47`, tree `a85fd1ad`). Discovered and executed 3868; passed 227; failed 3641, every one categorised as SQL Server unavailable. No unexpected pass, must-fail violation or other violation. The accepted run had 3778; the difference is this package's 90 new SQL cases. The allow-list (227 entries), the must-fail list (188), their pin test and the script are blob-identical to `1c1030ad`: none was changed |
| Candidate index (I-1) | **Before** the runs, read-only: no catalog on the test server had the candidate table (20 `Apperture*` catalogs). **After**: 20 catalogs; the three shared fixture catalogs that the runs migrated to the accepted model (`Apperture_ManagementGovernanceTests`, `Apperture_BootstrapHttpTests`, `Apperture_BootstrapFirstAdminTests`) all have `IX_UserTotpReplacementCandidates_UserId`; none lacks it. Nothing was reset or dropped |
| Negative controls | 23/23 caught at `c1741d47` (section 11) |

**Earlier development runs** are in `runs.txt`; they are not the final evidence. Recovery Access 92/92. Native MFA 297/301. The four failures were two cases in each mode. One was the accepted conditional-enrolment case, which still expected the old untyped exception and was re-based for the typed conflict (section 12). The other was a new case whose audit-row reader parsed a JSON boolean as `True`, a test helper defect. Both were fixed, then 54/54. Host 145/145. The recovery-code assessment before the fix: 2/6, the demonstration of section 9. After the fix: 168/168.

**Not run:**

* the full `IntegrationTests` assembly against real SQL Server: only the focused set ran against it, and the honesty run executes the whole assembly against an unreachable endpoint;
* `BrowserTests` and `EndToEndTests`, which need a running environment;
* the emergency command line end to end (this package does not change it) and a running Management Host smoke test;
* Linux and hosted CI;
* an independent pre-review panel: one owner implemented the package.

## 11. Negative controls

**Method.** The harness (`artifacts/svc-logs/tools/mutate.ps1`, with the exact edits in `mutants.json`) works on a disposable clone of the committed HEAD:

* each mutant is one or more exact text edits, each required to match exactly once;
* the unit and integration test projects are rebuilt (`-m:1`, no shared compilation, no build servers);
* the mutant's unit and SQL filters run with `TreatNoTestsAsError`;
* each edited file is restored with `git checkout` and checked against the committed blob hash;
* a green baseline runs before the mutants and a green final pass after them;
* every output is scanned for the SQL password.

| Commit | Run |
|---|---|
| `0865e07a` | Stopped during the baseline, before any mutant ran: the code changed (`18d86e5e`) |
| `18d86e5e` | 22 of 23 caught. **M04b escaped**: in the mis-cased replacement case the switch replaced ten unused codes with ten, so nothing the guard reads changed and the id made no difference. The case was strengthened in `c1741d47` |
| **`c1741d47`** | **23 of 23 caught**, every restore verified. Baseline and final pass: unit 165/165, SQL 102/102. Password found in outputs: 0 |

Failing tests per mutant at `c1741d47`. "Unit" and "SQL" are the failed cases among those its filters ran; every SQL case runs in both isolation modes.

| Id | Property | Unit | SQL | Caught by (examples) |
|---|---|---|---|---|
| S01 | Designation: a refusing decision is acted on | 3 | 12 | the last-viable-administrator and unavailable refusals; the designation racing the disablement of the other viable administrator |
| S02 | D-1: the designation's success row names the stored account | - | 2 | mis-cased designation |
| S03 | D-2: the revocation reads its own transition back | 2 | - | a setter that lies; another account designated after the write |
| S04 | D-2: the decision must be about the revocation's own transition | 1 | - | states that do not show the transition |
| S05 | Strict revocation: a refusing decision is acted on | 2 | 8 | no other Entra-independent administrator; missing, stale or stopped attestation |
| S06 | Provisioning decides the designation again under the gate | 1 | 2 | a designation committed while provisioning waited |
| S07 | L-P3 as it was: no gate and no locked recheck | 2 | 4 | the guard pins; the waiting designation; the recheck |
| S08 | L-P3: the locked recheck alone | - | 4 | the same SQL cases |
| S09 | L-P3: the gate alone | - | 2 | the designation waiting for the password change |
| S10 | U1: the revocation does not prepare | 2 | - | a failed preparation; the guard order |
| M01 | I-R3-1: a decision not made on the posture alone is refused | 2 | 2 | the ungated Microsoft link proof while the switch holds the gate |
| M02 | Possession is proven against the candidate's secret | 18 | 26 | every replacement case |
| M03 | S-1: "applied earlier" only when the candidate is the active authenticator | 1 | - | a consumed candidate whose identity is no longer active |
| M04 | D-1: the bare reset is decided on the stored id | - | 2 | mis-cased bare reset |
| M04b | D-1: the replacement is decided on the stored id | - | 2 | mis-cased replacement (after `c1741d47`) |
| M05 | Bare reset: a refusing decision is acted on | 2 | 6 | posture refusal, unavailable, mis-cased reset |
| M06 | L-P2: an enrolment whose conditional write changed nothing completes | 1 | 4 | the begin between the check and the enrolment (two classes) |
| M07 | Recovery code: the sign-in is accepted without its write having consumed the code | 1 | 4 | both concurrency interleavings |
| M08 | Recovery code: the consumption is not conditional on the code being unused | - | 4 | both concurrency interleavings |
| M09 | S-1: the new secret is not given the candidate's id | - | 12 | lost acknowledgement, unknown outcome, rolled-back commit, two switches |
| M10 | S-3: the switch compares the stored owner ignoring case | - | 2 | the switch's own defences |
| M11 | U7/S-5: a refusal row does not record the decision's own evidence | 25 | - | every mutation × evidence value; refusals of every service |
| M12 | U1: the replacement prepares inside its unit, after the gate | 2 | - | a failed preparation takes no gate; the guard order |

**Not covered by a mutant here:**

* S-2's input checks in the switch (used, repeated and oversized codes). These are pinned by the real-SQL defence case.
* The rule against catching an exception around the switch. This is a guard pin, and the partial-failure cases cover it behaviourally.
* The U3 translation.
* The discard on expiry.

Each of these has a unit or SQL case, listed in section 7.

## 12. Accepted tests re-based (and why)

| Test | Change | Reason |
|---|---|---|
| `RecoveryAccessDesignationSqlServerTests.A_designation_and_a_revocation_that_overlap…` | waits on the gate instead of the user's row; asserts the serial outcome only | the revocation now takes the gate before it reads; the WP4B-A invariant-failure branch cannot occur under the gate |
| `IdentityGovernanceAuditSqlServerTests.Revoking_Recovery_Access_after_a_lost_acknowledgement…` | adds an ordinary administrator with an enabled Local credential | strict revocation now stands only with another Entra-independent administrator |
| `ConditionalMfaEnrollmentSqlServerTests.A_begin_between_the_check_and_the_enrolment…`, `NativeMfaServiceTests.An_enrollment_whose_pending_secret_was_replaced…` | expect the typed `authenticator_changed` conflict | L-P2/U3: a typed, retryable conflict instead of a bare `InvalidOperationException` |
| `AdministratorViabilityWriterCoverageGuardTests` | inventory, integrated packages, method pins, the pending case now asserts nothing is pending | the service package integrates the last pending writers |
| `MfaReplacementAuditContractTests` | the dormancy pins move to the first callers | U6 |
| Construction helpers (`MfaCandidateWorld`, `AuthenticationAuditSqlServerTests`, `PendingMfaEnrollmentSqlServerTests`, `IdentityGovernanceAuditSqlServerTests`, `MfaReplacementCandidateLockOrderSqlServerTests`, `RecoveryAccessDesignationSqlServerTests`, unit harnesses) | build the services with the coordinator, the refusal writer, the user and candidate repositories | the operations now require them; a missing one is a wiring defect that fails loudly |

No assertion of an accepted case was weakened; every interleaving still forces the same order, and the lock-order cases still assert zero deadlocks with and without retry.

## 13. Remaining risks and owner rulings

**Choices made here that the owner may overrule** (each is a definition the obligations asked for, not a deviation):

| # | Choice | Why | Alternative |
|---|---|---|---|
| R-1 | A refusal row of a decision without an evidence class records `not_required`; a success row leaves the detail out (S-5) | the refusal producer requires a value, and of the three canonical values only `not_required` is true of a host deciding on its own options; the success producers already treat the detail as optional | a fourth value (needs a catalog change and an owner ruling under section 4) |
| R-2 | The I-R3-1 refusal (`administrator_access_changed`) records the evidence class of the decision it discarded, which can be `unavailable` after a fall-through | D-3: a kept refusal records the decision's evidence; the code, not the evidence, says the configuration was not the cause | record `not_required` for this refusal |
| R-3 | A replacement stages two rows: `auth.mfa.replaced` and `auth.mfa.recovery_codes.generated` (`regenerated: true`) | the switch replaces the codes, as an enrolment stages `auth.mfa.enrolled` with the codes row | one row only |
| R-4 | The new active secret's `CredentialId` is the candidate's id | it makes "applied earlier" verifiable (S-1) without a new column | a separate identity plus a stored link |
| R-5 | ~~`SwitchAsync` does not require an ambient transaction; its single production caller is pinned inside the unit~~ **Superseded by SR-1** (review SO-2): the switch requires the caller's open transaction and the persistence cases are re-based (section 15) | the accepted persistence suite calls it standalone (S-1 said "consider") | require one and re-base the persistence cases (done) |
| R-6 | `EnsureCanonicalIdentityAsync` stays ungated: it renames the designated account to its canonical name at host start and from the command line | it changes the account's username and display name only, which no viability decision reads (the rename inside a designation is in the designation's unit) | gate it (gate-only entry) |

**Known limits, not defects:**

* **Expiry discard.** On `Expired` the service discards the user's live candidate through the accepted user-scoped `DiscardAsync`. A candidate staged by another request between the expiry and the discard is removed too, and its verification then reports that it is no longer valid (fail-safe: nothing is activated).
* **Replay floor.** A candidate's code must match a step later than the active authenticator's last used step. An operator who has just signed in with the old authenticator may have to wait for the next 30-second step.
* **Lifetime.** A candidate lives 10 minutes by the database's clock. The wait for the gate counts against it.
* **Approximate count.** The `remaining` count on a recovery-code row is computed from the list read before the conditional consumption, so under concurrency it can be off by the concurrent consumptions (the acceptance itself is exact).
* **D-4 is unchanged.** Evidence age is judged at decision time only.
* **Enrolment of an unknown account.** Beginning an enrolment now requires the account to exist ("User not found."), as every other operation does.

**Entry obligations for the emergency command line** (not done here, by scope):

1. Map `GovernanceConflictException` (every code of section 4 plus the viability codes) and `PlatformAdministratorSetBusyException` to distinct operator outcomes and exit codes (U3). Today neither is caught by the emergency commands: both reach the top-level handler of `Program.cs`, which prints `Error: <message>` and exits with 1. An unknown outcome (`AuditOutcomeUnknownException`) is already caught there and tells the operator to run `emergency status` before trying again.
2. Make `reset-mfa` the replacement workflow (begin, show the secret once, read the code, verify, complete, show the codes once; abandon on cancel). Keep the bare reset as an explicit, separately named operation. Today `reset-mfa` performs a bare reset followed by a new enrolment.
3. Never print a recovery code or secret from anything but the committed result; on an unknown outcome, tell the operator to check the state instead of repeating.

**Open environment item.** The test SQL container `apperture-test-sql` is still published on `0.0.0.0:1433` (`docker ps`, 2026-11-09). Its remediation is a pending owner action recorded on 2026-11-07, and this package changed nothing about it.

## 14. Evidence

**Archive.** `G:\Backup\Apperture-Core-worktree-evidence\2026-11-09\wt-iam10-2b-wp4bc-services-evidence`. Access is restricted, by inheritance, to the owner, SYSTEM and Administrators.

* Size: 361 files, 116,762,952 bytes.
* `SHA256SUMS.txt` is CRLF with backslash paths, so verify it with PowerShell `Get-FileHash`. Manifest SHA-256: `8320ADCE50497260A4D952038FD24F5C5CED9BD8642335E8ACEA827B946F9C45`.
* It was copied from `artifacts/svc-logs` and verified file by file: 0 mismatches.

Contents:

* `final/`: the clean build log, suite TRX and logs, and `status.json`.
* `trx/`: the focused SQL TRX, the development TRX, and the recovery-code assessment that failed before the fix.
* Mutation runs: `mutation/` (at `c1741d47`), `mutation-at-18d86e5e/` (with the M04b escape) and `mutation-stopped-at-0865e07a/`.
* `honesty/`: the report, logs and TRX.
* The candidate-index checks, before and after.
* `tools/`: the SQL runner, the mutation harness and `mutants.json`, the final validation, the index check and the archive script. Each reads the connection string into its own process only.
* The commit messages.

**Secret scan before the copy.** The test SQL password appears in no file. No file matches an `otpauth://` URI, a password assignment, a private key or an Azure key pattern, apart from the scanning script's own pattern text.

**Bundle.** `G:\Backup\Apperture-Core-git-bundles-integration\feat-iam10-2b-wp4bc-services-<head>.bundle` holds the branch at its final commit. Its SHA-256 is in `SHA256SUMS-2026-11-09-services.txt` in that folder and in the hand-over message. The bundle carries the branch and no `HEAD`, so clone it with `git clone -b feat/iam10-2b-wp4bc-services`.

**Working copies.** The worktree `F:\Code\Apperture\wt-iam10-2b-wp4bc-services` holds `artifacts/svc-logs` and the build outputs:

* `artifacts/svc-dev`: development builds;
* `artifacts/svc-release`: the final clean build;
* `artifacts/svc-final`: a partial build, stopped at `0865e07a` when the code changed (not evidence).

The mutation clone is `artifacts/svc-mut/clone`, clean at `c1741d47`; its honesty build is in `artifacts/honesty-build`. Nothing was cleaned up.

## 15. SR-1 remediation (2026-11-09)

**Review.** The independent review of `ec66ed4e` (code `c1741d47`) returned **ACCEPT WITH BOUNDED REMEDIATION**. Its record is archived at `G:\Backup\Apperture-Core-worktree-evidence\2026-11-09\review-wp4bc-services-ec66ed4` (77 files, `SHA256SUMS.txt` manifest `E476086D55B8BE28D3BE7E59AD037A3CBC8EB3AA98C48B2EA2F9936DC8FF789B`, re-verified on 2026-11-09 with 0 mismatches and left unchanged). Recovery Access, the MFA workflows, viability enforcement, refusal handling and recovery-code concurrency were accepted. The one required item was **SR-1**: S-1 was enforced only by the call graph and a lexical guard. The persistence switch, called outside a transaction, opened and committed its own (reviewer probe RP1), and the guard did not see a `var` local resolved from a service provider (reviewer guard probe A). Nothing else was reopened.

### 15.1 Identity

| | |
|---|---|
| Reviewed | `ec66ed4ec53f409c754cc2c7d123a6b102aa47fc` (code `c1741d47`, `src` `c9348c10`, `tests` `4b5b681b`) |
| `27bc2968` | fix: the switch refuses without the caller's open transaction; standalone test callers re-based |
| `05ffe194` | test: the RP1 regression and the candidate-repository caller rules. **SR-1 code commit** `05ffe1948ecc378535d8adef40e5cc6224d334e7`, tree `87cc5989e0c44566f65396297e30e54d93d4fe29`, `src` `6dfcdbe83945edd67e0c02d1c0f0c686eec9f310`, `tests` `35ccbbc7d0c092c94ec1f28d8069e474d4c1e456` |
| Later | documentation only (this section, the closure brief, the decision record) |

`git diff --stat ec66ed4e 05ffe194`: 8 files, all in `src` and `tests`. Production: `SqlNativeMfaReplacementCandidateRepository.cs`, `INativeMfaReplacementCandidateRepository.cs` (documentation). Tests: `MfaCandidateWorld.cs`, `MfaReplacementCandidateConcurrencySqlServerTests.cs`, `MfaReplacementCandidateLockOrderSqlServerTests.cs`, `NativeMfaServiceViabilitySqlServerTests.cs` (re-based callers), `MfaReplacementCandidateSqlServerTests.cs` (the regression), `AdministratorViabilityWriterCoverageGuardTests.cs` (the caller rules). Nothing outside `src` and `tests` changed in those two commits.

### 15.2 The transaction precondition

The first statement of `SqlNativeMfaReplacementCandidateRepository.SwitchAsync`, before the argument checks and any SQL:

```csharp
if (_db.Database.CurrentTransaction is null)
    throw new InvalidOperationException(
        "The authenticator switch runs only inside the caller's open transaction (the audited unit, under the administrator-set gate); it never commits on its own.");
```

* **Why this check is genuine enlistment.** The audited unit (`SqlAuditedUnitOfWork.RunAttemptAsync`) begins its transaction with `_db.Database.BeginTransactionAsync` on the scoped `AppertureDbContext`. The repository is registered scoped over the same context, and every statement of the switch goes through `_db.Database`, which runs on that context's current transaction. No ambient `System.Transactions` marker is consulted. Mutant SR1-M3, which checks that marker instead, breaks 38 of 58 cases of the persistence class on the real audited-unit and caller-transaction paths.
* **Convention.** An `InvalidOperationException`, as `SqlPlatformAdministratorSetGate.AcquireAsync`, the managed-contact email lock and the role authority snapshot throw when their transaction is absent. It is not a typed business outcome, not a `NativeMfaSwitchResult`, and not a standalone write.
* **No transaction of its own.** The body runs directly on the caller's transaction; it no longer goes through `InTransactionAsync`, which stays for staging, verifying and discarding, whose standalone use is legitimate.
* **Unchanged.** Every condition of the switch statement (candidate identity, owner, verified status, version, database-clock expiry, the credential it was staged against, the replay step it carries), the ordinal owner check, the input defences of S-2 and S-3, the lock order, the outcomes and the result identity. Inside the audited unit the code path is the same as before: `InTransactionAsync` already ran the body directly when a transaction was present.
* The contract (`INativeMfaReplacementCandidateRepository`) states the requirement and the exception.

The optional hardening the review mentioned, also requiring the administrator-set application lock to be held by that transaction (`APPLOCK_MODE`), was **not adopted**: it would add a statement before the refusal and a dependency of the persistence layer on the gate's resource name, and the brief asked for the smallest safe correction. The guard's sequence pin still requires the one production switch to come after `Viability.EnterAsync(`.

### 15.3 Standalone tests re-based

Every test call of the real switch that had no transaction now runs it on a transaction of its own through `MfaCandidateWorld.SwitchInTransactionAsync`. That helper begins the transaction under the context's execution strategy, switches, and commits; a throw rolls it back. That is exactly what the repository's own transaction did before, so a retrying context still retries (the lock-order cases still count every deadlock victim they retry) and a pause interceptor still sees the same statements.

* Re-based: `MfaCandidateWorld.SwitchAsync` (used by the persistence, contract, concurrency and replay suites), the two parked switches of `MfaReplacementCandidateConcurrencySqlServerTests`, the parked switch of `MfaReplacementCandidateLockOrderSqlServerTests`, and the switch-defence case of `NativeMfaServiceViabilitySqlServerTests` (whose three refusals must be the defences named, not S-1's).
* Untouched: the switches already inside an audited unit (`A_switch_inside_the_audited_unit_…`) or an explicit transaction (`MfaReplacementViabilityIntegrationSqlServerTests`).
* No assertion was changed or removed.

### 15.4 The caller guard

`AdministratorViabilityWriterCoverageGuardTests` gains two rules and a self-test:

* **`Only_the_enumerated_files_name_the_candidate_repository`.** Any production file whose code (comments removed) names `INativeMfaReplacementCandidateRepository` or `SqlNativeMfaReplacementCandidateRepository` as a whole word must be one of four enumerated files, each with its reason: the contract; the implementation; `NativeMfaService.cs`, the one replacement service, whose switch order is pinned by the sequence check; and `ServiceCollectionExtensions.Governance.cs`, the two registrations, which resolve and call nothing. There are no wildcards. The actual production inventory is exactly those four files (`git grep`).
* **Receivers.** The candidate-store writes are now counted on receivers declared with either type, or held in a `var` local resolved through `GetService<…>` or `GetRequiredService<…>`. A write in an enumerated file can therefore not hide behind the service-locator idiom either.
* **Self-test.** The rules find the reviewer's probe A text, a typed local and a concrete-type `var` local; an unclassified `MfaCandidateSwitch` writer is reported for each; and a comment or a longer identifier is not a caller.

This is a complement to the run-time precondition, not a replacement for it.

**Clarification about the reviewed commit.** At `c1741d47`, `MfaReplacementAuditContractTests.The_candidate_repository_is_named_by_its_contract_its_implementation_and_the_registrations_only` (in the `Audit` namespace of the unit tests) already pinned the same four files with a whole-word match, and it fails on probe A: re-run against the old guard file at the SR-1 head, it fails on probe A and on the concrete-type probe. The review ran probe A against the writer-coverage guard class only (42/42), so its observation is accurate for that class, and the unit suite as a whole would have failed. The SR-1 rules put the requirement into the writer-coverage guard itself, with reasons and the receiver scan.

### 15.5 Negative controls

All four were run in a disposable clone at `05ffe194` (`artifacts/sr1-mut/clone`), with the connection string in the runner's process environment only. No output contains the test SQL password.

**The RP1 regression.** `MfaReplacementCandidateSqlServerTests.A_switch_without_the_callers_transaction_is_refused_before_any_statement_and_commits_nothing` runs on both isolation modes. On a retrying context and on a plain one, neither with a transaction:

* the call throws `InvalidOperationException` and the context sends **zero commands** (every reader, non-query and scalar counted);
* the context has no transaction afterwards;
* read back by SQL on another connection, nothing changed: the active credential and its identity, the secret digest, the step, the timestamps, the recovery codes, the candidate row (verified, same version, secret kept) and the `auth.mfa.replaced` and `auth.mfa.recovery_codes.generated` counts;
* the same candidate then switches on a caller's transaction.

**RP1 reproduced** (`tools/rp1.ps1`, an uncommitted probe class that makes the reviewer's exact call: the repository on `Scratch.NewDb()` with no transaction). Only `SqlNativeMfaReplacementCandidateRepository.cs` is taken from `c1741d47` for the first run.

| Switch | `rcsi: False` | `rcsi: True` |
|---|---|---|
| Reviewed (`c1741d47`) | `Applied`; the credential took the candidate's id; codes replaced; candidate `Consumed`; `auth.mfa.replaced` 0 → 0; codes-generated 1 → 1 | same |
| SR-1 (`05ffe194`) | refused; credential, codes and candidate (`Verified`) unchanged; 0 → 0; 1 → 1 | same |

The clone was clean afterwards and the repository file's blob matched `HEAD`.

**Mutants** (`tools/mutate.ps1`, `tools/mutants.json`: exact single-match edits, restore proven by blob hash). The baseline was green (unit 44/44, SQL 58/58), and so was the final run after every restore.

| ID | Mutation | Verdict | Detected by |
|---|---|---|---|
| SR1-M1 | the precondition removed (the statements run without a transaction and each commits by itself) | **Caught** 2/2 | the regression, both modes: no exception |
| SR1-M2 | the reviewed behaviour: without a caller transaction, open and commit one (RP1) | **Caught** 2/2 | the regression, both modes: no exception |
| SR1-M3 | the precondition checks `System.Transactions.Transaction.Current` instead of the context's transaction | **Caught** 38/58 | the audited-unit and caller-transaction cases of the persistence class |
| SR1-M4 | the refusal comes after the first statement (the credential lock) | **Caught** 2/2 | the regression: one command sent before the refusal |
| SR1-G1 | the guard no longer sees a `var` local resolved from a service provider | **Caught** 1/44 | the self-test |

**Guard probe A** (`tools/guard-probe.ps1`). The probe file is placed in the clone's `src` and the guard classes (the writer-coverage guard and `MfaReplacementAuditContractTests`, 55 tests) run from the DLL already built. Nothing is rebuilt between variants, so a detection can only come from the probe file.

| Variant | Guard at `05ffe194` | Guard file as at `c1741d47` |
|---|---|---|
| none | 55/55 | 53/53 |
| A, the reviewer's `var store = services.GetRequiredService<INativeMfaReplacementCandidateRepository>()` (byte-identical) | 3 failures: `Only_the_enumerated_files_name_the_candidate_repository`, `Every_production_writer_of_a_viability_input_is_classified` (unclassified `MfaCandidateSwitch` writer), the audit-contract pin. Each message names the probe file | 1 failure: the audit-contract pin only (the writer-coverage guard passes, as the review found) |
| B, the reviewer's typed local | the same 3 failures | not run (the review showed it detected) |
| C, `var store = services.GetService<SqlNativeMfaReplacementCandidateRepository>()!` | the same 3 failures | 1 failure: the audit-contract pin only |
| none, after | 55/55 | |

Afterwards the guard file's blob matched `HEAD`, there were no tracked changes, and no untracked file was in `src`.

### 15.6 Validation at `05ffe194`

| Check | Result |
|---|---|
| Clean Release build (`--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers`, fresh `artifacts/sr1-release`) | exit 0, 0 errors, 73 warnings (as at `c1741d47`), 389 s; started on a tree with no tracked change |
| Unit | 6247 passed, 0 failed, 1 skipped (the opt-in desktop timing diagnostic, as before): the reviewed 6245 plus the two new guard facts |
| Arch | 38/38 |
| Ui.Shared, Ui.Client, AppHost.Diagnostics | 1497/1497, 532/532, 7/7 |
| Focused SQL (the 36-term filter of section 10, byte-identical), both modes | **1411/1411**: 635 with `rcsi: False`, 635 with `rcsi: True`, 141 without a mode; 2582 s. Every case of the reviewed run (1409) is present and passed; the only additions are the two regression cases. No output contains the test SQL password |
| Affected classes during development (Release, both modes) | 244/244: the persistence, concurrency, lock-order, contract, viability-integration, service-viability and replay-protection classes |
| SQL honesty (`scripts/dev/Test-SqlExecutionHonesty.ps1 -Configuration Release`, built by the script in the clean clone at `05ffe194`) | **PASS, authoritative**, mode `enforce`: discovered and executed 3870, passed 227, failed 3643; no unexpected pass, must-fail violation or other violation. The two more than the reviewed 3868 are the regression cases. The allow-list (227 entries), the must-fail list (188), the script and its tests are blob-identical to `1c1030ad`; `git diff --name-only 1c1030ad 05ffe194 -- scripts .github tests/test-manifest.json` is empty |
| Normal path | the audited-unit replacement still commits with `auth.mfa.replaced` and its final viability decision: `A_switch_inside_the_audited_unit_commits_with_its_audit_row_and_rolls_back_with_the_unit`, `The_real_switch_inside_a_protected_attempt_is_decided_on_the_posture_alone_and_commits_with_it`, and the `NativeMfaServiceViabilitySqlServerTests` replacement cases, all in the focused set |

**Not run:** the full `IntegrationTests` assembly against real SQL (only the focused set; the honesty run executes the whole assembly against an unreachable endpoint), the browser and end-to-end suites, the command line end to end, a running host, Linux and hosted CI.

### 15.7 Owner rules and deferred items

* **SO-1, standing rule (no change of behaviour).** A refusal row whose decision carries no evidence class still records `not_required` (R-1). The owner has not authorised a general change. The standing rule: `not_required` may describe a decision that genuinely required no configuration evidence; it must not conceal unavailable evidence that was required. In the only composition that runs the four guarded operations (the emergency command line), the reading is always `Attested` or `Unavailable`, so the fallback is not reached there. Before any host-composition caller of designate, revoke, bare reset or switch is added, the owner rules on a fourth value or on keeping those operations out of the host composition.
* **SO-2** (R-5): superseded by SR-1.
* **SC-1 to SC-5**, the command-line requirements of the review, are carried forward unchanged to the command-line package: the typed-outcome map and exit codes (SC-1), `reset-mfa` as the replacement workflow (SC-2), codes and secrets shown only from a committed result (SC-3), a composition test of the command line's container (SC-4), and reaching the switch only through `INativeMfaService.CompleteReplacementAsync` (SC-5). The switch now also enforces SC-5's purpose at run time and in the guard.
* **SI-1**, mapping a busy gate on the set-password action to a 409, **remains deferred**. No other informational item was acted on.

### 15.8 Evidence

* **Archive:** `G:\Backup\Apperture-Core-worktree-evidence\2026-11-09\wt-iam10-2b-wp4bc-services-sr1-evidence`, copied from `artifacts/sr1-logs` after a secret scan, with a `SHA256SUMS.txt` (CRLF, backslash paths: verify with PowerShell `Get-FileHash`) verified against the copy. Its file count and manifest SHA-256 are in the hand-over message. The review's own archive and the service package's archive (section 14) are unchanged.
* **Bundle:** `G:\Backup\Apperture-Core-git-bundles-integration\feat-iam10-2b-wp4bc-services-<head>-sr1.bundle`, the branch at its final commit, with its SHA-256 in `SHA256SUMS-2026-11-09-services-sr1.txt` in that folder and in the hand-over message. It carries no `HEAD`, so clone it with `git clone -b feat/iam10-2b-wp4bc-services`.
* **Working copy:** `artifacts/sr1-logs` (tools, mutation, rp1, guard-probe, final, honesty, trx, commit messages), builds in `artifacts/sr1-dev` and `artifacts/sr1-release`, the clone in `artifacts/sr1-mut/clone`. Nothing was cleaned up.
