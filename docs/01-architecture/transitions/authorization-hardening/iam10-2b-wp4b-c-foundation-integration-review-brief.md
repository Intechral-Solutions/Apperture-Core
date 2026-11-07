# Independent review request: IAM-10.2b WP4B-C Foundation integration (1A + 1B + 1C)

Filled from [.ai/prompts/independent-review-request.md](../../../../.ai/prompts/independent-review-request.md) by the implementer (sections 1 to 3 and 5). Section 4 of that template, the reviewer's reproduction checklist, is the reviewer's and is **not reduced** here; the lighter audit E.7 procedure is not adopted. This brief states facts and claims. It does not state a conclusion; the verdict is the reviewer's.

**Package:** IAM-10.2b WP4B-C Foundation integration. **Record:** [iam10-2b-wp4b-c-foundation-integration-record.md](iam10-2b-wp4b-c-foundation-integration-record.md) (with the [1A](iam10-2b-wp4b-c-foundation-1a-record.md) and [1B + 1C](iam10-2b-wp4b-c-foundation-1b1c-record.md) records and the [decision record](iam10-2b-wp4b-decision-record.md)).

## 1. Exact scope under review

| | |
|---|---|
| Repository | `Intechral-Solutions/Apperture-Core`, **local only** (nothing is pushed; there is no PR) |
| Branch / worktree | `integration/iam10-2b-wp4bc-foundation`, `F:\Code\Apperture\wt-iam10-2b-foundation` |
| **Exact commit** | The head of that branch, whose full SHA the owner's hand-over message gives (a document cannot contain the SHA of the commit that holds it). Its `src` tree must equal `e961044d235e4145685f61cebce154ee1beed82f` and its `tests` tree `63cbc625ebf06b868bca5abd5b71216f77694fc3`; those are the trees of validated code commit `ce1990d6a860056f54ebba5676ce22d53d311802`. If either differs, stop and say so |
| Base | `c3e9813a09748234228e3227b22cb906e8aedfad` (accepted IAM checkpoint) |
| Components | 1B + 1C `b5c7caf6a9cd5e9e29b76312fb6a7f4e917eabcd`; 1A `a767fba23d9cd0f15947d3b85946a0c2542f53dc`; merge `077abc780e4116dea094d81b0c5f5fe9413b5f4c`; post-merge integration `ce1990d` |
| Diff to read | `git diff c3e9813..<head>` (32 files at `ce1990d`), and separately `git diff 077abc7..ce1990d` (9 files) for what the integration itself added |
| Not under review | The service reducers, Foundation 1D, WP5, the command-line mapping of outcomes, MFA candidate persistence, claiming the attestation at application start, the DEV-1 toolkit, development-environment hardening. They do not exist in this tree or are out of this package |

## 2. Protected properties and the controls that should fail if they break

| # | Property (what must stay true) | Where enforced | Control that should fail if it breaks | Evidence |
|---|---|---|---|---|
| P1 | The merge is exact: nothing but the two branches' own changes and the nine integration files | `git` | Reviewer's own `git diff` / `merge-tree` / blob comparison | record §1 |
| P2 | A publisher takes its row back only when the database shows the holder stopped, silent beyond the window, or the row gone; a live holder is never displaced | `SqlHostConfigurationAttestationStore.ReclaimAsync` | 1A store tests; 1A mutants M02 to M06, M09; integration mutants I01, I02 | 1A record §4, §5; integration record §6 |
| P3 | Of any number of simultaneous candidates for a free row at most one obtains it; claim stays atomic | same statement; claim upsert | 8-way races, 6 rounds, both modes; 1A mutant M11; seam race case | same |
| P4 | The stored fingerprint is re-stamped by the owner's refresh and by a reclaim, and never by a non-owner; the reader's comparison is unchanged | store `RefreshAsync`, `ReclaimAsync`; classifier | 1A mutants M07, M08, M10; integration mutant I03 | same |
| P5 | A protected attempt uses exactly one attestation reading, taken after the gate inside its transaction; nothing a publisher writes meanwhile changes, upgrades or extends it; a new attempt reads again | `AttemptConfigurationReading`, `AdministratorViabilityScope`/coordinator | 1B + 1C unit and SQL classes and its 44 mutants (cited); five seam cases; integration mutant I05 | integration record §5, §6 |
| P6 | Evidence is not relied on past the window it had when read (`EvidenceRemaining` from the database's clock) | classifier; `AttemptConfigurationReading.HasExpired` | freshness cases; integration mutant I04; seam case 2 | same |
| P7 | The publisher and store take no gate and no administrator lock; the two administrator-viability invariants and the global gate behave as before on the merged tree | publisher/store; coordinator | 1A tests that hold the gate; `AdministratorViabilityWp2SqlServerTests` (304 cases); seam race case | same |
| P8 | Evidence classes follow the owner's ruling (`attested` when an accepted Local attestation contributed; `not_required` when none was needed; `unavailable` when required evidence cannot be established) and the typed refusals, including `recovery_access_posture_reduced`, are correct | `AdministratorViabilityGuard`, `ViabilityRefusal`, decision record §4 | posture-decision tests, mutant E01 (cited), refusal-code table test | 1B + 1C record |
| P9 | `RecoveryPostureReduced` cannot be misreported by the Entra-primary policy | `EntraPrimaryAuthenticationPolicy.Deferred` | the new throw test; the source guard that only `DecideReplacement` builds it and the policy never calls it | integration record §3 |
| P10 | The two test doubles do not hide missing behaviour | `ScriptedStore`, `CountingStore` | review of what each does with the new member | integration record §1 |

**What must not have changed:** the required CI checks, the SQL honesty allow-list, must-fail list and script (and its pin), the validation protocol (`.ai/playbooks/dev-loop.md`), the migrations and the database schema, the model snapshot, `EmergencyAccessService`, `NativeMfaService` and the emergency CLI, the protected IAM branch. The diff should touch none of them; verify.

## 3. Evidence to inspect

* Evidence folder (copy on the second drive) and `SHA256SUMS.txt`: the owner's hand-over names the path. **Verify the checksums first**; a mismatch is a finding.
* Working copy: `F:\Code\Apperture\wt-iam10-2b-foundation\artifacts\fnd-logs` (`ev\trx`, `honesty`, `mut`, `build1.log`); 1A evidence on the second drive under `...\2026-11-07\wt-iam10-2b-attestation-foundation-1a`.
* SQL execution and isolation: every real-SQL case runs with `rcsi: False` and `rcsi: True`; the new cases read state back with SQL they send themselves.
* Mutation results: `fnd-logs\mut\summary.md` (integration seam, 5 of 5); the 1A and 1B + 1C results are preserved evidence of the *separate* trees, not rerun on the integrated tree.
* Hosted CI: none (nothing is pushed).

## 4. What the reviewer reproduces independently

As in the template, unreduced: own clean build of the exact commit with `--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers` and a fresh artifacts path; the focused and non-SQL suites in full; every new or changed test class on both isolation modes; classes the changed production files touch that the implementer's filter may have omitted (the implementer's SQL filter was the attestation, viability, Entra-primary, startup-order and attestation host-test classes; **not** the whole IntegrationTests assembly, BrowserTests, EndToEndTests); the mutation or negative control for each property, plus at least one novel control; `Test-SqlExecutionHonesty.ps1`; anything skipped or not executed (one opt-in password-timing diagnostic is not executed). The DEV-1 toolkit is not authoritative for source-reading tests; do not rely on its label.

## 5. Specific questions (bounded)

1. **Merge exactness.** Is there any change in the diff that is neither a 1A change, a 1B + 1C change, nor one of the nine integration files? Any behaviour change in a file both branches could affect?
2. **Re-claim atomicity.** In `ReclaimAsync`, can two candidates both receive `Reclaimed` under locking read committed or under read committed snapshot, including against a concurrent `ClaimAsync` (which is an unconditional upsert by design)? Can a claim and a reclaim interleave so that the row names a process other than the one that last wrote it?
3. **Stale and stopped boundaries.** Is "strictly older than the window" (store) versus "age not greater than the window" (reader) consistent at the edge and with a record dated after the database clock? Can a reclaim ever make a row live that the reader had already called stale for a reason other than a new write?
4. **Own-row paths.** Is the owner's idempotent reclaim, and the refusal for a stopped owner, safe if the same process id appears twice (it should not, but check what ensures it)?
5. **Rebinding.** Is the owner's silent re-stamp of the fingerprint safe against a restored or copied database that a live process is still pointed at? Does any path let a non-owner, or a writer that names its own fingerprint, bind a row?
6. **Attempt semantics across the seam.** Can anything a publisher does (claim, reclaim, refresh, stop) between the attempt's gate and its decision produce a decision that used evidence the attempt did not read, or evidence beyond its window? Is `EvidenceRemaining`, computed from `RefreshedUtc`, still correct for a row that was reclaimed rather than refreshed, and across a database-clock step back (record R-B8)?
7. **Classification.** Does every decision path record `attested`, `not_required` or `unavailable` in line with the owner's ruling? Is the Entra-primary `Deferred` throw reachable by any caller, and is throwing there safe (the unit rolls back)?
8. **Do the tests catch failures across the boundary?** Are the five seam cases and the five integration mutants a real control of the boundary or tautologies? What would a novel mutant of your own show? Are the two test doubles honest?
9. **New gaps from the merge.** Anything the merge made worse that neither branch showed alone.

## 6. Required output

As in the template: a verdict at the exact commit (`ACCEPT`, `ACCEPT WITH BOUNDED OBLIGATIONS`, `REJECT`); the exact scope of acceptance; findings with severity, a concrete failure scenario, the reviewer's own repro and the required remediation; what was run; residual risks and owner decisions.

**The reviewer must distinguish two kinds of assurance in the verdict.** *Foundation-level*: the attestation, the publisher and store, the attempt-scoped reading, the decisions, classifications and refusals, as implemented and tested here. *Future service-level*: whether the administrator-viability contract holds end to end once the reducers (designate, strict revoke, bare reset, the replacement lifecycle), the gated raisers and the command-line mapping call these foundations. Nothing in this package establishes the second, and an acceptance of the first must not be read as covering it.

## 7. Handling the result

Findings are fixed with a control that fails on the old behaviour, in a separate commit, and sent for closure verification at the new exact commit ([review-closure-verification.md](../../../../.ai/prompts/review-closure-verification.md)). The implementer does not declare the integration accepted.
