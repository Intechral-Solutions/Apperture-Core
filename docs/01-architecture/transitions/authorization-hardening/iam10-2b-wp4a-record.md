# IAM-10.2b WP4A: the Entra-primary policy under the administrator-set gate

| | |
|---|---|
| Package | IAM-10.2b, work package 4A ([WP0 reconciliation](iam10-2b-wp0-reconciliation.md) §7, the row marked WP4A; A10.2b.9, A10.2b.10), on the [WP1](iam10-2b-wp1-review-record.md) foundation, the [WP2](iam10-2b-wp2-review-record.md) writers and the [WP3](iam10-2b-wp3-record.md) registration writers and safe post-state rule. |
| Branch | `feat/iam-10.2b-administrator-viability`, from `ed79e0c`. Not pushed, not merged. |
| Status | Implemented and validated; awaiting the independent security review. Nothing here reopens a decision of the [design gate](iam10-design-gate.md) or a ruling D-1 to D-6. |
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
| I-FALLBACK violated | `DeferredLastEntraIndependentPlatformAdministrator` (existing) | enabled | `credential.local.disable_deferred_last_entra_independent_admin` (attempt, after the rollback) |
| I-VIABLE violated (the target's Entra link is pending, for another directory, or not usable; healthy Recovery Access does not matter) | `DeferredLastViablePlatformAdministrator` (new) | enabled | the same attempt row (§5, ambiguity 1) |
| Configuration unavailable (the Local or the Microsoft reading is not established, and no proven Local administrator remains) | `DeferredAdministratorViabilityUnavailable` (new) | enabled | none (§5, ambiguity 2); the caller logs a warning |
| The global gate is busy past its bound | `NotAppliedBusy` (new) | enabled | none; the caller logs a warning |
| The user is the designated Recovery Access account | `ProtectedEmergencyAccount` (existing) | enabled | `emergency.local.protected` (attempt) |
| Already disabled / no credential | `AlreadyDisabled` / `NoLocalCredential` (existing) | unchanged | none |

`NotAppliedBusy` is the only exception the policy turns into an outcome (`PlatformAdministratorSetBusyException`, around the unit). Infrastructure failures, a gate failure that is not contention, cancellation and an unknown audited outcome propagate exactly as before; a source-level test pins that the method has one `catch`.

**The target's Entra proof and tenant.** The shared definition counts a Microsoft way in only when the link is proven, its directory is the one the sign-in accepts in that view, and Microsoft sign-in is usable. The decision therefore never assumes a pending link is an alternative: with only a pending link, or a link for another directory, the credential is the last viable way in and is kept. A user with no administrator role contributes no fact to either view, so the guard sees an unchanged state and allows the change whatever the configuration says.

**Both views.** I-VIABLE and I-FALLBACK are applied to the Now view and the Restart view independently, as for every writer.

## 3. Callers

Every production caller of `ApplyAfterEntraProvenAsync` (inventory unchanged from the integrated baseline): `SqlPrincipalToUserResolver` (proof of a pending link at sign-in), and `EntraIdentityAssociationService` twice (every successful Entra sign-in with a proven link, and a self-link). The policy is best-effort in all three: the sign-in or association succeeds whatever the outcome.

One shared place (`EntraPrimaryOutcomeLogging`) says what a caller says: `NotAppliedBusy` and `DeferredAdministratorViabilityUnavailable` are logged as warnings with the user id and a non-sensitive reason ("another change to administrator access is in progress", "the sign-in configuration could not be established"), and the policy runs again at the next Entra sign-in. Every other outcome is already recorded (by the change, by an attempt row) or needs no record, so the caller adds nothing and the policy logs nothing. An unknown audited outcome keeps its own error with the operation id, in the callers, as before.

## 4. The presentation

`DescribeAsync` (Account Security and user administration) computed the recovery safeguard with the old count-based predicate. It now asks the shared definitions what the policy would do: the state is read as it is (a display takes no gate), the post-state of disabling that user's credential is derived by `AdministratorViabilitySimulation` with the same evaluator, and the same guard decides. The safeguard is shown exactly when the policy would keep the credential, including the case the old predicate got wrong (a proven link for another directory with healthy Recovery Access). A state that cannot be established is shown as kept, because the policy would not disable it either. Tests compare the presentation with what the policy then does, on unit fakes and on real SQL.

## 5. Audit contract: ambiguities for an owner decision

No audit action, catalog entry or schema was added.

1. **One attempt row for two invariants.** `credential.local.disable_deferred_last_entra_independent_admin` is written for an I-FALLBACK deferral and for an I-VIABLE deferral. It is true in both: an I-VIABLE violation means the target's Local credential is its only way in, so the target is the last Entra-independent administrator. But the row carries only the user id; it does not say which invariant was decided, and an I-VIABLE deferral with healthy Recovery Access (which the old policy would have disabled) reads like an I-FALLBACK one. The outcome the caller receives distinguishes them; the audit trail does not.
2. **No row for an unavailable configuration or a busy gate.** Nothing is proven about the administrators in either case, so no last-administrator row is written, and the only record is the caller's warning. A deferral that cannot be seen in the audit trail may be preferred as an attempt row with its own action.
3. **The success row's posture flag** (`recoveryAccessHealthy`) now reports the Recovery Access posture of the state read under the gate, not a separate read.

The recommended direction, if the owner wants the trail to carry them: one new attempt action with a reason field (`last_viable`, `last_entra_independent`, `configuration_unavailable`, `busy`), added by a separate audit-catalog change. This package does not make that decision.

## 6. Writer coverage

The writer guard now lists the policy as integrated (WP4A): the order above is pinned method by method, the single `SetDisabledAsync` of the file is inside that method, the old count and posture predicate (`ListUserIdsWithRoleAsync`, `GetPostureAsync`, `IEmergencyAccessService`, `IRoleRepository`) is gone from the file, `DescribeAsync` takes no gate, and the method has a single `catch`. WP4B stays pending (bootstrap, first administrator, recovery, Recovery Access provisioning and designation, MFA reset and enrolment, the command line, F-1). E-1, E-2 and C-14 are unchanged and still pinned.

## 7. Remaining and deferred

WP4B (mandatory: F-1, D-6 before it, R-2), IAM-10.4 (how the UI presents a deferral), WP5, the audit decision of §5, the legacy deployment-secret fallback (WP3 §5). Known until WP4B lands, and unchanged by this package: the writers that do not yet take the gate (bootstrap, Recovery Access provisioning and designation, MFA reset and enrolment, the command line) can change a viability input between a reducer's two reads; a reducer compares the whole system, so the effect is a refusal or a deferral in the safe direction at worst.

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
| Recovery Access is not protected (M10) | 1 unit and both SQL cases |
| The coordinator no longer takes the gate (M11) | all 4 race cases |

**Corrections during the work.** The first run of the cross-package pair for a Local link removal failed because the arranged administrator had only that link, which the repository refuses to remove; the arrangement gives it a pending Entra link, as in WP3. A missing `using` in a test file. No product defect was found by the tests.

**Not executed, and limitations.** The full multi-hour integration suite and any Browser or UI test were not run (the feature-level pre-merge checkpoint). The policy's code was written before its tests in the same pass, so the unit cases were not seen failing first; their sensitivity is shown by the mutation checks above instead. The pause that holds the first writer is placed before the audit insert: the credential change and the verdict have been made, the commit has not. A deadlock at the secret read inside this unit is covered by the WP3 remediation for the shared read, not repeated here. The dedicated test SQL Server container (`apperture-test-sql`) was running.
