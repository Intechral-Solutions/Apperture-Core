# Closure verification request: IAM-10.2b WP4B-C services, SR-1

This request follows [.ai/prompts/review-closure-verification.md](../../../../.ai/prompts/review-closure-verification.md). It states what changed and the implementer's evidence. It is not a closure. A finding is closed only by the reviewer's own reproduction on the reviewer's own clean build of the exact commit.

**Package:** IAM-10.2b WP4B-C service implementation.

**Record:** [iam10-2b-wp4b-c-services-record.md](iam10-2b-wp4b-c-services-record.md), section 15.

**Original review:** the independent review of `ec66ed4e` (code `c1741d47`), verdict **ACCEPT WITH BOUNDED REMEDIATION**, with one required item (SR-1). Its record is archived at `G:\Backup\Apperture-Core-worktree-evidence\2026-11-09\review-wp4bc-services-ec66ed4`, manifest `E476086D55B8BE28D3BE7E59AD037A3CBC8EB3AA98C48B2EA2F9936DC8FF789B`, unchanged.

## 1. Exact commits

| | |
|---|---|
| Commit last reviewed | `ec66ed4ec53f409c754cc2c7d123a6b102aa47fc` (code `c1741d4786dde318cb8d7a1487fdcb086dc07bce`) |
| **Exact commit to verify now** | The head of `feat/iam10-2b-wp4bc-services`; the owner's hand-over message gives its full SHA. Its `src` tree must be `6dfcdbe83945edd67e0c02d1c0f0c686eec9f310` and its `tests` tree `35ccbbc7d0c092c94ec1f28d8069e474d4c1e456`, those of the SR-1 code commit `05ffe1948ecc378535d8adef40e5cc6224d334e7`. If either differs, stop and say so |
| Diff since the last reviewed commit | `git diff ec66ed4e..<head>`: 8 files in `src` and `tests`, plus 3 records |
| Commits in between | `27bc2968` fix: the switch refuses without the caller's open transaction; standalone test callers re-based. `05ffe194` test: the RP1 regression and the candidate-repository caller rules. One documentation commit: the record, this brief, the decision record |

Branch and worktree: `feat/iam10-2b-wp4bc-services`, `F:\Code\Apperture\wt-iam10-2b-wp4bc-services`. It is local only: nothing pushed, no PR. The protected IAM branch is still `1c1030ad` and was not changed.

## 2. Findings to close

| ID | Severity | Finding (as reported) | Remediation | Control that fails on the old behaviour | Evidence |
|---|---|---|---|---|---|
| SR-1 | Bounded remediation (required for acceptance) | "S-1 is enforced only by the call graph and a lexical guard that a new caller written in the command line's idiom evades; the persistence switch silently self-commits when called outside a transaction (no audit row, no gate, no decision). This also undermines the identity-only 'applied earlier' proof" | `27bc2968`: the first statement of `SqlNativeMfaReplacementCandidateRepository.SwitchAsync` throws `InvalidOperationException` when `_db.Database.CurrentTransaction` is null. This happens before any SQL, the switch opens no transaction of its own, and the body runs on the caller's transaction. The contract documents it. The standalone test callers run the switch on a transaction of their own. `05ffe194`: `Only_the_enumerated_files_name_the_candidate_repository` (four files, each with a reason), receivers extended to the concrete type and to `var` locals resolved through `GetService`/`GetRequiredService`, and a self-test | (a) `A_switch_without_the_callers_transaction_is_refused_before_any_statement_and_commits_nothing`, both modes. (b) Mutants SR1-M1 (precondition removed), SR1-M2 (the reviewed self-committing behaviour), SR1-M3 (ambient marker instead of the context's transaction) and SR1-M4 (refused after a statement): all caught. (c) RP1 reproduced: `Applied` with a committed replacement on the reviewed switch, refused with nothing changed on the SR-1 switch, both modes. (d) Guard probe A, byte-identical to the review's: three failures naming the probe file, 55/55 again after its removal. (e) SR1-G1 caught by the self-test | record section 15.5; `artifacts/sr1-logs` (`mutation`, `rp1`, `guard-probe`) and the G: archive named in the record |

## 3. Changes that are not remediation

* **Records:** record section 15 and its header and identity rows. Record sections 5 and 13 mark R-5 as superseded. This brief. A dated update in section 15 of the decision record.
* **Contract documentation:** `INativeMfaReplacementCandidateRepository` describes the requirement and the exception (part of SR-1, listed for completeness).
* **No other change.** In particular: no service behaviour, viability predicate, `PrepareAsync` order, staging, verification, replay protection, recovery-code consumption, evidence classification, audit catalog or event, Foundation 1A–1D contract, honesty allow-list or must-fail list, honesty script, CI, test manifest or validation tooling in the repository.

## 4. What the reviewer does

The template's checklist applies unreduced. Points specific to SR-1:

* **Old behaviour.** RP1 at `ec66ed4e` is in the review's own evidence. The probe's `world.SwitchAsync` helper now opens a transaction of its own (SR-1 re-basing), so **re-running the archived RP1 class unchanged at the new commit would pass through a transaction and report `Applied`**. To reproduce RP1, call `new SqlNativeMfaReplacementCandidateRepository(world.Scratch.NewDb()).SwitchAsync(...)` directly, as the implementer's `artifacts/sr1-logs/rp1` probe does.
* **Genuine enlistment.** The precondition reads the repository context's own current transaction. That is the transaction the audited unit begins on the same scoped context. Check that no production composition could give the repository a different context instance from the unit's.
* **The guard.** Run probe A against the whole unit-test assembly, not only the guard class. At `c1741d47` the older `MfaReplacementAuditContractTests` pin already failed on probe A (record section 15.4); the SR-1 rules add the same requirement, with reasons and the receiver scan, to the writer-coverage guard.
* **Neighbouring path (one step).** Staging, verifying and discarding still join a caller transaction or run as one of their own (unchanged by design; none changes a posture input). Check that none of them can now reach the switch's statements.

## 5. Required output

As in the template:

* for SR-1, `CLOSED`, `NOT CLOSED` or `CLOSED, NEW FINDING NEAR IT`;
* any new findings;
* the verdict at the exact commit;
* the exact scope of the acceptance;
* what was run.

The command-line requirements SC-1 to SC-5, the standing rule under SO-1 and the deferred SI-1 are carried forward in record section 15.7 and are not part of this closure.
