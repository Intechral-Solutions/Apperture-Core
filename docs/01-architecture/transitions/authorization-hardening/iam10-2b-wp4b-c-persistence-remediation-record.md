# IAM-10.2b WP4B-C persistence remediation: Foundation 1D findings B-1, R-1, R-2 and R-3

| | |
|---|---|
| Package | Targeted security remediation of the [persistence integration](iam10-2b-wp4b-c-persistence-integration-record.md) after its independent review returned **NOT ACCEPTED** at `072b7e940e0a68347c5567a8e4167cd90b59b560` (2026-11-08). |
| Branch | `integration/iam10-2b-wp4bc-persistence` (worktree `wt-iam10-2b-persistence-int`), local only: not pushed, no PR, the protected IAM branch `feat/iam-10.2b-administrator-viability` not advanced (still `c3e9813a`). |
| Status | **Remediated and locally validated by the implementer. Not independently verified.** Closure of the findings is the reviewer's. |
| Not in scope | The WP4B-C service reducers and CLI, the service-entry obligations (section 7), a recovery-code rewrite, Configuration C, the streamlined E.7 review procedure, development-environment work, credential rotation, tooling repairs. |

## 1. Identity

| | |
|---|---|
| Reviewed commit | `072b7e940e0a68347c5567a8e4167cd90b59b560`, `src` `47e2a54333f4388254f169d13bc0ef7e9fa48b2f` |
| Fix commit | `c8200a4980d998fe29bf83270791d5fe9095a42d`: B-1 and R-1 (source and tests) |
| Rollback note | `9318fc369f99d7cfc8575f05b8b5ad38acdc2bfd`: `deploy/README.md` only (R-3) |
| **Validated code commit** | **`c8bc00e2835318365837ef4314e4ce20c607b57d`**: two further R-1 cases (tests only); `src` `2b7705a6035ff302c864e5cba0ad472b186454c9` |
| Later commits | This record and the corrections of the earlier records (documentation only). The full SHA of the commit that holds this record is in the hand-over message; `src` and `tests` must equal those of `c8bc00e2`. |

Preparation: the worktree was verified clean at `072b7e94`, no other process used it, and the review evidence on G: and the reviewer's clones were read and left unchanged. The accepted foundation (`cfe3e702`), the 1D branch (`859de93f`), their worktrees and evidence, and the bundles on G: were not modified.

## 2. B-1: replay of an accepted TOTP code (blocking)

**Defect (review section 4).** `NativeMfaService.VerifyTotpAsync` read the credential without a lock, checked the code against it, then called `UpdateLastUsedAsync(user, step, at)`, which re-read the row by user and wrote the step unconditionally. A sign-in that checked a code of the old secret could finish after a switch committed, overwrite the new credential's step with a lower one, and so reopen the code that had verified the candidate; two concurrent sign-ins with one code could both succeed (pre-existing).

**Correction.**
- `INativeTotpCredentialRepository.UpdateLastUsedAsync(UserId userId, string expectedCredentialId, long matchedStep, DateTimeOffset verifiedAtUtc, CancellationToken)` now returns `ValueTask<bool>`.
- `SqlNativeTotpCredentialRepository` implements it as **one** `ExecuteUpdateAsync` with the condition `UserId = @user AND IsEnrolled = 1 AND CredentialId = @expected AND (LastUsedStep IS NULL OR LastUsedStep < @step)`; it sets `LastUsedStep` and `LastVerifiedAtUtc` (the row version advances with it) and returns whether one row changed. An `UPDATE` evaluates its condition on the committed row under its update lock in both isolation modes, so a concurrent writer is waited for and re-checked, never overwritten (proved by the held-lock case below).
- `VerifyTotpAsync` passes the `CredentialId` read with the secret it checked and returns the write's result: a valid code whose step write changed nothing does not sign in.
- Callers traced: the only production caller is `VerifyTotpAsync` (used by the sign-in in `AuthController`); no other writer of `LastUsedStep` exists except the switch (which records the matched step in its own conditional statement) and the conditional enrollment. The unit-test fake mirrors the four conditions; the probe decorator and the new test decorator forward the result. The writer-coverage guard pattern `_totp.UpdateLastUsedAsync(` still matches.

**Tests (permanent, real SQL, both isolation modes): `NativeTotpReplayProtectionSqlServerTests`, 22 cases.**

| Requirement | Case(s) | Origin |
|---|---|---|
| 1. An old-credential sign-in cannot finish after a replacement commits | `A_sign_in_with_the_retired_authenticator_does_not_complete_after_the_switch…`, `…at_a_step_newer_than_the_switch_does_not_complete_and_the_new_one_still_signs_in` | F2b; the second isolates the identity condition (stale step above the switched one) |
| 2. An old-credential sign-in cannot overwrite the new replay counter | `A_sign_in_that_read_the_replaced_credential_cannot_lower_the_step_the_switch_recorded`, `…cannot_record_even_a_newer_step_on_the_new_one` | F2a, plus the identity-only variant |
| 3. The candidate's verification code is not accepted again after the switch | the F2b case (replay after the stale sign-in) and `After_a_switch_the_code_that_verified_the_candidate_is_not_accepted_as_a_sign_in` | F2b |
| 4. Two concurrent sign-ins with one step cannot both succeed | `Of_two_sign_ins_with_one_code_that_both_read_the_credential_before_either_wrote_exactly_one_succeeds` (stale-read order), `A_sign_in_that_waits_for_another_sign_in_recording_the_same_step_fails_once_that_one_commits` (the first write held uncommitted until the server reports the second waiting) | F2c, plus the held-lock order |
| 5. A step at or below the stored step is rejected | `A_step_at_or_below_the_recorded_step_is_refused_and_changes_nothing` | new |
| 6. A newer step succeeds against the correct credential | `A_newer_step_of_the_enrolled_credential_signs_in_and_is_recorded` | new |
| Enrolled condition alone | `A_pending_credential_never_records_a_sign_in_step` | new |
| Owner condition alone | `A_step_write_changes_only_its_own_user_even_where_credential_identities_coincide` | new |

The interleavings are exact (a decorator of the real repository runs the switch between the sign-in's read and its write; task completion sources order the two sign-ins; the held-lock case polls the dynamic management views for the waiting session). No case sleeps for a fixed time or simulates SQL. Unit level (`NativeMfaServiceTests`, 4 new cases): the service presents the identity it checked and the matched step; a valid code whose write changes nothing fails; a switch between check and write fails the sign-in and records nothing; the second of two sign-ins fails.

## 3. R-1: candidate lock order and the missing index

**Defect (review section 5).** `RemoveCandidatesAsync` (used by the credential delete, `ReplaceAllAsync` and `DeleteAllAsync`) and `DiscardAsync` deleted the user's candidate rows without taking the credential row, so a staging holding the credential row and a history row and a deleter holding the live row formed a cycle (same user). The only index on `UserId` was filtered, so user-scoped deletes scanned the clustered index and waited for other users' rows (cross user).

**Correction.**
- **Lock order.** `RemoveCandidatesAsync` first reads the user's credential row with `UPDLOCK, HOLDLOCK` (whether pending or enrolled), then deletes the candidates. `DiscardAsync` now calls it, so every candidate removal follows credential, then candidates, then codes. It joins the caller's transaction; with none, both statements run in one transaction of their own (so the lock is held until the delete; without that, an autocommit lock would be released at once). **Absent credential row:** the serializable read locks the key where the row would be, which is also what a staging, verification or switch of that user takes first (and they refuse with `NoActiveEnrollment`), so the order still holds and the removal still runs: no cleanup path is skipped. Candidate ownership, version and expiry conditions are unchanged.
- **Index.** The unshipped migration `20261108120000_Iam102b_MfaReplacementCandidates` creates the unfiltered `IX_UserTotpReplacementCandidates_UserId`; the model (`HasIndex(x => x.UserId, "IX_UserTotpReplacementCandidates_UserId")`, named because the filtered index is on the same column) and the snapshot agree (`HasPendingModelChanges` passes).
- **FORCESEEK (beyond the reviewer's suggestion; recorded honestly).** With the index alone, the first run of the new cases still deadlocked in all 16 cross-user cases: the server reported the removal `DELETE … WHERE [UserId] = @p0` waiting `LCK_M_U` on another user's key, because on a small table the optimizer chooses a clustered scan over a seek plus lookup. The three user-scoped candidate statements (staging's history purge and supersede, and the removal) are therefore written `… FROM [UserTotpReplacementCandidates] AS c WITH (FORCESEEK) WHERE c.[UserId] = …`, which forces a seek on the owner indexes and fails loudly (error 8622) if no such index exists. Statements keyed by `CandidateId` already seek the primary key.
- Not changed: the redundant second candidate removal in `ResetAsync` (optional in the review; harmless), the switch, verification, staging's lock and conditions.

**Tests (permanent, real SQL, both isolation modes): `MfaReplacementCandidateLockOrderSqlServerTests`, 86 cases**, on contexts **without retry** (a 1205 surfaces to the test) and with the production retry counting its deadlock victims; both must be zero. One side is parked by a statement interceptor while it holds its locks; the other runs until it finishes or the server reports it waiting for a lock (polled from `sys.dm_exec_requests`, never a fixed sleep); then the first is released, and the end state must be a serial outcome.

| Case | Count | Asserts |
|---|---|---|
| F1a: staging parked after its purge vs regeneration, reset, code removal, discard of the same user, live key sorting first or second | 32 | no 1205 and no retried victim; the deleter waited for the credential row; staging applied, then the deleter removed every candidate; codes per deleter |
| F1b: staging of user 1 vs each of the four deleters of user 2 | 16 | no deadlock; user 2's deleter **finished while user 1's staging was parked** (no cross-user wait); both users' end states |
| F1c: switch parked after its update vs the four deleters (both key orders) | 32 | no deadlock; the deleter waited; serial outcome (a reset that read before the switch is refused as a concurrency conflict and changes nothing, as before) |
| Two stagings of different users | 4 | no deadlock; the second finished while the first was parked (pins staging's own FORCESEEK) |
| A discard outside any transaction, parked between its credential lock and its delete | 2 | a same-user staging waits; serial end state (pins the removal's own transaction) |

`MfaReplacementCandidateConcurrencySqlServerTests`: the one existing case that claimed "without deadlock" now counts the retried deadlock victims of both sides and asserts zero, and the class comment says what its retrying contexts can and cannot show.

## 4. R-2: evidence and records

- The [Foundation 1D record](iam10-2b-wp4b-c-foundation-1d-record.md) keeps acceptance criterion 4 and the lock-order line as written, each followed by a dated correction: the "no deadlock" claim was false, and the cited cases could not observe a retried deadlock. Its review section records the outcome.
- The [integration record](iam10-2b-wp4b-c-persistence-integration-record.md) gains the review outcome in its header, a correction after the concurrency paragraph, a corrected-severity row in section 9 (F-2 is a demonstrated authentication vulnerability, High and blocking; F-1 is a demonstrated deadlock, Medium), a correction of the "one host version per instance" statement (R-3), the superseded limitation in section 13, and the service-entry obligations of the review (section 7 below).
- **Original failures versus results after remediation.** The reviewer's probe failed 26 of 78 cases at `072b7e94` (F2a, F2b, F2c and the F1a/F1b deadlock expectations, both modes); those results stand in the review evidence and are not repeated or rewritten here. After remediation the same probe (adapted in three mechanical places, section 6.2) passes 78 of 78, and the permanent cases above pass (section 5). The original defects restored on top of the remediated tree (mutants N06 and N12) make the permanent cases fail again (section 6), which is the evidence that they detect what the review found.

## 5. Validation

Environment: Windows 11, PowerShell 7, .NET SDK 10, SQL Server 2022 (16.0.4250.1) container `apperture-test-sql` at `127.0.0.1,1433`, connection string only in the child process environment (never on a command line or in a log). Build protocol `--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers`, fresh repo-local `--artifacts-path artifacts/rem-build`, Release.

| Run | Commit | Result |
|---|---|---|
| Clean solution build (`Apperture.slnx`) | `c8bc00e2`, clean tree | succeeded, 0 errors, 73 warnings (the same count as the review build), 7 min 47 s |
| UnitTests (writer-coverage guard, catalog and contract tests, model consistency, SQL source guards, `NativeMfaServiceTests`) | `c8bc00e2` | **6155 passed, 0 failed, 1 skipped** (the known opt-in diagnostic); +4 new cases against 6151 |
| ArchTests | `c8bc00e2` | 38 passed |
| **Focused real-SQL set** (the integration's 1026-case filter plus every MFA replacement, replay and lock-order class) | `c8bc00e2` | **1156 passed, 0 failed, 0 skipped**, 32 min 44 s: `rcsi: False` 557, `rcsi: True` 557, 42 not parameterised |
| **SQL execution-honesty script**, built by the script (not `-NoBuild`), Release, enforce mode, run in the clean clone at the validated commit (tree `756fc1de236d332461ea841d626949790894b215`) | `c8bc00e2` | **PASS, exit 0, authoritative**: 3778 discovered, 3778 executed, 227 passed (all on the allow-list), 3551 failed as expected without SQL (all "unavailable"), 0 skipped, no unexpected pass, no must-fail violation. The allow-list (227, SHA-256 `8798D5A9…442E`) and must-fail list (188, `17D8A73D…73E3`) are byte-identical to the integration's run, so no list change was needed. The 109 more discovered cases are exactly the new SQL cases (22 + 86 + 1). The one more pass is `IntegrationTestSecretProtectorTests.Factory_replaces_the_production_protector…`, an allow-listed case that the integration run tolerated under its declared condition. Assembly SHA-256 `2242B9A817DB586DFA2865135BEC6C5AD9C13B6E26590196A39FDCAEE7D8232C`, script SHA-256 `A75F53F6…E513`, 3 min 6 s |

The focused set per class (all passed): AdministratorViabilityAttempt 50, AdministratorViability 21, AdministratorViabilityWp2 304, AuthenticationAudit 4, ConditionalMfaEnrollment 12, EntraPrimaryPolicyViability 88, EntraRegistrationDiagnosticsMigration 5, EntraRegistrationViabilityWp3 306, HostConfigurationAttestation 60, IdentityGovernanceAudit 18, ManagementHostAttestation 8, ManagementHostStartupOrder 10, MfaReplacementCandidateConcurrency 16, MfaReplacementCandidateContract 12, **MfaReplacementCandidateLockOrder 86 (new)**, MfaReplacementCandidateMigration 4 (one new), MfaReplacementCandidateRoundTrip 1, MfaReplacementCandidate 56, MfaReplacementViabilityIntegration 8, **NativeTotpReplayProtection 22 (new)**, PendingMfaEnrollment 14, RecoveryAccessDesignation 32, RoleAuthorizationDataStep 19. The previously accepted foundation classes (viability, attestation, policy, registration) give the same counts as in the integration record's focused set, all passing.

## 6. Negative controls

### 6.1 Mutation run on the validated commit

The run used a clone of `c8bc00e2` in the repo-local `artifacts/rem-mut/clone`, built with `-m:1 -p:UseSharedCompilation=false --disable-build-servers` into its own artifacts path.
- **Baseline first, green.** The replay, lock-order, migration and round-trip classes passed 113/113, and `NativeMfaServiceTests` 14/14.
- **Twelve mutants.** Each is a set of exact single-match text edits. Each was built, run against its filter, and restored. The restore was verified against the committed blob hash, and the clone was clean at the end.
- **Edits recorded.** The exact edits are in `mutation/mutants.json`, with a `.diff` per mutant. N06 and N12 restore the reviewed behaviour on top of the remediated tree, so they show that the permanent cases fail on the defects the review found.

| Id | Removed property | Result | Failing cases (both modes unless stated) |
|---|---|---|---|
| N01 | step write: credential identity | **Caught** | 4: the identity-only cases (stale newer step through the repository and through the service) |
| N02 | step write: strictly newer step | **Caught** | 5: both F2c orders (stale read 2; held lock 1, under read committed snapshot, because under locking read committed the waiting sign-in's read already sees the committed step), step at or below 2 |
| N03 | step write: enrolled state | **Caught** | 2: pending credential |
| N04 | step write: owner | **Caught** | 2: coinciding identities |
| N05 | service ignores the write's result | **Caught** | unit 3; SQL 7 (F2b, identity through the service, both F2c orders) |
| **N06** | **B-1 as reviewed**: unconditional write by user, result ignored | **Caught** | unit 3; SQL **15 of 22**: F2a, F2b, F2c and the condition cases |
| N07 | removal takes no credential row | **Caught** | 14: the 12 F1a cases with the live key first for regeneration, code removal and discard (both modes, with and without retry), **each a 1205 or a retried victim**, which is the pattern the review demonstrated; and the 2 standalone-discard cases |
| N08 | removal without FORCESEEK (index kept) | **Caught** | 16: every F1b case. User 2's `DELETE` waited `LCK_M_U` on user 1's key. It formed no cycle, because staging still seeks |
| N09 | staging purge and supersede without FORCESEEK | **Caught** | 4: two stagings of different users, each a 1205 or a retried victim |
| N10 | migration without the unfiltered index | **Caught** | 87 of 91: every removal fails with "could not produce a query plan because of the hints" (error 8622), and the migration's index assertion fails |
| N11 | removal outside a caller transaction as two autocommit statements | **Caught** | 2: the standalone discard no longer keeps a staging waiting |
| **N12** | **R-1 as reviewed**: no credential lock, no index, no FORCESEEK | **Caught** | **34 of 86, 32 with a 1205 or a retried victim**: 12 F1a, 16 F1b, 4 stage against stage; and 2 standalone discard |

12 of 12 caught; none escaped, none equivalent. The non-retrying cases saw each deadlock as SQL error 1205 raised to the test. The retrying cases saw it as a victim counted by the production strategy.

### 6.2 The reviewer's probe against the remediated code

The reviewer's file `ReviewerProbeMfaSqlServerTests.cs` was read from `F:\Code\Apperture\review-wp4bc-probe-072b7e9` and left unchanged there.
- **Adaptation.** Three mechanical edits let it compile and park against the new code; `probe/adaptation.diff` holds them.
  - The decorator uses the new `UpdateLastUsedAsync` signature and forwards its result.
  - F2a's stale write presents `stale.CredentialId`, the identity it read.
  - The staging pause matches the new purge text.
- **Unchanged.** No assertion, interleaving or expectation was changed.
- **Run.** The adapted file was placed untracked in the clone at `c8bc00e2`, run, and deleted; the clone was clean afterwards.
- **Result.** **78 passed, 0 failed** (`probe/probe-remediated.trx`): F2a, F2b, F2c, F1a, F1b and F1c, in both modes, with and without retry. At `072b7e94` the same probe gave 52 passed and 26 failed; that review evidence is unchanged.

## 7. Service-entry obligations (unchanged, carried forward)

D-1 to D-3 (decision record, section 15), U1 to U7 (integration record, section 10) and S-1 to S-5 from the independent review (integration record, section 10, "Service-entry obligations from the independent review") remain mandatory for the service package; none is implemented here, and B-1 needed none of them. U3 is updated: the TOTP step write is no longer a tracked update and F-1's 1205 is no longer expected. The concurrent acceptance of one recovery code is recorded there for targeted assessment; it is not a demonstrated finding and was deliberately not changed.

## 8. R-3: rollback

Owner direction: a clear rollback contract instead of a column default for older binaries. No binding release requirement for program-only rollback was found (the RC1 gate and the IAM-10.2b decision records state none; AUD-1.1 and IAM-10 P-19 chose older-binary compatibility for their own changes only). So no default was added, and:
- `deploy/README.md`, "Authenticator records after upgrading": program-only rollback across this migration is unsupported; back up before the first start; before an earlier version starts, stop every host and either restore the pre-upgrade backup (backing up the current database first) or downgrade the schema by reverting the migration (backup first; authenticator records only, not a full rollback); upgrading again issues new identities. It states that no supported production upgrade or rollback procedure exists yet.
- Tested behaviour (migration suite): `Down` drops the table and the column and keeps every credential value; `Up` again issues new identities (existing case and the round-trip case); new: on the upgraded schema an earlier build's first-enrollment insert is refused with error 515 and writes nothing, and after the downgrade the same insert succeeds; the unfiltered index exists with `UserId` as its only key.

## 9. Evidence

The review's own evidence is untouched: `G:\Backup\Apperture-Core-worktree-evidence\2026-11-08\review-wp4bc-persistence-072b7e9` and the reviewer's two clones. This package's evidence is copied from the ignored `artifacts\rem-logs` of the worktree to `G:\Backup\Apperture-Core-worktree-evidence\2026-11-09\wt-iam10-2b-persistence-remediation-evidence`. The folder is restricted to the owner, SYSTEM and Administrators, and has a `SHA256SUMS.txt` (CRLF, backslash paths; verify with PowerShell `Get-FileHash`). It contains:
- the clean-build log and its provenance;
- the UnitTests, ArchTests and focused SQL TRX files, with the filter;
- the development runs, including `dev1`, the first run with the index alone and its 16 cross-user deadlocks;
- the mutation run: `mutants.json`, `results.json`, `baseline.json`, and the diff, build log and TRX of each mutant;
- the adapted reviewer probe, its adaptation diff and TRX;
- the honesty report, TRX and logs;
- the helper scripts: the SQL runner, the mutation harness and the probe adapter.

A verified recovery bundle of the branch at its final commit goes to `G:\Backup\Apperture-Core-git-bundles-integration\integration-iam10-2b-wp4bc-persistence-<head>.bundle`. Its hash is in `SHA256SUMS-2026-11-09-remediation.txt` there and in the hand-over message. Before archiving, the evidence is scanned for the test connection string and its password.

## 10. Scope for closure verification

The reviewer is asked to verify at the new exact commit:
- **B-1:** the conditional step write and the service's use of its result (section 2).
- **R-1:** the lock order of every candidate removal, the index, and the `FORCESEEK` hints, including the treatment of an absent credential row (section 3).
- **R-2:** the record corrections (section 4).
- **R-3:** the rollback note (section 8).
- **The evidence:** that the permanent cases fail on the reviewed behaviour (N06, N12) and pass after remediation, in both isolation modes.

Foundation 1A to 1C and the accepted parts of 1D are not reopened. Their source is unchanged except for the two repository methods, the candidate removal and the staging statements named here.

## 11. Limits

- Linux not run. The full SQL assembly (about 2 hours) was not run; the focused set and the honesty script stand in.
- `FORCESEEK` fixes the plan shape on SQL Server; it was verified on SQL Server 2022 in both isolation modes, not on Azure SQL.
- The cross-user guarantee covers the candidate table. Recovery-code deletes by the switch (`WHERE UserId`) use the existing unfiltered `UserId` index of that table without a hint; the review observed no deadlock there and none was claimed.
