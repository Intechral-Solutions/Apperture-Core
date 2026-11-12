# IAM-10.2b WP4B-C Foundation integration: attestation hardening (1A) with the coordinator and attempt-scoped evidence (1B + 1C)

| | |
|---|---|
| Package | IAM-10.2b, work package 4B-C, the combined foundation: [Foundation 1A](iam10-2b-wp4b-c-foundation-1a-record.md) (publisher and store: L-B1, L-B2) with [Foundation 1B + 1C](iam10-2b-wp4b-c-foundation-1b1c-record.md) (attempt-scoped evidence, I-3, the posture decisions, classifications and refusals). |
| Branch | `integration/iam10-2b-wp4bc-foundation` (worktree `wt-iam10-2b-foundation`), local only: not pushed, no PR, the protected IAM branch not advanced. |
| Status | **Integrated and locally verified by the implementer. Independent security review of `7a15f72` returned ACCEPT WITH BOUNDED REMEDIATION (test-only R-1 and R-2); the remediation is implemented and locally verified (§10) and is pending the reviewer's closure verification.** The foundation has **not** yet received fully independent acceptance. The service-level administrator-viability contract is **not** complete (§8, §10). |
| Not in scope | The WP4B-C service reducers (designate, strict revoke, bare reset, replacement candidate and switch, provision, enrol, regenerate as gated raisers), command-line mapping of outcomes, Foundation 1D, WP5, claiming the attestation at application start, development-environment work. |

## 1. Identity

| | |
|---|---|
| Accepted baseline | `c3e9813a09748234228e3227b22cb906e8aedfad` (`feat/iam-10.2b-administrator-viability`, unchanged) |
| Foundation 1B + 1C | `feat/iam10-2b-wp4bc-coordinator` at `b5c7caf6a9cd5e9e29b76312fb6a7f4e917eabcd` (branch and worktree kept) |
| Foundation 1A | `feat/iam10-2b-wp4bc-attestation` at `a767fba23d9cd0f15947d3b85946a0c2542f53dc` (branch and worktree kept) |
| Merge | `077abc780e4116dea094d81b0c5f5fe9413b5f4c`, a normal merge commit (first parent `b5c7caf`, second parent `a767fba`), made without conflicts and without edits |
| Integration commit (validated) | **`ce1990d6a860056f54ebba5676ce22d53d311802`**, tree `039347f5ff40bec4a442dce78d7d9aacf62b74f5`, `src` `e961044d235e4145685f61cebce154ee1beed82f`, `tests` `63cbc625ebf06b868bca5abd5b71216f77694fc3` |
| This record | A documentation-only commit above `ce1990d`: its `src` and `tests` trees equal those of `ce1990d` |

Before the merge both branches were verified to descend from `c3e9813` and to have clean working trees; the preservation bundle of the IAM branch (`iam-10.2b-administrator-viability-post-dev1-ff-c3e9813-2026-11-07.bundle`) re-hashed correctly and verified, and the 1A branch bundle verified.

### Exactness of the merge

The two branches changed disjoint sets of files (no path appears in both). `git diff --name-only b5c7caf 077abc7` equals the list of files 1A changed against `c3e9813`, and `git diff --name-only a767fba 077abc7` equals the list 1B + 1C changed. The seven `src`/`tests` files 1A changed have the same blob in the merge as in `a767fba`. Merge `src` tree `0c6eef8`, `tests` tree `19838a2`.

### Changes after the merge (`077abc7..ce1990d`, 9 files, +218 -10)

| File | Change | Why |
|---|---|---|
| `AdministratorViabilityAttemptReadingTests.cs` | `ScriptedStore.ReclaimAsync` throws `NotSupportedException`; a source guard that the replacement's refusal is built in one place (`DecideReplacement`) and the Entra-primary policy never calls it | The unit double's exercised contract is reading only; like its other writers it throws, so a coordinator path that wrote the attestation would fail loudly instead of passing |
| `AdministratorViabilityAttemptSqlServerTests.cs` | `CountingStore.ReclaimAsync` delegates to the real store (as its Claim, Refresh and MarkStopped do); five new seam cases (§5) | The counting double wraps the real store, so it must pass the new operation through |
| `EntraPrimaryAuthenticationPolicy.cs`, `...PolicyTests.cs` | `Deferred` names `ConfigurationUnavailable` and throws for any other kind; a test that `RecoveryPostureReduced` and `Allowed` throw | §3 |
| `HostConfigurationAttestationPublisher.cs`, `HostAttestationPayloadSource.cs`, `HostConfigurationAttestationPublisherTests.cs` (comment) | "Never builds the options" corrected to "not before real traffic" | L-B3 |
| `iam10-2b-wp4b-b-record.md` | Component-table row and R-B8 corrected | L-B3 |
| `iam10-2b-wp4b-decision-record.md` | §4 table and the owner's ruling | §2 |

The unit-test double `RecordingStore` and `FakeStore` already carried 1A's `ReclaimAsync`. No other implementer of `IHostConfigurationAttestationStore` exists (searched `src` and `tests`).

## 2. Owner ruling: evidence classification

Incorporated in the decision record §4. `attested`: an accepted attestation actually contributed to the decision, including a Local-only proof that needed the attested Local reading, whether it allowed or refused. `not_required`: the decision was established without needing deployment-specific configuration evidence (inputs unchanged; the posture alone decides). `unavailable`: required evidence could not be established reliably. The implemented behaviour (`attested` for a proof on accepted Local; `not_required` for posture-only decisions) agrees with the ruling and was kept. Verification found no contradiction with the security contract: the value records evidence used, a refusal for missing evidence is `unavailable`, and a posture-only decision never consults the attestation. The 1B + 1C review point 1 is thereby resolved and its mutant E01 still pins the choice (cited, not re-run).

## 3. The Entra-primary policy and the new verdict

`EntraPrimaryAuthenticationPolicy.Deferred` mapped every verdict except two to "administrator viability unavailable" through `_ =>`. The new `RecoveryPostureReduced` could be silently misreported by that arm if it were ever passed. Verified: the verdict is built in exactly one place, `DecideReplacement`, and the policy asks for `VerifyAfterAsync`, which uses the net `Evaluate`, which cannot return it (source guard `The_replacements_refusal_is_produced_only_by_...`). So the path is unreachable today. The arm is nevertheless made explicit: `ConfigurationUnavailable` maps to unavailable, and any other kind throws `InvalidOperationException`, which rolls the disable back inside the audited unit and fails loudly. This is a behaviour change only for kinds the policy cannot receive.

## 4. Verified properties of the combined code

Each was checked by reading the merged code and, where noted, by a test that runs on the merged tree (§5, §6).

1. **One trusted reading per attempt (I-3).** `AttemptConfigurationReading` is owned by one `AdministratorViabilityScope`, pinned on first use after the gate, inside the transaction. Unchanged by the merge; 1A changed nothing it reads. Seam cases 1 to 3 show a reclaim, a refresh and a re-stamp during an attempt do not change what it reads (`Reads == 1`).
2. **No evidence reuse across attempts.** A rerun or a new scope reads again; the seam cases and the existing rerun cases confirm the next attempt sees the reclaimed or corrected row.
3. **Freshness budget.** `EvidenceRemaining = window - (DatabaseNowUtc - RefreshedUtc)` in the classifier, where `window` defaults to `HostConfigurationAttestationTiming.FreshnessWindow`, which 1A assigns from `HostConfigurationAttestationLiveness.FreshnessWindow` (180 s). Same value, same meaning; the reader and the store's reclaim use one definition. A reclaim writes a new `RefreshedUtc`, which grants a *new* reader a full window and no existing reading anything (seam case 2).
4. **Fingerprint stamping.** The refresh and the reclaim re-stamp the server-computed fingerprint; the reader's comparison is unchanged. Seam case 3.
5. **Safe re-claim; no displacement.** Seam cases 1, 4 and 5 and the 1A tests. A stopped publisher cannot take its own stopped row back (1A store case).
6. **Atomicity.** The reclaim is one statement; claim stays an upsert. 1A's 8-way races and seam case 5 (six publishers racing while an attempt holds the gate) both modes.
7. **Both invariants and the gate.** The publisher and store take no gate and no administrator lock (1A tests unchanged); the attempt's reading happens after the gate. The existing 304 viability cases of `AdministratorViabilityWp2SqlServerTests` pass on the merged tree in both modes.
8. **Classifications and refusals.** Unchanged by 1A; §3 for the Entra-primary policy; the refusal-code table test covers `recovery_access_posture_reduced`.

## 5. Validation

Build: `dotnet build Apperture.slnx -c Release --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers --artifacts-path artifacts\fnd-build1`, 0 errors, 5 min 12 s, from the tree committed as `ce1990d` (no source edit after the build started; the working tree was clean at commit). Test SQL: the disposable `apperture-test-sql` on `127.0.0.1,1433`, connection string in the process environment only, `.env` ignored by the tests, target checked before the runs. The DEV-1 toolkit was not used and no `AUTHORITATIVE` label is relied on; Configuration C and the E.7 protocol were not adopted.

| Run | Result |
|---|---|
| UnitTests, whole project | 6,124 passed, 0 failed, 1 not executed (the opt-in password-timing diagnostic, unrelated, unchanged) |
| ArchTests / Ui.Shared / Ui.Client / AppHost.Diagnostics | 38 / 1,497 / 532 / 7 passed, 0 failed |
| Real SQL, the relevant classes (below) | **549 of 549 passed**, 0 skipped, 0 not executed, 16 min 46 s |
| `scripts/dev/Test-SqlExecutionHonesty.ps1 -Configuration Release -NoBuild` | **PASS**, exit 0: 3,557 discovered and executed, 226 passed (all on the reviewed allow-list), 3,331 failed with SQL unreachable; provenance `reused-existing-build`, no tracked change from HEAD (`ce1990d`); allow-list, must-fail list and script unchanged |

SQL classes in the filter (each case is run with `rcsi: False` and `rcsi: True`, the two isolation modes): `AdministratorViabilityWp2SqlServerTests` 304 (152 + 152), `EntraPrimaryPolicyViabilitySqlServerTests` 88 (44 + 44), `HostConfigurationAttestationSqlServerTests` 56 (28 + 28), `AdministratorViabilityAttemptSqlServerTests` 50 (25 + 25), `AdministratorViabilitySqlServerTests` 21 (10 + 10 + 1 mode-independent), `AdministratorViabilityHostHttpTests` 12 (6 + 6), `ManagementHostStartupOrderSqlServerTests` 10 (5 + 5), `ManagementHostAttestationSqlServerTests` 8 (4 + 4). The source and writer guards (`AdministratorViabilityWriterCoverageGuardTests`, the attestation source pins, the new replacement-verdict guard) are in the whole-project unit run.

**Not run:** the full IntegrationTests assembly (about 2 h 11 min in earlier packages), BrowserTests and EndToEndTests, and the hosted CI. The classes not in the filter do not touch the changed files, but that is a claim by reading, not by running.

### The new seam cases (`AdministratorViabilityAttemptSqlServerTests`, five tests, both modes = ten runs)

| Case | What it shows at the boundary |
|---|---|
| A publisher takes its row back during an attempt that began without evidence | Starting from a stopped superseding publisher, the attempt reads `unavailable`; the reclaim during the attempt succeeds without waiting for the gate; the attempt's decision stays unavailable with one read; the next attempt is attested |
| A reclaim that writes a fresh row during an attempt | An attempt that read evidence with 5 s left still refuses after 5.5 s, although the row was meanwhile reclaimed with a full window |
| A refresh that corrects the binding during an attempt | A record for another database reads `unavailable`; the owner's refresh re-stamps it; the running attempt does not change; the next attempt is attested |
| A publisher cannot displace a live competitor during an attempt | The reclaim is `Held`, the old process's refresh is `Superseded`, the attempt reads the competitor's row, the row's owner is unchanged |
| Many publishers racing for a stopped row while an attempt holds the gate | Six concurrent reclaims: exactly one wins, none waits for the attempt, the attempt's reading is not changed |

## 6. Negative controls

**Integrated-tree seam mutations, run for this package** in a disposable clone of `ce1990d` (never the worktree under test), green baseline first (unit 96, SQL class 50), restore proved byte-identical by blob hash and clean status, restored tree rebuilt. 5 of 5 caught:

| Mutant | Boundary | Caught by |
|---|---|---|
| I01 the reclaim can displace a live publisher | store → attempt | 2 seam cases × 2 modes (seam class only was run) |
| I02 the reclaim ignores a stopped publisher | store → attempt | 2 seam cases × 2 modes (seam class only was run) |
| I03 the refresh keeps the old fingerprint | store → attempt | 1 seam case × 2 modes (seam class only was run) |
| I04 `EvidenceRemaining` ignores the row's age | classifier → attempt | unit 3; SQL 2 cases × 2 modes, including the new reclaim-during-attempt case |
| I05 the attempt reads the attestation again instead of its pinned reading | coordinator → store | unit 11; SQL 3 cases × 2 modes, including two of the new seam cases |

I01 to I03 are the same edits as 1A's M02, M03 and M07, whose store-level tests also catch them (1A record §5). This run executed only the seam class on purpose, to show the boundary itself notices: the coordinator's attempt behaviour is checked against the real publisher store, not only the store against itself. A correction to the 1A harness found while writing this one: `Invoke-Build` in `mutate.ps1` returned the build text as well as a Boolean, so its "mutant did not build" guard could never fire. Every 1A result nonetheless names failing tests consistent with the mutation, which an unrebuilt (unmutated) binary cannot produce, and the 1A baseline passed; the integration script returns only the Boolean.

**Cited, not re-run.** The 44 of 44 mutants of Foundation 1B + 1C (disposable clone `mut-iam-coord`, at `73140f2` and `58bb7b1`) and the 13 of 13 mutants of Foundation 1A (clone of `0f550b5`) are independently preserved implementation evidence of those trees. They were **not** executed against the integrated tree.

## 7. Documentation corrections made (L-B3)

The publisher's and the payload source's remarks, the WP4B-B component table row and R-B8 no longer say the writer "never" builds the options (it does not before real traffic; afterwards the host's own reading may be the first reader of the endpoints' options: R-B1) and no longer say a backward step of the database clock only makes rows future-dated (it can also bring an expired row back inside the window).

## 8. What is not done, and what is only locally verified

* L-B1, L-B2 and I-3 are **implemented and locally verified**, pending independent integration acceptance. L-B3 is corrected as above. L-D1 is OPS-1.4.
* The service-level contract is **not complete**: no reducer calls the coordinator yet; designation, strict revocation, bare reset and the replacement lifecycle are not wired; provision, enrol and regenerate are not gated raisers; the command-line mapping of outcomes and exit codes is not done; WP4B-C items L-P2 and L-P3, the MFA candidate store and WP5 are open. Foundation assurance does not extend to any of them.
* Residual points carried forward: the start-up claim is still unconditional (a process that claims and then dies without binding supersedes a live host for up to the window); a rebinding refresh is silent; a record dated after the database clock blocks a reclaim until the clock catches up (the 1A record §8); no re-check between the decision and the commit (the R-B2 position) and a host that stops during an attempt is still trusted up to the window (the 1B + 1C record §8); `EvidenceRemaining` and the freshness bound inherit the database-clock limit in R-B8.
* No claim is made that the independent review will accept the integration. See the [review brief](iam10-2b-wp4b-c-foundation-integration-review-brief.md).

## 9. Evidence

Working evidence is under `artifacts\fnd-logs` in the worktree (ignored): build log, `evidence.ps1` and its TRX files, the honesty report, `mutate-int.ps1` and per-mutant logs with `mut\summary.md`. A copy with `SHA256SUMS.txt`, and bundles of the integration branch and of the 1B + 1C branch, is archived on the second drive; the location is in the hand-back. Do not `git worktree remove` this worktree before that copy is confirmed (removal deletes ignored files).

## 10. Independent security review of `7a15f72` and the bounded remediation (R-1, R-2)

**Review.** Verdict **ACCEPT WITH BOUNDED REMEDIATION** at `7a15f723f36aa1bea44b29b8f83acd1d3075d030`; no blocking finding and no production change required. The reviewer's record and negative-control evidence are on the second drive (`...\2026-11-07\review-wp4bc-foundation-7a15f72`; its 55-entry `SHA256SUMS.txt` was re-verified before use: 0 mismatches). The reviewer also reports that the reviewing session authored Foundation 1B + 1C, so the reviewer notes that an owner who wants full independence for 1B + 1C may commission a second reviewer. That is an owner decision, not made here. The reviewer's own mutants N1 and N4 were caught at `7a15f72`; N2, N3 and N5 **escaped**, which is what R-1 and R-2 are.

**Remediation commits** (test-only; `git diff --name-only 7a15f72..HEAD -- src` is empty and the `src` tree is still `e961044d235e4145685f61cebce154ee1beed82f`):

| Commit | Content |
|---|---|
| `07c03bc3` | R-1 unit cases and R-2 SQL case |
| `ec659b60` | Hardening of the R-2 case against a slow test database; three gaps found by a read-only adversarial review of the new tests (below) |

**R-1 (N3, N5): where the attempt's time is spent.** `AdministratorViabilityAttemptReadingTests` gained two hooks, `ScriptedStore.OnRead` and `ChangingFacts.OnRead`, that spend time on the existing `ManualClock` from inside the operation. Nothing sleeps and nothing reads the system clock. A row 170 s old in the 180 s window leaves exactly 10 s.

| New case | Proves |
|---|---|
| Time spent waiting for the attestation store counts (theory over After, Designation, Revocation, BareReset, Replacement): 10 s + 1 tick inside the store's answer | Both the captured state and the state after the change lose the evidence together; exactly one store read. Kills N3 |
| The same, up to exactly what was left (10 s) | Still `attested`; no over-sensitivity |
| Time spent reading the state after the change counts (same theory), spent only inside that read, armed after the capture | Both states lose the evidence; one store read. Kills N5 |
| The same, up to exactly what was left | Still `attested` |
| The store's time (6 s) and the later state read's time (5 s) add up | Neither read alone uses more than the 10 s left; together they do. The decision is refused as `ConfigurationUnavailable` with evidence `unavailable` and the "went out of date" detail; one store read |
| 6 s + 4 s | Exactly the budget: still `attested` |
| A reading that was missing or already stale keeps its own reason after a five-minute attempt (found by the adversarial review: a mutant that treated every non-attested reading as expired would otherwise have escaped) | The reason is never replaced by the "went out of date" reason |

The five requested properties map as follows: time awaiting the store counts (rows 1 and 5), time of the post-change read counts (row 3 and 5), evidence is unavailable when the budget is exhausted (rows 1, 3, 5), before and after states are classified together (every row asserts both `Captured` and `After`, and the trust levels), and no extra attestation read (every row asserts `Reads == 1`).

**R-2 (N2): the early edge of the reclaim window.** `HostConfigurationAttestationSqlServerTests.A_holder_that_went_quiet_for_most_of_the_window_but_not_all_of_it_is_still_held_and_never_displaced`, both isolation modes. The holder is set 176, 178 and 179 s back by the database's own clock (`DATEADD(SECOND, -n, SYSUTCDATETIME())`), its age is read back from the database (at least n seconds, and the reader's `Assess` still calls it attested), and the reclaim must return `Held`, leave the process unchanged and leave the **whole** row equal to what was read (the sibling case compares the whole row the same way). The existing 170 s, dated-ahead and 181 s cases are untouched. The row keeps ageing while a test runs, so a stall of the test database could make a correct store see a holder past the window; a stopwatch bounds the real time from the update to the end of the reclaim, and an attempt whose age plus that time could have reached 180 s proves nothing and is repeated (three tries, then it fails with that explanation). Any other result is a hard assertion, and a wrong store is not excused: its mistake shows at 176 s with 4 s of margin. A second case, `A_holder_that_went_quiet_just_longer_than_the_window_is_reclaimed_so_the_store_is_not_laxer_than_the_reader`, sets a holder 180.5 s back and requires `Stale` from the reader and `Reclaimed` from the store; the row only ever ages, so a slow database cannot fail it, and it catches a store that is a second laxer than the reader.

**The window, the SQL reclaim logic and the production code were not changed.**

**Adversarial review of the new tests.** Three read-only skeptic agents reviewed the committed tests at `07c03bc` without building or running anything (one traced N3 and N5; one invented new mutants of the time logic; one reviewed the SQL case). They confirmed by trace that N2, N3 and N5 are each killed by a named test, and found: the 179 s case could fail under a stall of the test database (fixed: the stopwatch guard), a 181 s reclaim bound would survive (fixed: the 180.5 s case), a refused reclaim was checked only by its refresh time (fixed: whole row), and a mutant making every non-attested reading expire would survive (fixed: the keeps-its-own-reason case). These are the implementer's own aids, not independent acceptance.

**Mutation acceptance.** The reviewer's `review-mutations.ps1` (sha256 `a468fe1b1bd8f4e6be398a12a0f958378efafda84c76b68f75d7a61b5f36f50f`, verified against the review's manifest) was copied and changed only in its harness: a `-Only` selector, a recorded diff of each mutation, the first error line of every failed test, and the clone path. **The N1 to N5 mutant definitions were not changed.** It was run for N2, N3 and N5 against `ec659b60` in a disposable clone, one mutant at a time: the find string must occur exactly once, both test projects are rebuilt (a failed build is `Invalid`, never `Caught`), the unit filter (`AdministratorViability` unit namespace and `EntraPrimaryAuthenticationPolicyTests`) and the SQL filter (`HostConfigurationAttestationSqlServerTests`, `AdministratorViabilityAttemptSqlServerTests`, `ManagementHostAttestationSqlServerTests`) run, the source is restored by `git checkout` and compared by blob hash, and the run ends with a rebuild and a repeat of the baseline.

| Id | Mutant | Result | Failing tests (unit / SQL) and why |
|---|---|---|---|
| baseline | none | green: unit 679, SQL 118 (`7a15f72`: 663 and 114) | |
| N2 | reclaim stale bound 175 s instead of 180 s | **Caught**, 0 / 2 | the 176, 178, 179 s case, both modes: `Expected outcome to be Held`, the store took a live holder |
| N3 | `_startedAt` taken after the store answers | **Caught**, 6 / 0 | the five store-time rows (`Expected EvidenceOfBothStates(scope) to be equal to {Unavailable, Unavailable}`) and the add-up case (`Expected decision.IsAllowed to be False`) |
| N5 | expiry judged before the post-change state read | **Caught**, 6 / 0 | the five state-read rows and the add-up case, the same assertions |
| final | none | green: unit 679, SQL 118 | restored sources byte-identical (hashes compared by the script, which would have thrown) |

Each failure is an assertion about the intended behaviour (outcome `Held`, evidence classes of both states, `IsAllowed`), not a build error or a fixture failure. N1 and N4 were caught by the reviewer at `7a15f72` and were **not rerun**; the earlier integration mutants I01 to I05, 1A's 13 and 1B + 1C's 44 are still cited, not rerun.

**Validation of the final tree `ec659b60`** (`dotnet build Apperture.slnx -c Release --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers --artifacts-path artifacts\rem-build`: 0 errors, 5 min 21 s; the tests were built from the tree committed afterwards, with no edit after the build started; the SQL target was the disposable container on `127.0.0.1,1433`, credentials in the process environment only, DEV-1 toolkit not used):

| Run | Result |
|---|---|
| UnitTests, whole project | 6,140 passed (6,124 plus the 16 new cases), 0 failed, 1 not executed (the opt-in password-timing diagnostic, unchanged); the source and writer guards are in this run |
| ArchTests / Ui.Shared / Ui.Client / AppHost.Diagnostics | 38 / 1,497 / 532 / 7 passed |
| Real SQL, the same classes as §5 | **553 of 553 passed** (549 plus the 4 new runs), 0 skipped, 16 min 41 s. Per class, `rcsi: False` / `rcsi: True`: `AdministratorViabilityWp2SqlServerTests` 152 / 152, `EntraPrimaryPolicyViabilitySqlServerTests` 44 / 44, `HostConfigurationAttestationSqlServerTests` 30 / 30 (was 28 / 28), `AdministratorViabilityAttemptSqlServerTests` 25 / 25, `AdministratorViabilitySqlServerTests` 10 / 10 and 1 without a mode, `AdministratorViabilityHostHttpTests` 6 / 6, `ManagementHostStartupOrderSqlServerTests` 5 / 5, `ManagementHostAttestationSqlServerTests` 4 / 4 |
| `scripts/dev/Test-SqlExecutionHonesty.ps1 -Configuration Release -NoBuild -ArtifactsPath artifacts\rem-build` | **PASS**, exit 0, authoritative: 3,561 discovered and executed (3,557 plus the 4 new runs), 226 passed (all allow-listed; 227 entries, one tolerated linked-worktree test), 3,335 failed with SQL unreachable, 0 violations. Provenance `reused-existing-build` at `ec659b60`, tree `a0116dcd`, clean. The script and the allow-list and must-fail list hashes are identical to the previous report (script `A75F53F6...E513`, allow-list `8798D5A9...442E`, must-fail `17D8A73D...73E3`) |

The new cases need no change to the allow-list or the must-fail list: the honesty script does not police extra failures, and the four new SQL runs are among the failures with SQL unreachable. CI, the allow-lists, test floors, the build protocol and the development infrastructure were not touched (`git diff --name-only 7a15f72..HEAD` lists the two test files and, with this commit, the two documents).

**Not run:** the full IntegrationTests assembly, BrowserTests, EndToEndTests, the hosted CI, and the earlier mutation suites on the changed tree. The DEV-1 toolkit's source-root defect remains unresolved and its `AUTHORITATIVE` label was not relied on.

**Reviewer obligations still open.** (1) Closure verification of R-1 and R-2 at the new exact commit, which the implementer does not self-declare. (2) D-1 to D-4 are **requirements of the WP4B-C service implementation**, recorded in the [decision record §15](iam10-2b-wp4b-decision-record.md); none is discharged by this foundation. (3) The review's informational points (numbered in the review record; its I-3 is not the attempt-scoped evidence item I-3 of §8) I-1 (make the `AttemptConfigurationReading` constructor internal), I-2 (a disbelieved snapshot reaching `FullEvaluation`), I-4 (the unknown-kind throw text in `ViabilityRefusal`), I-5 (a reclaim that commits and is then cancelled at stop leaves the row live for up to the window) and I-6 (test-quality remarks on the posture-decision tests) were **not addressed** here; they are outside the authorised scope (test-only, R-1 and R-2) and are carried forward. I-3 (the `ViabilityDecision` Evidence comment) is a documentation point, also not changed here. (4) The foundation assurance above still does not extend to the service level (§8).
