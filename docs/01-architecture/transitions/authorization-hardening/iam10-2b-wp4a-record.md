# IAM-10.2b WP4A: the Entra-primary policy under the administrator-set gate

| | |
|---|---|
| Package | IAM-10.2b, work package 4A ([WP0 reconciliation](iam10-2b-wp0-reconciliation.md) §7, the row marked WP4A; A10.2b.9, A10.2b.10), on the [WP1](iam10-2b-wp1-review-record.md) foundation, the [WP2](iam10-2b-wp2-review-record.md) writers and the [WP3](iam10-2b-wp3-record.md) registration writers and safe post-state rule. |
| Branch | `feat/iam-10.2b-administrator-viability`, from `ed79e0c`. Not pushed, not merged. |
| Status | Implemented and validated; independently reviewed (accepted with bounded remediation). The remediation of that review (M-1, the invalid mutation evidence; L-1, a misleading attempt row) is in the commit after `5be87db` and is recorded in §2.1, §8 and §9. Nothing here reopens a decision of the [design gate](iam10-design-gate.md) or a ruling D-1 to D-6. |
| Not in scope | WP4B (Recovery Access, MFA, bootstrap and command-line writers, F-1, D-6, R-2), WP5, IAM-10.4 (the UI's presentation of a refusal), the legacy deployment-secret fallback. |

## 1. What changed

After a user's Microsoft link is proven, the Entra-primary policy disables that user's Local credential. It used to decide in its own audited unit with its own predicate (a count of other role holders with an enabled credential, and the Recovery Access posture), without the global gate. That predicate disagreed with the shared definitions in four ways: it never asked whether the target's **own** Entra link is a way in (so a pending link, or a link for another directory, let the last administrator lose Local sign-in whenever Recovery Access was healthy); it could not see an unknown configuration (it never read the Local setting or the Microsoft reading); it read the Recovery Access posture through a different service than the viability reads, in different statements; and it did not serialize with the other writers, so two applications for two administrators could each see the other's credential still enabled (A10.2b.9).

It now decides with the one shared guard, under the one global gate. There is no second predicate in the file (a source-level test pins that).

**Order** (checked method by method by the writer guard): a lock-free read answers "nothing to disable" (§1.1); the audited unit opens and takes its operation lock; **the global administrator-set gate**; the target and its credential are read; the Recovery Access protection; the state before is captured (`CaptureBeforeAsync`); the credential is disabled; the state the change produced is read back in the same transaction and decided (`VerifyAfterAsync`, I-VIABLE and I-FALLBACK in both views, with the WP3 safe post-state rule); only then the success row is staged. A refusal declares the attempt a no-op, so the unit rolls the disable back, and the attempt row (if any) is written after the rollback on a context of its own. A rerun after a confirmed rollback is a new transaction and takes the gate again.

### 1.1 The lock-free exit

The policy runs on every successful Entra sign-in. In the steady state the credential is already disabled, or there is none, and both answers change nothing. Taking the global gate for them would queue every Entra sign-in of the instance behind every administrator change. So the policy first reads the credential without any lock and returns `AlreadyDisabled` or `NoLocalCredential` without the gate and without a unit. This read only ever answers "nothing to do", never "go ahead": a concurrent writer that enables the credential after it is met at the next sign-in, and the decision to disable is made inside the unit, under the gate. A decision of this kind opens no unit, so it writes no row (as before: those outcomes were already no-ops).

## 2. Outcomes

| Verdict of the guard | Outcome | Credential | Row |
|---|---|---|---|
| Allowed (including the WP3 safe post-state proof, and any user who holds no administrator role) | `Disabled` | disabled, hash unchanged | `credential.local.disabled_entra_primary` (success, in the unit) |
| I-FALLBACK violated, and the state read under the gate shows the user as an Entra-independent administrator in the violated view (§2.1) | `DeferredLastEntraIndependentPlatformAdministrator` (existing) | enabled | `credential.local.disable_deferred_last_entra_independent_admin` (attempt, after the rollback) |
| I-VIABLE violated, with the same condition (the target's Entra link is pending, for another directory, or not usable; healthy Recovery Access does not matter) | `DeferredLastViablePlatformAdministrator` (new) | enabled | the same attempt row (§5, ambiguity 1) |
| Either invariant violated, but the state read under the gate does not show the user as an Entra-independent administrator in the violated view: a concurrent change to administrator access, not the user, caused it (§2.1) | `DeferredConcurrentAdministratorAccessChange` (new, review L-1) | enabled | none; the caller logs a warning |
| Configuration unavailable (the Local or the Microsoft reading is not established, and no proven Local administrator remains) | `DeferredAdministratorViabilityUnavailable` (new) | enabled | none (§5, ambiguity 2); the caller logs a warning |
| The global gate is busy past its bound | `NotAppliedBusy` (new) | enabled | none; the caller logs a warning |
| The user is the designated Recovery Access account | `ProtectedEmergencyAccount` (existing) | enabled | `emergency.local.protected` (attempt) |
| Already disabled / no credential | `AlreadyDisabled` / `NoLocalCredential` (existing) | unchanged | none |

`NotAppliedBusy` is the only exception the policy turns into an outcome (`PlatformAdministratorSetBusyException`, around the unit). Infrastructure failures, a gate failure that is not contention, cancellation and an unknown audited outcome propagate exactly as before; a source-level test pins that the method has one `catch`.

**The target's Entra proof and tenant.** The shared definition counts a Microsoft way in only when the link is proven, its directory is the one the sign-in accepts in that view, and Microsoft sign-in is usable. The decision therefore never assumes a pending link is an alternative: with only a pending link, or a link for another directory, the credential is the last viable way in and is kept. A user with no administrator role contributes no fact to either view, so the guard sees an unchanged state and allows the change whatever the configuration says.

**Both views.** I-VIABLE and I-FALLBACK are applied to the Now view and the Restart view independently, as for every writer.

### 2.1 Attribution of a refusal (review L-1)

The verdict compares the state read under the gate before the write with the state read after it. A writer that is exempt from the gate, such as the consumption of the last Recovery Access recovery code at its sign-in (ruling C-14), or one not yet under it (WP4B), can change a viability input between the two reads. Example: Recovery Access is healthy, nobody is an Entra-independent ordinary administrator, and the policy is applying to a user who holds no role; the last recovery code is consumed after the first read. The second read finds Recovery Access unhealthy and no Entra-independent administrator, so I-FALLBACK reads as newly violated, and the policy, which must stay conservative, defers. Before the remediation it then wrote `credential.local.disable_deferred_last_entra_independent_admin` for that user, which was not true: the user never contributed to the Entra-independent set.

The decision is unchanged (the credential stays enabled, the unit rolls back, the gate, the lock order and the guard are as they were). Only what is said about it differs. A refusal is attributed to the user, and only then written as the attempt row, when

```
verdict.View is { } view && captured is not null && captured.For(view).EntraIndependentAdministrators.Contains(userId)
```

(`captured` is the state `CaptureBeforeAsync` read under the gate). Why this expresses it:

- *Genuine deferrals are always attributed.* Without an outside change, the two states differ only by this user's credential. I-FALLBACK is then violated only if Recovery Access is unhealthy in both and this user was the only Entra-independent administrator of the view; I-VIABLE only if this user was the only viable administrator, and a viable administrator who loses nothing but a Local credential has that credential as a usable method, which is what Entra-independent means. Both put the user in the captured set of the violated view. The existing cases that expect the row (pending link, other directory, Entra off, no Recovery Access, exhausted codes) pass unchanged.
- *The view is the violated one.* `Detect` names the view; the user must be Entra-independent in that view, not in the other.
- *Missing information is not attribution.* A verdict without a view, or a scope without a captured state, cannot establish the condition, so the refusal is not attributed (and cannot reach the row). An unavailable configuration never reaches this test: it keeps its own outcome and writes no row, as before.
- *A user who is also the last administrator keeps the row* even when the outside change happened too, because the captured state shows them as one (tested on real SQL).

Limitation, in the safe direction: two independent outside changes in one window, one of which is a genuine violation by the user, can be reported as the concurrent outcome when the guard names the other invariant first (`Detect` returns the viable-administrator violation before the fallback one). The credential stays enabled and the caller warns; the next successful Entra sign-in decides again on the state as it then is and writes the row if it is genuine.

## 3. Callers

Every production caller of `ApplyAfterEntraProvenAsync` (inventory unchanged from the integrated baseline): `SqlPrincipalToUserResolver` (proof of a pending link at sign-in), and `EntraIdentityAssociationService` twice (every successful Entra sign-in with a proven link, and a self-link). The policy is best-effort in all three: the sign-in or association succeeds whatever the outcome.

One shared place (`EntraPrimaryOutcomeLogging`) says what a caller says: `NotAppliedBusy`, `DeferredAdministratorViabilityUnavailable` and `DeferredConcurrentAdministratorAccessChange` are logged as warnings with the user id and a non-sensitive reason ("another change to administrator access is in progress", "the sign-in configuration could not be established", "administrator access changed while it was being applied, so whether disabling local sign-in is safe was not established"), and the policy runs again at the next Entra sign-in. The third says nothing about a last administrator, because that is not what was found. Every other outcome is already recorded (by the change, by an attempt row) or needs no record, so the caller adds nothing and the policy logs nothing. An unknown audited outcome keeps its own error with the operation id, in the callers, as before.

## 4. The presentation

`DescribeAsync` (Account Security and user administration) computed the recovery safeguard with the old count-based predicate. It now asks the shared definitions what the policy would do: the state is read as it is (a display takes no gate), the post-state of disabling that user's credential is derived by `AdministratorViabilitySimulation` with the same evaluator, and the same guard decides. The safeguard is shown exactly when the policy would keep the credential, including the case the old predicate got wrong (a proven link for another directory with healthy Recovery Access). A state that cannot be established is shown as kept, because the policy would not disable it either. Tests compare the presentation with what the policy then does, on unit fakes and on real SQL.

## 5. Audit contract: ambiguities for an owner decision

No audit action, catalog entry or schema was added.

1. **One attempt row for two invariants.** `credential.local.disable_deferred_last_entra_independent_admin` is written for an I-FALLBACK deferral and for an I-VIABLE deferral. It is true in both: an I-VIABLE violation means the target's Local credential is its only way in, so the target is the last Entra-independent administrator. But the row carries only the user id; it does not say which invariant was decided, and an I-VIABLE deferral with healthy Recovery Access (which the old policy would have disabled) reads like an I-FALLBACK one. The outcome the caller receives distinguishes them; the audit trail does not.
2. **No row for an unavailable configuration or a busy gate.** Nothing is proven about the administrators in either case, so no last-administrator row is written, and the only record is the caller's warning. A deferral that cannot be seen in the audit trail may be preferred as an attempt row with its own action.
3. **The success row's posture flag** (`recoveryAccessHealthy`) now reports the Recovery Access posture of the state read under the gate, not a separate read.

*Review L-1 (owner ruling, no new audit action).* The established attempt row is kept for genuine I-VIABLE and I-FALLBACK deferrals and is no longer written when the state captured under the gate does not show the user as an Entra-independent administrator in the violated view (§2.1). That case is a distinct outcome whose only record is the caller's warning, like the busy and unavailable ones. The three open questions above remain the owner's.

The recommended direction, if the owner wants the trail to carry them: one new attempt action with a reason field (`last_viable`, `last_entra_independent`, `configuration_unavailable`, `busy`), added by a separate audit-catalog change. This package does not make that decision.

## 6. Writer coverage

The writer guard now lists the policy as integrated (WP4A): the order above is pinned method by method, the single `SetDisabledAsync` of the file is inside that method, the old count and posture predicate (`ListUserIdsWithRoleAsync`, `GetPostureAsync`, `IEmergencyAccessService`, `IRoleRepository`) is gone from the file, `DescribeAsync` takes no gate, and the method has a single `catch`. WP4B stays pending (bootstrap, first administrator, recovery, Recovery Access provisioning and designation, MFA reset and enrolment, the command line, F-1). E-1, E-2 and C-14 are unchanged and still pinned.

## 7. Remaining and deferred

WP4B (mandatory: F-1, D-6 before it, R-2), IAM-10.4 (how the UI presents a deferral), WP5, the audit decision of §5, the legacy deployment-secret fallback (WP3 §5).

**Findings of the independent review that are recorded and not implemented here** (the remediation touched the attribution and the evidence only). A deadline below is a gate in the program, not a calendar date; the program owner may move an item by ruling.

| # | Finding | Assigned to | Deadline |
|---|---|---|---|
| D-A | Entra-primary policy against registration activation and switch: the WP3 pair matrix covers registration delete and disable against the policy, not activate or switch. | WP5 (pair matrix) | A pair-matrix case for activate and for switch, both isolation modes, before the WP5 closure record. |
| D-B | Two applications of the policy for the same user at once (two sign-ins, or a sign-in and a self-link): by design the second is serialized by the gate and then finds the credential disabled, but none of the tests of this record pairs them. | WP5 (pair matrix) | A same-user case, both isolation modes, before the WP5 closure record. |
| D-C | Contention on the global gate by the policy: every Entra sign-in that still has a Local credential to disable queues behind administrator writers, a busy gate gives `NotAppliedBusy` and the policy repeats at every sign-in, so a busy instance repeats warnings. Measure the deferral and warning volume, and decide whether the warning needs a rate limit or a counter. | WP5, with the owner's decision on a counter | A measurement and a ruling before the WP5 closure record. |
| D-D | Operational visibility of a persistent unavailable-configuration outcome: it leaves no audit row (§5, ambiguity 2) and the only record is a warning per sign-in. | WP5, together with D-C and the §5 owner decision | The same ruling as D-C. |
| D-E | Multiple Entra links on one user: `DescribeAsync` shows the state of the first Entra link only (`DescribeEntra`), while enforcement counts every proven link of the accepted directory. The presentation can disagree with the decision for such a user. | IAM-10.4 (presentation); WP5 records that enforcement does not use the presentation | IAM-10.4 design review, before the UI shows the safeguard; WP5 closure states it as a known limitation until then. |

The earlier rulings stand: a busy gate and an unavailable configuration do not need a new durable audit event. Known until WP4B lands, and unchanged by this package: the writers that do not yet take the gate (bootstrap, Recovery Access provisioning and designation, MFA reset and enrolment, the command line) can change a viability input between a reducer's two reads; a reducer compares the whole system, so the effect is a refusal or a deferral in the safe direction at worst.

## 8. Evidence

**Source and provenance.** One commit on top of `ed79e0c`. Tree hashes of the final tree (`git rev-parse <commit>:src <commit>:tests`): `src` = `23ffb61d476def3cae25dd91792fc3cf0ecfb1c4`, `tests` = `75f941ad97ed2a0542b126aed643863d3276ca05` (before: `dd3a3e05bfcddf75e1da311a4c6745cddff3055d` and `3e2a8252aea044b0dc3ef6d8ea159cfface8f001`). The binaries were built from the staged tree with the working tree otherwise unchanged (documentation aside), in a new output directory. .NET SDK 10.0.402.

**Clean builds** (a new output directory, `--no-incremental`, from the repository root), both 0 errors:

```
dotnet build tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --artifacts-path artifacts/wp4a-final --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo
dotnet build tests/Apperture.UnitTests/Apperture.UnitTests.csproj             --artifacts-path artifacts/wp4a-final --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo
```

Integration 4 min 5 s (48 warnings), unit 4 min 41 s (91 warnings). Tests ran with `dotnet test … --artifacts-path artifacts/wp4a-final --no-build --logger trx` against those outputs, the unit project whole and the integration project filtered to the 27 classes described under the isolation modes.

| Run | Result |
|---|---|
| Whole unit project | 5720 passed, 0 failed, 0 skipped (5681 before) |
| Focused integration set | 1173 passed, 0 failed, 0 skipped (1095 for the 26 classes of the WP3 remediation, plus the 78 cases of the new class) |

**Isolation modes**, read from the trx names: 498 cases carry `rcsi: False` (locking READ COMMITTED) and all passed; 498 carry `rcsi: True` (READ_COMMITTED_SNAPSHOT) and all passed; the other 177 take no isolation parameter (host suites and audit suites on a catalog of their own). The new class `EntraPrimaryPolicyViabilitySqlServerTests` is 78 cases, 39 and 39. The focused set is the 26 classes of the [WP3 record](iam10-2b-wp3-record.md) §10 plus the new class; it includes the Entra association, principal-resolution and Management Host suites that call the policy through the real composition (`ManagementHostEntraLinkingHttpTests`, `ManagementHostSessionIdentityHttpTests`, and the others of that record). `EntraGovernanceAuditSqlServerTests` (the policy's earlier audited-unit cases) was moved to the new constructor and passes unchanged in what it asserts.

**What the new cases cover.**

- *Sequential, both modes, real SQL.* The last Entra-independent administrator is deferred (I-FALLBACK) and nothing changes: the credential stays enabled, the hash is unchanged, no success row, one attempt row. Healthy Recovery Access lets an administrator with a usable Entra link lose Local sign-in (the success row records the posture). A pending link, a proven link for another directory and no active registration each make the credential the last viable way in and defer it even with healthy Recovery Access (I-VIABLE). Role holders who cannot sign in (a disabled credential, no Local link) are not the administrator who remains. A second administrator with a Local credential and link lets the first be disabled on a later application. Exhausting the recovery codes defers; restoring one disables. Recovery Access is never disabled (an attempt row). An unknown Microsoft reading defers the last Local administrator as unavailable (no row), and does not stop a change that leaves a proven Local administrator (the WP3 proof); an unknown Local setting defers even then; an ordinary user is disabled whatever the configuration says.
- *Steady state.* An already-disabled, a missing and an unknown user's credential take neither the gate nor a unit (no transaction started, the gate acquired 0 times).
- *Busy.* A held gate gives `NotAppliedBusy` in one attempt with no success row and no deferral row, the credential enabled, then the next application disables it. Cancelling the wait is the caller's `OperationCanceledException`.
- *Audited unit (M6/W3).* An audit insert failure leaves the credential enabled and nothing recorded, then the policy succeeds; a lost acknowledgement is verified as committed (1 attempt, the gate acquired once, `Disabled`); a confirmed rollback reruns (2 attempts, the gate acquired twice, one success row); an unknown outcome throws `AuditOutcomeUnknownException`, is not retried and is not turned into busy or a deferral; a deferral is one attempt row by `system:entra-primary-policy`; a deferral row that cannot be written does not change the outcome.
- *Concurrency (A10.2b.9, A10.2b.10).* Two ordinary administrators, each with a Local route and a proven Entra link, no Recovery Access: the first application is parked after its change (holding the operation lock and the gate), the second is shown waiting on the application lock and on no row, then released; exactly one credential is disabled, the other deferred with its attempt row, and an Entra-independent administrator remains. Both target orders, both modes; the same two applied sequentially give the same result. The control with the gate removed: under row versioning both are disabled and nobody is left who signs in without Microsoft; under locking READ COMMITTED the second waits on a row (not the gate) and is then deferred.
- *Cross-package (ordered pairs, both modes, the first parked, the second shown waiting on the application lock).* The policy against each WP2 administrator writer that stops another administrator signing in locally (role removal, credential disable, Local link removal): whichever commits first, the other is refused (`entra_independent_fallback_required`) or deferred. The policy against each WP3 registration writer (delete, disable): with healthy Recovery Access and a second administrator who signs in only through Microsoft, the policy first makes the registration change refuse (`last_viable_sign_in_method`), and the registration change first makes the policy defer as the last viable credential.
- *Unit.* Every verdict kind to its outcome; tenant mismatch, pending link, Entra off; Recovery Access never counted; role holders who cannot sign in; unknown Local and Microsoft readings; the proof for a remaining Local administrator; a non-administrator; the order of reads and writes (the gate before the user and credential reads, the state read around the write); a busy gate, and that gate, cancellation, unknown-outcome and database failures are never turned into a busy outcome; a failure while recording leaves the credential enabled; the presentation agreeing with the policy. The three callers: a busy gate and an unavailable configuration are logged once as a warning with the user id and a reason, every other outcome is silent, and an unknown outcome keeps its error with the operation id.

**Mutation checks** (one line of source edited in the working tree, a separate output directory, the file restored from a byte-identical backup and its hash compared; each restored file's hash was identical). The runs were limited to the cases for the mutated behavior (unit: the policy, caller and writer-guard classes, 119 cases; SQL: the new class or the cases named).

| Mutation | Fails |
|---|---|
| The verdict is ignored (M1) | 14 unit and 45 of the 78 SQL cases |
| A busy gate is not turned into an outcome (M2) | 2 unit and both SQL busy cases |
| Every exception is turned into busy (M3) | 6 unit |
| No lock-free exit for a disabled credential (M4) | 1 unit |
| An unavailable configuration is recorded as a violation (M5) | 2 unit and 4 of 8 SQL cases |
| The viable-administrator deferral is reported as the fallback one (M6) | 4 unit and all 6 SQL cases |
| The resolver does not log a busy gate (M7) | 2 unit |
| The association service does not log a busy gate (M8) | 3 unit |
| The presentation never shows the safeguard (M9) | 3 unit |
| Recovery Access is not protected (M10) | 1 unit. **The SQL part of this row (two cases) is withdrawn as invalid; see §9.1 for the valid run.** |
| The coordinator no longer takes the gate (M11) | **Withdrawn as invalid: the four race cases did not reach the database. See §9.1 for the valid run.** |

**Corrections during the work.** The first run of the cross-package pair for a Local link removal failed because the arranged administrator had only that link, which the repository refuses to remove; the arrangement gives it a pending Entra link, as in WP3. A missing `using` in a test file. No product defect was found by the tests.

**Not executed, and limitations.** The full multi-hour integration suite and any Browser or UI test were not run (the feature-level pre-merge checkpoint). The policy's code was written before its tests in the same pass, so the unit cases were not seen failing first; their sensitivity is shown by the mutation checks above instead. The pause that holds the first writer is placed before the audit insert: the credential change and the verdict have been made, the commit has not. A deadlock at the secret read inside this unit is covered by the WP3 remediation for the shared read, not repeated here. The dedicated test SQL Server container (`apperture-test-sql`) was running. **Correction (§9.1):** this sentence, as written, was not true for the M10 SQL and M11 mutation runs of this section; the independent review found the container stopped for them, and the retained logs agree. The sentence is left as it was so that the error stays visible.

## 9. Remediation of the independent review

The independent review (Opus 5.5) accepted the package with bounded remediation and found no confirmed security-enforcement defect. This section is the record of the two findings it asked to have closed. It is one commit on top of `5be87db`, not amended into it.

| Finding | Resolution |
|---|---|
| **M-1**, the M10 SQL and M11 mutation evidence was invalid (the test SQL Server container was not running) | Withdrawn in §8 and replaced by valid runs, §9.1. |
| **L-1**, an exempt writer's concurrent change to Recovery Access can make the policy write a last-administrator attempt row that is not true of the user | Attribution added to the refusal; one new non-mutating outcome; no new audit action or schema field, §2.1 and §9.2. |

**Source and provenance.** Tree hashes (`git rev-parse <tree>:src <tree>:tests`, taken from the staged tree before the commit): `src` = `e9c6bbb6bd00352a00dee18236d0009017033283`, `tests` = `17b35a16525d677b3b33db274f121d5e56ffdeaa` (before, at `5be87db`: `23ffb61d476def3cae25dd91792fc3cf0ecfb1c4` and `75f941ad97ed2a0542b126aed643863d3276ca05`). Production source changed in three files: `EntraPrimaryAuthenticationPolicy.cs`, `EntraPrimaryOutcomeLogging.cs`, and the outcome enum in `IEntraPrimaryAuthenticationPolicy.cs`. The guard, the coordinator, the gate, the lock order, the evaluator, the simulation and every writer other than the policy are untouched. .NET SDK 10.0.402. Every mutated file was restored from a byte-identical backup, its hash compared, and after the last run the working tree matched the staged tree (git blob hashes of the three production files and the tree hash `c4e80522fadb54bc6990bb07941b6a82efb45bf4`, all equal to those before the first mutation).

### 9.1 M-1: the mutation evidence

**What was wrong.** The §8 rows for M10 (SQL part) and M11 were produced when the database was not reachable. The review found the SQL Server container stopped; at the start of this remediation `docker ps -a` showed `apperture-test-sql` as `Exited (137)`, and only that container was started (`docker start apperture-test-sql`). No container status had been captured for the original runs, so the cause is established by the review and by the signature in the retained logs (`artifacts/wp4a-logs/mutation-M10-*.txt`, `mutation-M11-*.txt`): the SQL cases of M10 failed as a group in 27 ms (2 cases) and those of M11 in 31 ms (4 cases), with the failures reported at the same instant, 7.41 to 7.43 s into the run; the valid runs below take 0.1 to 1.7 s per case, because a case creates and uses a catalog. The original harness also kept only the `[FAIL]` lines, not the failure messages, so those failures cannot be shown to be assertions. A failure to reach the database is not a detection, and the rows are withdrawn. The other SQL-bearing rows of §8 do not have this signature: in the same retained logs, M1 shows 33 of its 78 SQL cases passing in 27 s and M5 shows 4 of 8 passing, which cannot happen without the database, and the rerun below reproduces M1 (the same 45 SQL cases, plus the ten new ones), M2, M5 (through the equivalent M15) and M6 with the same failing cases.

**What the harness now keeps.** For each run: the commands, start and end times, the git HEAD, the SHA-256 of the file before, mutated and after, `docker inspect` of `apperture-test-sql` before and after each project and at the end (including `startedAt`, so a restart between would show), the full console output, and a trx whose every result is listed with outcome, duration and the first 230 characters of the message. A failure whose message looks like a connection, login, timeout or setup problem is flagged. Files: `artifacts/wp4a-rem-logs/` (`mutate.ps1`, `run-mutations.ps1`, `mutation-*.txt`, `*-console.log`, `trx-*`, `mutations-digest.txt`), kept with the earlier evidence, which was not touched.

**Availability of SQL.** `apperture-test-sql` was `running` with the same `startedAt` (2026-10-22T00:34:49Z, i.e. the start of this remediation) in every snapshot before and after every run: it did not stop or restart during any run recorded here. It is reached through the existing test configuration (the untracked `.env` of the test harness); no connection detail was put on a command line or in a log.

**Baseline.** The same harness, the same output directory, with the "mutation" replacing the text with itself (the file hash is unchanged): the M10 and M11 SQL cases and their controls, 16 SQL cases, all passed, at 0.16 to 1.31 s each; plus the M10 unit case, which passed.

**M10, Recovery Access is not protected** (`if (false && user.IsBreakGlassEligible)`, the occurrence in `ApplyAfterEntraProvenAsync`). Unit: 1 of 140 failed, `Designated_emergency_account_is_never_auto_disabled`. SQL: 2 of 6 failed, `The_recovery_access_account_is_never_disabled_and_the_protection_is_an_attempt_row` in both modes (252 ms and 416 ms), each with the assertion `Expected outcome to be ProtectedEmergencyAccount {value: 4}, but found Disabled {value: 2}`. The 4 controls passed in the same run (`The_last_entra_independent_administrator_is_deferred_by_I_FALLBACK_and_a_deferral_is_not_a_change` and `Healthy_recovery_access_satisfies_I_FALLBACK_so_an_administrator_with_a_usable_entra_link_is_disabled`, both modes). No failure was flagged as infrastructure.

**M11, the coordinator no longer takes the gate** (`await _gate.AcquireAsync(...)` replaced by `await Task.CompletedTask`). SQL: 4 of 10 failed, the four cases of `A9_two_applications_for_two_administrators_disable_exactly_one_and_defer_the_other` (first application X or Y, locking READ COMMITTED and READ_COMMITTED_SNAPSHOT), in 146 to 249 ms, all with the same assertion: `Expected ProbeGateAsync(p.Scratch) to be -1 because the first application holds the administrator-set gate, but found 0`. The 6 controls passed in the same run: `A9_the_same_two_applied_one_after_the_other_give_the_same_result` (4 cases, no overlap, needs no gate) and `Control_with_the_gate_removed_the_same_interleaving_is_not_serialized_and_under_row_versioning_disables_both` (2 cases, its own gate removal by injection, so unaffected by the mutation).

On the race under READ_COMMITTED_SNAPSHOT: the failure is at the first observable of a missing gate, measured in the database. The first application has opened its unit, taken its operation lock, read the state and written the credential (the pause is reached inside the unit's transaction; that is what the 146 to 249 ms are), and the application lock of the gate is free (`0`, not `-1`). The assertions after it (the second application waiting on the lock and on no row; exactly one credential disabled) are not reached, so the mutation is detected by the gate probe, not by the end state. The end state without the gate is shown by the control in the same run: under READ_COMMITTED_SNAPSHOT the second application is not held up, both credentials are disabled, and nobody is left who signs in without Microsoft; under locking READ COMMITTED the second application waits on a row, which is incidental protection and not the gate. Both pass in the baseline and in this run.

### 9.2 L-1: the attribution

The condition, the reasoning and the limitation are in §2.1. In the code:

- `EntraPrimaryAuthenticationPolicy.Deferred(verdict, captured, userId)` maps the verdict kind to its outcome as before; for the two kinds that would write the attempt row, it returns `DeferredConcurrentAdministratorAccessChange` unless `verdict.View is { } view && captured is not null && captured.For(view).EntraIndependentAdministrators.Contains(userId.Value)`. `ConfigurationUnavailable` keeps `DeferredAdministratorViabilityUnavailable` and never reaches the test. The attempt row is staged for exactly the two outcomes that mean "this user was the last administrator" and for no other.
- The unit still declares itself a no-op, so the proposed disable is rolled back, for every refusal; only the row and the outcome differ. The viability decision (the guard, `VerifyAfterAsync`) is not touched.
- `EntraPrimaryOutcomeLogging` logs the new outcome as one warning with the user id and the reason "administrator access changed while it was being applied, so whether disabling local sign-in is safe was not established. Local sign-in stays enabled and the policy is applied again at the next Entra sign-in." Both callers use it; the sign-in or the association succeeds whatever the outcome.
- The outcome is distinct from `NotAppliedBusy` (the gate was taken and a decision was made) and from `DeferredAdministratorViabilityUnavailable` (the configuration was established).

**Regression cases.** Unit (21 added; 5,741 in the project, 5,720 before):

- the policy, 5 cases and one 13-row table of verdicts: a user who holds no role is deferred with the new outcome, no row, the credential enabled and unchanged, one unit and one gate acquisition, when the last recovery code is consumed between the reads (with the same arrangement allowed when it is not); the same for the removal of the only viable administrator between the reads (the I-VIABLE branch); a user who is the only Entra-independent administrator keeps the row and the fallback outcome when the posture also changed; a pending-link administrator keeps the row and the viable outcome; and the table: the user in the violated view (both views, both invariants) is attributed, the user only in the other view or in neither is not, a viable user who is not Entra-independent is not, a verdict without a view or a scope without a captured state is not, and an unavailable configuration keeps its own outcome;
- the callers, 3 cases: the resolver and the association service (sign-in and self-link) log the new outcome once as a warning with the user id and no claim of a last administrator, and the sign-in or link still succeeds.

SQL, real Microsoft SQL Server, each in locking READ COMMITTED and READ_COMMITTED_SNAPSHOT (10 cases; the policy's class is now 88, 44 and 44): the application is parked before the write of the credential (it holds the operation lock and the gate; the probe shows the gate held) and then (1) the last Recovery Access recovery code is consumed by the real `NativeMfaService.ConsumeRecoveryCodeAsync` (a real hash, its own audited unit; it completes while the gate is held, as C-14 requires) for a user with no role: the policy resumes and returns the new outcome; the credential is enabled with its hash unchanged; no success row and no last-administrator row; the code's own event is recorded; Recovery Access is unhealthy afterwards; a later application disables the credential (one success row, no deferral row); (2) the same through the caller, the proof of a pending Entra link at sign-in: the session resolves to the user, the link is proven, the credential is enabled, no row, and exactly one warning (a warning, not an error) naming the user and the reason without a last-administrator claim; (3) the user is the only Entra-independent administrator and the code is consumed too: the established outcome and one attempt row; (4) the user's own pending Entra link makes their Local credential the only way in (I-VIABLE) and the code is consumed too: the viable outcome and one attempt row; (5) a writer not yet under the gate removes the only administrator who can sign in: the new outcome, no row.

The earlier genuine-deferral cases of the policy's class (I-FALLBACK, I-VIABLE with a pending link and with another directory, Entra off, exhausted codes, the two-administrator race, the cross-package pairs) pass unchanged in both modes and keep their attempt row.

### 9.3 Validation

Clean builds into a new output directory, `--no-incremental`, from the repository root, both 0 errors and the same warning counts as WP4A (48 and 91):

```
dotnet build tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --artifacts-path artifacts/wp4a-rem-final --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo
dotnet build tests/Apperture.UnitTests/Apperture.UnitTests.csproj             --artifacts-path artifacts/wp4a-rem-final --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo
```

Integration 4 min 39 s, unit 4 min 55 s. Tests ran with `--no-build` against those outputs.

| Run | Result |
|---|---|
| Whole unit project (includes the policy, the callers and the writer-coverage guard) | 5,741 passed, 0 failed, 0 skipped (5,720 before) |
| Focused integration set: the same 27 classes as WP4A | 1,183 passed, 0 failed, 0 skipped, 30 min 26 s (1,173 before; the 10 added cases are in the policy's class) |

Isolation modes, read from the trx names: 503 cases carry `rcsi: False` (locking READ COMMITTED) and 503 carry `rcsi: True` (READ_COMMITTED_SNAPSHOT), all passed; the other 177 take no isolation parameter. The set includes the WP2 and WP3 writer suites, the registration suites, the gate suite, and the host suites that call the policy through the real composition, so the narrowly affected regressions are covered by it. Not executed, as instructed: the full multi-hour integration suite and the Browser and UI suites.

### 9.4 Mutation checks (this remediation)

Same method as §9.1, every run with SQL running, with the controls named. The first rows are the withdrawn ones, rerun; the next five are new for L-1; the last four reproduce earlier rows whose source moved or whose SQL part is part of the decision.

| Mutation | Fails (unit; SQL) | Unaffected controls, which passed |
|---|---|---|
| M0, baseline (no change) | 0 of 1; 0 of 16 | 10 |
| M10, Recovery Access is not protected | 1 of 140; 2 of 6, both modes | 4 SQL |
| M11, the coordinator no longer takes the gate | -; 4 of 10, both modes and both orders | 6 SQL |
| M12, every refusal is attributed to the user (the behaviour before the remediation) | 6 of 53; 6 of 12 (the non-administrator, the sign-in and the removal case, both modes) | 2 SQL (the genuine deferral) and the 4 genuine L-1 cases |
| M13, no refusal is attributed to the user | 17 of 53; 6 of 12 (the genuine cases and the genuine control, both modes) | the 6 L-1 cases about a non-administrator |
| M14, the Now view decides whichever view was violated | 3 of 53; - | the rest of the policy's class |
| M15, the last-administrator row is written for every refusal (includes the old M5, the unavailable configuration recorded as a violation) | 4 of 53; 10 of 20 (the three non-administrator cases and 4 of the 8 unknown-configuration cases) | 2 SQL |
| M16, the callers say nothing about the new outcome | 3 of 140; 2 of 2 | the rest |
| M6, the viable-administrator deferral is reported as the fallback one | 7 of 140; 6 of 6 | the rest |
| M1, the verdict is ignored | 19 of 140; 55 of 88 (45 of the 78 of §8, plus the 10 new cases) | the other SQL cases |
| M2, a busy gate is not turned into an outcome | 2 of 140; 2 of 4 (both modes) | 2 SQL |

The M13 control named in the harness (the genuine I-FALLBACK deferral) is affected by that mutation by construction and failed, as expected; its unaffected controls are the six cases listed. No failure in any run was flagged as an infrastructure failure, and no failure message was a connection or setup problem. The failing messages are assertions, for example M12: `Expected outcome to be DeferredConcurrentAdministratorAccessChange {value: 8}, but found DeferredL…`, and for the sign-in case `Expected DeferralRowsAsync(world, target) to be 0, but found 1`; M15: `Expected harness.Audit.Actions to be empty … but found … credential.local.disable_deferred_last_entra_independent_admin`.

### 9.5 Limits of this remediation

- The SQL cases consume the recovery code through the real service in its own audited unit; the unit tests and the guard test of C-14 are unchanged. The through-the-caller case uses the resolver (the proof of a pending link); the association service's two calls are covered by unit tests with a scripted policy.
- A compound race (two independent outside changes in one window, one of them a genuine violation by the user) can be reported as the concurrent outcome, §2.1; the next sign-in decides again.
- The findings of the review that are recorded and not implemented are in §7 (D-A to D-E) with their owners and deadlines.
