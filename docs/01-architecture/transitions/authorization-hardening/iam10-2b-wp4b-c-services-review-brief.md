# Independent review request: IAM-10.2b WP4B-C service implementation (Recovery Access and native MFA writers)

The implementer filled in sections 1 to 3 and 5 from [.ai/prompts/independent-review-request.md](../../../../.ai/prompts/independent-review-request.md). Section 4 of that template, the reviewer's reproduction checklist, belongs to the reviewer and is **not reduced** here. Neither the lighter audit E.7 procedure nor build Configuration C is adopted.

This brief states facts and claims, not a conclusion. The verdict is the reviewer's. One owner implemented the package. No separate pre-review panel was run, so everything in the [record](iam10-2b-wp4b-c-services-record.md) is the implementer's own account: re-derive anything you rely on.

**Package:** IAM-10.2b WP4B-C service implementation.

**Record:** [iam10-2b-wp4b-c-services-record.md](iam10-2b-wp4b-c-services-record.md). It sits on the accepted foundation described in the [persistence remediation record](iam10-2b-wp4b-c-persistence-remediation-record.md), the [persistence integration record](iam10-2b-wp4b-c-persistence-integration-record.md) (section 10: U1 to U7 and S-1 to S-5) and the [decision record](iam10-2b-wp4b-decision-record.md) (section 13: L-P2 and L-P3; section 15: D-1 to D-4).

## 1. Exact scope under review

| | |
|---|---|
| Repository | `Intechral-Solutions/Apperture-Core`, **local only** (nothing pushed, no PR) |
| Branch / worktree | `feat/iam10-2b-wp4bc-services`, `F:\Code\Apperture\wt-iam10-2b-wp4bc-services` |
| **Exact commit** | The head of that branch; the owner's hand-over message gives its full SHA. Its `src` tree must equal `c9348c106851b7e54aaf0bbed1b685ba33893128` and its `tests` tree `4b5b681b72706c366c9ab20b16ca7e4d2502163d`. If either differs, stop and say so. Validated code commit: `c1741d4786dde318cb8d7a1487fdcb086dc07bce`. Later commits are documentation only |
| Base | `1c1030ad9b023aeba48518f4e2cc09881c1527ae`: Foundation 1A to 1D, independently accepted (`src` `2b7705a6035ff302c864e5cba0ad472b186454c9`). The protected IAM branch `feat/iam-10.2b-administrator-viability` points at it and was not changed |
| Diff to read | `git diff 1c1030ad..<head>`: 11 production files, 19 test files, the records. Six code commits, read in order: `4dddb77a` (Recovery Access, L-P3), `9b61d1a6` (native MFA), `378086eb` (recovery-code consumption), `0865e07a` (a test), `18d86e5e` (preparation before the unit), `c1741d47` (a test) |
| Not under review | Foundation 1A to 1D: accepted at `1c1030ad`; do not reopen it without concrete contradictory evidence. Also out of scope: the emergency command line (not changed by this package; see question 14), WP5, OPS-1 and the DEV-1 toolkit |

## 2. Protected properties and the controls that should fail if they break

| # | Property | Where enforced | Control that should fail if it breaks | Record |
|---|---|---|---|---|
| Q1 | **I-VIABLE**: no designation removes the last viable ordinary Platform Administrator. **I-FALLBACK**: no strict revocation or bare reset removes the Entra-independent administrator while Recovery Access health is not established | `DesignateAsync`, `RevokeAsync`, `ResetAsync`; the guard (accepted) | SQL refusal cases for designation, revocation and reset; mutants S01, S05, M05 | sections 3, 4 |
| Q2 | Every reducer runs gate first, decision last: global gate, state before, its own writes, then `Verify…Async`; nothing is written after the decision; operator input stays outside the gate and the transaction | the seven writers | writer-coverage guard order pins; logged-order unit cases; SQL gate-wait cases | sections 2, 8 |
| Q3 | **D-1**: the loaded row's id is used throughout, never the caller's spelling | all writers | SQL mis-cased designation, reset and replacement cases; mutants S02, M04, M04b | section 7 |
| Q4 | **D-2**: a revocation stands only on its own transition | `RequireRevocationTransitionAsync`, `RequireDecidedOnTheRevocation` | unit cases; mutants S03, S04 | section 3 |
| Q5 | **D-3 / U7 / S-5**: a kept refusal records the decision's evidence; one mapping produces every blocked row; a refusal row with null evidence records `not_required`; a success row with null evidence omits the field | `ViabilityRefusalAudit` | `ViabilityRefusalAuditTests` (every mutation × every evidence value); mutant M11 | section 6 |
| Q6 | **Non-destructive replacement**: the active authenticator stays usable until the switch. Possession is proven against the candidate's secret. A failed, refused or rolled-back switch leaves the active authenticator, its codes and the verified candidate unchanged | `Begin/Verify/CompleteReplacementAsync`, `SwitchAsync` | SQL replacement, expiry, supersession, failure and rollback cases; mutant M02 | sections 4, 5, 8 |
| Q7 | **I-R3-1 / S-4**: a switch is kept only on a `PostureOnly` decision. Otherwise it is `administrator_access_changed`: rolled back, retryable, and recorded with operation `mfa.replace` | `CompleteReplacementAsync` | SQL case with an ungated Microsoft link proof while the switch holds the gate; mutant M01 | sections 4, 6 |
| Q8 | **S-1**: a `Consumed` result is not taken as proof. "Applied earlier" is reported only when the active credential's identity is the candidate's id. An unknown outcome never claims codes | service, `SwitchAsync` identity | SQL lost-acknowledgement, unknown-then-repeat, rolled-back-commit and two-switch cases; mutants M03, M09 | section 5 |
| Q9 | **S-2 / S-3**: nothing commits after a failed switch. Used, repeated or oversized codes and a differently cased owner are refused before any statement | unit (no catch, guard pin), `SwitchAsync` | SQL partial-failure and switch-defence cases; mutant M10 | section 5 |
| Q10 | **L-P2**: no enrolment completes on a secret nobody verified | `CompleteEnrollmentAsync` | SQL L-P2 race; accepted conditional-enrolment case; mutant M06 | section 4 |
| Q11 | **L-P3**: an administrator's password change cannot race a designation | `UserManagementService.SetLocalPasswordAsync` | SQL wait, recheck and control cases; mutants S07, S08, S09 | section 3 |
| Q12 | **Recovery code**: one code signs in once under concurrency | `ConsumeRecoveryCodeAsync`, `MarkConsumedAsync` | `RecoveryCodeConsumptionConcurrencySqlServerTests` (demonstrated failing before the fix); mutants M07, M08 | section 9 |
| Q13 | **U1**: every viability-reading writer prepares the reads before its unit; a failed preparation changes nothing | the seven writers | guard order pins; unit cases with a failing build; mutants S10, M12 | section 2 |
| Q14 | Provisioning decides the designation again under the gate and needs no configuration evidence | `ProvisionAsync` | SQL provisioning race and no-evidence cases; mutant S06 | section 3 |
| Q15 | Secrets: no candidate or active secret, plaintext or hashed recovery code, or verification code reaches an audit row, a log, an exception message, a test failure message or the evidence | services, tests, evidence | review of the new code; the evidence secret scan | section 14 |

**What must not have changed:** every file outside `src` and `tests` except the records. That includes the SQL honesty allow-list, the must-fail list, the honesty script and its pin, `.github` and the required CI checks, `tests/test-manifest.json`, the validation protocol and the protected IAM branch (`git diff --name-only 1c1030ad..c1741d47 -- scripts .github tests/test-manifest.json` is empty). The accepted persistence contracts change only in the three bounded ways of record section 5 (switch identity, switch input validation, ordinal owner check) and in conditional recovery-code consumption (section 9).

## 3. Evidence to inspect

* **Evidence folder on the second drive** and its `SHA256SUMS.txt`: the record (section 14) and the owner's hand-over message give the path. **Verify the checksums first.** The manifest is CRLF with backslash paths, so use PowerShell `Get-FileHash`, not `sha256sum -c`. A mismatch is a finding.
* **Working copy:** `F:\Code\Apperture\wt-iam10-2b-wp4bc-services\artifacts\svc-logs`. It contains:
  * `trx`, including the recovery-code assessment that failed **before** the fix;
  * `mutation`, with `mutants.json` holding the exact edit of each mutant, `results.json` and the per-mutant logs;
  * `honesty`;
  * the candidate-index checks;
  * `tools`, the SQL runner and the mutation harness, which read the connection string from the process environment only.
* **Both isolation modes:** every new real-SQL case runs with `rcsi: False` and `rcsi: True`, reading the state back with plain SQL on a separate connection.
* **Hosted CI:** none.

## 4. What the reviewer reproduces independently

As in the template, unreduced:

* your own clean build of the exact commit, with `--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers` and a fresh artifacts path inside the repository;
* the non-SQL suites;
* the focused SQL set in both modes;
* the honesty script (omit `-NoBuild` for authoritative evidence);
* the three new SQL classes and the re-based accepted cases of record section 12;
* classes the changed files touch that the implementer's filter may have omitted.

The implementer's SQL set and what was not run are in the record (section 10). The full SQL assembly was not run.

Apply the toolkit path caveat: the DEV-1 validation toolkit is not authoritative for source-reading tests.

## 5. Specific questions (bounded)

1. **D-1.** Is there any path where a caller-typed, cased or trimmed user id reaches a guard call, a repository write, a candidate call, a recovery-code owner or an audit target?
2. **D-2.** Can a revocation report success when the designation it names did not change, or changed for another account? Does the check of the decision's own states skip anything in production compositions?
3. **D-3.** Is there a write after any `Verify…Async`, or a staged row before it? Does any refusal row carry evidence other than the decision's?
4. **I-R3-1 and S-4.** Is "keep the switch only on `PostureOnly`" sufficient and necessary? Are its typed refusal (`administrator_access_changed`, retryable) and its audit row the right rule? The audit row records operation `mfa.replace`, that code, and the evidence of the discarded decision.
5. **S-1.** Can the `Consumed` plus identity check report a replacement as applied when it was not this candidate's? Can an unknown outcome or a repeat ever surface codes that were not committed?
6. **S-2.** Is there any path that commits after a switch statement failed? Consider the unit's execution strategy, a swallowed exception, and a refusal that declares a no-op after a partial switch.
7. **L-P2.** Is there an interleaving that enrols a secret whose code was not checked?
8. **L-P3.** Do the gate and the locked recheck together close the race? Is there another writer of an administrator's credential or designation that is still ungated?
9. **Recovery codes.** Is the conditional consumption sufficient under both isolation modes? Is anything else in the sign-in path accepted twice (the TOTP step was fixed in B-1)?
10. **Expiry.** On `Expired` the service discards the user's live candidate, by the accepted user-scoped `DiscardAsync`. A candidate staged after the expiry and before the discard is removed too, and its verification then reports "no longer valid". Is that acceptable?
11. **Typed outcomes.** Is each new conflict code correctly retryable or terminal? Does any outcome misreport a configuration problem, for example a fall-through reported as unavailable?
12. **Controls.** Are the 23 mutants (record section 11) and the new cases real controls or tautologies? What would a mutant of your own show?
13. **Secrets.** Q15: any leak path?
14. **Before the command line.** The emergency command line still performs `reset-mfa` as a bare reset followed by a new enrolment. It does not catch the new typed refusals or a busy gate, so the top-level handler prints them as `Error: <message>` with exit code 1. An unknown outcome is already reported with the instruction to check `emergency status`. The record (section 13) lists the mapping and the `reset-mfa` workflow as entry obligations of the command-line package. Is either a reason to hold this package back?

## 6. Required output

As in the template:

* a verdict at the exact commit: `ACCEPT`, `ACCEPT WITH BOUNDED OBLIGATIONS` or `REJECT`;
* the exact scope of the acceptance, stating separately whether the accepted foundation is still intact;
* findings, each with a severity, a concrete failure scenario, your own reproduction and the required remediation;
* what was run;
* residual risks and owner decisions.

Distinguish *service-level* assurance (these writers, as implemented and tested here) from *command-line* assurance (operator-facing outcomes, exit codes, the `reset-mfa` workflow), which this package does not provide.

## 7. Handling the result

Each finding is fixed in a separate commit, with a control that fails on the old behaviour. The fix is then sent for closure verification at the new exact commit ([review-closure-verification.md](../../../../.ai/prompts/review-closure-verification.md)). The implementer does not declare the package accepted.
