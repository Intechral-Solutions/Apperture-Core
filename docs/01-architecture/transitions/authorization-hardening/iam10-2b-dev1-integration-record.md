# IAM-10.2b: DEV-1 integration (master merged into the IAM line)

| | |
|---|---|
| Package | Integration of the DEV-1 development-loop work (SQL execution honesty, source-corpus and determinism fixes, artifact retention, the validation toolkit, development guidance, the build-protocol experiment) into the IAM-10.2b line, before WP4B-C. Not WP4B-C and not WP5. |
| Branch | `integration/iam10-2b-dev1`, from the IAM baseline `6e3ab57b897f0c8c09cdb58664d9694a7f80d057` (WP4B-B). The protected `feat/iam-10.2b-administrator-viability` is **not advanced** by this package. Not pushed, no PR. |
| Master side | `origin/master` `be0737d5219b659ad87065f72ab40982e32867ea` (PR #48 merged; both required checks green on that commit). |
| Status | Implemented and validated (sections 7 to 10); independent review requested at the exact commit named in the review section. Written in parts: sections 1 to 6 arrived with `8511519`; sections 7 to 11 arrive in a record-only commit that changes nothing else, and the review outcome is added the same way. |
| Validated tree | Every validation result below is for commit `85115199b271a81ab9b8485b3c0954635fa8c7dc` (tree `54f99f4e7db860121626db9daf55c59bafebd915`, `src` tree `e98f43fa04cc9a58e62e8f2c64888e856a800a2b`, clean). The commits after it change only this record. |

## 1. The merge

`d816cd13335374aebb4e6d6637b7d427b9a88364` merges `be0737d` into `6e3ab57` (normal merge commit, parents in that order; no rebase, no squash; the IAM history is untouched).

| Check | Result |
|---|---|
| Textual conflicts | **None.** One file changed on both sides (`tests/Apperture.UnitTests/Governance/RoleWriterCoverageGuardTests.cs`) and merged automatically |
| Merge tree | `f056ae7e8886eb467306e51e29735ca0b2bb452d`, identical to the tree a disposable `git merge-tree --write-tree` rehearsal predicted before the merge |
| `src` tree | `e98f43fa04cc9a58e62e8f2c64888e856a800a2b` before and after: **no production source changed** (master changed nothing under `src/`) |
| Ancestry | `6e3ab57` and `be0737d` are both ancestors; 76 commits are reachable from the merge and not from `6e3ab57` (master's 75 plus the merge) |
| Files | 219 paths differ from the IAM parent and 186 from the master parent; no deletion against the IAM parent |
| Build configuration | `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `NuGet.Config` and `Apperture.slnx` are identical on the IAM parent, the master parent and the merge |

## 2. Compatibility findings

The readiness assessment predicted work in four places. What the integrated tree showed:

| Prediction | Finding |
|---|---|
| New IAM tests would be unlisted passes in the SQL honesty check, needing allow-list entries and new pins | **Not so.** Recorded run of `scripts/dev/Test-SqlExecutionHonesty.ps1` on the merge: 3,479 discovered and executed, 226 passed, 3,253 failed with SQL unreachable, none skipped. The 226 passes are exactly the 226 allow-listed non-SQL tests (the 227th entry, a `requires-git-directory` test, fails only in a linked worktree, as the condition allows). IAM adds about 930 tests and **none passes without SQL Server**. The authoritative run (`-Configuration Release -NoBuild`) is a PASS. **No allow-list entry, no pin and no floor changed** |
| The soft-skip and target-guard source scans might refuse IAM sources | They accept them: `SqlSoftSkipGuardTests.No_test_source_returns_early_from_an_unavailable_SQL_Server_or_swallows_it` and the `SqlTestTargetGuardCoverageTests` cases pass on the merged tree |
| Two `ExecutionMarker` assertions are not evidence | Confirmed and corrected, section 3 |
| Two IAM unit tests walk to the repository root themselves | Left as they are: they work in a clone and in a linked worktree (they look for `AGENTS.md`) and caused no failure |
| Compile conflicts from master's change to the scratch-database helper | None: no IAM-added or IAM-changed test uses `SqlServerScratchDatabase` |

The merged tree builds with 0 errors (protocol build, 73 warnings) and the non-SQL suites pass unchanged: ArchTests 38, Ui.Shared 1,497, Ui.Client 532, AppHost.Diagnostics 7, UnitTests 5,985 (5,984 passed and the one opt-in password-timing diagnostic skipped by design).

## 3. Correction: the two `ExecutionMarker` assertions

`HostConfigurationAttestationSqlServerTests` (first test) and `ManagementHostAttestationSqlServerTests` (first test) ended with `scratch.ExecutionMarker.Should().NotBeNullOrEmpty()`. The marker is built from the catalog name alone, so the assertion holds whether or not any SQL ran. `AuditSqlScratch.ExecutionMarker` and `tests/README.md` say that no test may assert on it, and the DEV-1 plan lists these two as a correction due when this branch integrates.

Each is now replaced by state read back with SQL the test sends itself, on its own connection, from the catalog it created (the mechanism the README names: assertions on database state): `DB_NAME()` equals the scratch catalog, exactly one attestation row exists, and its `HostId` and `Payload` equal what the store under test returned. The store under test is not part of that read.

Commit `95b8e47`. Test-only: no production source, allow-list or pin changed.

## 4. Scope kept out

No WP4B-C or WP5 behaviour, no L-B1/L-B2 remediation, no change to the validation protocol (`.ai/playbooks/dev-loop.md` is unchanged; Configuration C and the lighter review procedure are not adopted), no allow-list or pin change.

## 5. Review-record reconciliation

The branch records for WP4B-D and WP4B-B said "awaiting review" while the program (the DEV-1 plan on master) and the owner treat both as accepted. The reviewers' written reports are **not preserved** in the repository, in the preserved evidence folders or in any file that was searched. This section states what is substantiated and what is not. It does not quote the reviewers.

### 5.1 What is substantiated

| Package | Evidence | Verdict recorded |
|---|---|---|
| WP4B-A | Record §13: review accepted the core, two low findings (L-1, L-2), remediation `05b833a` | Accepted with bounded remediation (already recorded) |
| WP4B-D (`478ae7a`) | Reviewer's own run files from 2026-10-29: 81 unit and 29 integration tests passed (00:59 to 01:04), and a negative-control run of 12 tests of which 8 failed as designed (01:06 to 01:07); L-D1 is recorded durably in the program plan (`docs/audits/rc1-architecture-audit-and-program-plan.md`, OPS-1.4) and in the WP4B-D record §7 item 6; the owner's integration brief and the DEV-1 plan on master state the outcome | **Accepted, no blocker; one Low follow-up (L-D1)** |
| WP4B-B (`6e3ab57`) | Reviewer's own run files from 2026-10-29: 158 unit and 46 integration tests passed (10:36 to 10:46); the owner's integration brief and the DEV-1 plan on master state the outcome | **Accepted, ready for the next work package, with entry obligations for WP4B-C** |

The run files are in the primary checkout's ignored `artifacts/` (`wp4bd-review`, `wp4bb-review`). They show that independent runs took place and passed; they do not contain the verdict text.

### 5.2 Findings, and how far each is established

Wording below is a summary reconstructed after the fact. Where the code can show the consequential fact, it was checked at `6e3ab57` and says so. A closure verification of anything the original reviewer raised remains with the WP4B-C review; the list may be incomplete.

| Id | Summary | Established how | Owner and obligation |
|---|---|---|---|
| L-D1 | Anonymous first-administrator requests whose invalidity is found only inside the unit (username and password policy) take the global gate before being refused, so they can briefly contend with administrator changes | Stated in the WP4B-D record §7 item 6 and the program plan (OPS-1.4) | OPS-1.4, not a WP4B-C item |
| L-B1 | Two Management Host processes with the same host id: the second claims before its web host binds and replaces the live host's row, so no conflict is visible; the superseded process never claims again | **Verified in source:** `ClaimAsync` is an unconditional upsert by host id (it overwrites the process instance and stamps), and `HostConfigurationAttestationPublisher` sets `_superseded` on a `Superseded` refresh and stops recording for good. Consistent with the record's own risk R-B6. The reviewer's suggested remedy (claim at application start, re-claim when the superseding row is stopped or stale) is a summary | **WP4B-C entry obligation**, before any reducer consumes the evidence |
| L-B2 | Refresh does not re-stamp the database binding (fingerprint), so a failover or rename leaves a mismatch until restart | **Verified in source:** `RefreshAsync` updates `RefreshedUtc`, `SchemaVersion` and `Payload` only; `DatabaseFingerprint` is written by the claim | **WP4B-C entry obligation**, same point |
| L-B3 | Two documentation statements are too strong: "never builds the Entra options" (the host's own reading may be the first reader of the options after real traffic, which is R-B1) and R-B8 (a backward step of the database clock can re-open a crashed host's expired window) | Summary only; the code was not re-checked for this item | Correct the WP4B-B record in the WP4B-C documentation; not corrected here |
| I-3 | Pin one attestation reading per mutation attempt (the viability decision reads the attestation at more than one capture point) | Partly seen: the reading seam performs a store read on every call. The number of reads per attempt was not counted here | WP4B-C: a test that pins one reading per attempt |

Not substantiated: the exact wording of any finding, the reviewers' severity labels beyond those above, and whether informational items exist that are not listed. If the original hand-backs are retained by the owner they should be filed beside these records.

## 6. Deferred decisions D-C and D-D (WP4A)

Exact text, from `iam10-2b-wp4a-record.md` §7 (the "Findings of the independent review that are recorded and not implemented"):

- **D-C.** "Contention on the global gate by the policy: every Entra sign-in that still has a Local credential to disable queues behind administrator writers, a busy gate gives `NotAppliedBusy` and the policy repeats at every sign-in, so a busy instance repeats warnings. Measure the deferral and warning volume, and decide whether the warning needs a rate limit or a counter." Assigned to WP5, with the owner's decision on a counter. Deadline: a measurement and a ruling before the WP5 closure record.
- **D-D.** "Operational visibility of a persistent unavailable-configuration outcome: it leaves no audit row (§5, ambiguity 2) and the only record is a warning per sign-in." Assigned to WP5, together with D-C and the §5 owner decision. Deadline: the same ruling as D-C.

Related text the ruling depends on: §5 ambiguity 2 of the same record ("No row for an unavailable configuration or a busy gate ... A deferral that cannot be seen in the audit trail may be preferred as an attempt row with its own action") and its recommended direction ("one new attempt action with a reason field (`last_viable`, `last_entra_independent`, `configuration_unavailable`, `busy`), added by a separate audit-catalog change"). The record also says: "The earlier rulings stand: a busy gate and an unavailable configuration do not need a new durable audit event."

**Impact on WP5.** Both items are WP5 deliverables with a ruling as the gate to the closure record. Neither blocks WP4B-C, which does not touch the Entra-primary policy. WP4B-C does add gate-holding writers (designation, reset, replacement switch, provisioning), so the busy volume that D-C asks about will grow after WP4B-C; a measurement taken before it would understate the problem.

**Recommended disposition for the owner (a recommendation, not a ruling).**
1. D-C: measure first, in WP5, with a deterministic concurrent-sign-in harness against a held gate (count `NotAppliedBusy` outcomes and warnings per sign-in), after WP4B-C lands. Prefer a **counter** (a metric or structured log field) to a rate limit, because a rate limit would hide the very repetition the measurement is meant to expose. Do not add an audit row for a busy gate: sign-in load would write audit rows.
2. D-D: keep the existing ruling that no durable audit event is added for a persistent unavailable-configuration outcome, and give it operational visibility through the same counter plus the existing warning. A separate attempt action with a reason field is an audit-catalog change; if the owner wants it, it is a WP5 scope addition that needs its own decision and review.
3. Record the ruling in the WP5 plan before any WP5 implementation starts.

D-A (activation and switch against the policy), D-B (same-user policy twice) and D-E (multiple Entra links; IAM-10.4) are unchanged and remain WP5 and IAM-10.4 items. OPS-1.4 remains a separate issue.

## 7. Established validation (the authoritative procedure)

Commit `85115199b271a81ab9b8485b3c0954635fa8c7dc`, linked worktree `wt-iam10-2b-integration` (so `.git` is a file), clean tree, SDK 10.0.402, PowerShell 7.6.9, a shell with no `MSBUILD*`, `DOTNET_*`, `NUGET_*` or other customisation. Numbers come from the TRX files and logs, not from the console.

| Step | Command or scope | Result |
|---|---|---|
| Build | `dotnet build Apperture.slnx -c Debug --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers --artifacts-path artifacts\e-final` | 0 errors, 73 warnings (the merge-commit build before the remediation also gave 73), 5 min 15 s |
| Non-SQL suites | `dotnet test --no-build` per project | ArchTests 38 of 38; Ui.Shared 1,497 of 1,497; Ui.Client 532 of 532; AppHost.Diagnostics 7 of 7; UnitTests 5,985 total, 5,984 passed, 0 failed, **1 skipped** (the opt-in password-timing diagnostic, skipped by design) |
| IntegrationTests, whole assembly, real SQL | Disposable `apperture-test-sql` (`127.0.0.1,1433`), required-SQL mode (the only mode), `dotnet test --no-build`, 2 h 30 min | 3,479 executed, **3,478 passed, 1 failed, 0 skipped**, 0 not executed. Isolation modes: 1,047 `rcsi: False` and 1,047 `rcsi: True` parameterised cases, **none failed in either mode** |
| The one failure | `IntegrationTestSecretProtectorTests.Factory_replaces_the_production_protector_and_does_not_share_the_browser_harness` | "Repository root (.git) not found": the test looks for a `.git` directory and in a linked worktree `.git` is a file. This is the allow-list's documented `requires-git-directory` condition, not a product failure. In an ordinary clone of the same code the class passes (4 of 4; the control clone in section 9 ran it) |
| SQL execution proof | `Get-SqlEvidence.ps1 -Mode Markers` before and after, 20 `Apperture_*` catalogs online | total user updates 100 before, 9,540 after: the run wrote to the test server |
| SQL honesty (CI-equivalent) | `Test-SqlExecutionHonesty.ps1 -Configuration Release -NoBuild` after a protocol Release build of the integration project | **PASS**: 3,479 discovered and executed, 226 passed (the allow-listed non-SQL tests; the 227th, `requires-git-directory`, fails only in a worktree, which the list allows), 3,253 failed with SQL unreachable, none skipped, no violation. No allow-list entry, pin or floor changed |

No other suite was run: the BrowserTests project belongs to the hosted `browser-security` check, which runs on the pull request, not here.

## 8. First parallel toolkit comparison

The toolkit ran beside the established procedure, on the same commit, with the same build flags. It is not authoritative for IAM because its own self-tests pass; this section compares what it produced with what the established procedure produced.

`Invoke-Evidence.ps1 -Record iam-int-dev1 -Build -Configuration Debug -SqlContainer apperture-test-sql -ProcessEnvironmentOnly`, build arguments `--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -nologo -v:q` (all on the toolkit's allow-list), plan: the 43-clause focused integration filter used for WP4B-B plus the `AdministratorViability` unit namespace. Evidence: `artifacts/evidence/iam-int-dev1/20261106T205751Z-85115199` (14 files, checksums verified by the tool).

| Property | Established | Toolkit | Same? |
|---|---|---|---|
| Commit and tree | `8511519`, tree `54f99f4e` | same, `src` `e98f43fa`, 0 modified tracked files | yes |
| Focused integration tests (identity and outcome, by display name) | 1,397 selected from the whole-assembly TRX by the same filter, 1,397 passed | 1,397 executed, 1,397 passed | **identical**: 0 only-in-one-side, 0 outcome differences |
| Isolation modes | 574 `rcsi: False`, 574 `rcsi: True`, 249 without a mode | 574, 574 (and 249) | yes |
| Unit tests, `AdministratorViability` | 470 of the UnitTests TRX, 470 passed | 470 executed, 470 passed | identical |
| Executed, skipped, not executed | 0 skipped, 0 not executed in the selection | 0 and 0 | yes |
| SQL execution | markers 100 to 9,540 over the whole run | markers 9,540 to 13,986, 389 catalog start-ups in the window, 3 catalogs written | both prove writes |
| Fails without SQL | every one of the 1,397 failed in the unreachable-SQL honesty run | n/a | none can be a silent no-op |
| `Compare-Trx.ps1` (whole-assembly baseline, focused candidate) | | 1,397 matched by id, 0 added, 0 outcome-changed, 0 new failures, 0 findings (the 2,082 "removed" are the rest of the assembly) | consistent |
| Toolkit authoritative? | | **yes**, 2 runs, 3,326.7 s and 392.6 s | |

Further toolkit results on the same tree:
- `Invoke-AcceptanceControls.ps1` (a **replay** of preserved WP4B-B evidence, not a re-execution): 16 mutations, 16 caught, 0 escaped; the WP4B-D to WP4B-B TRX comparison reproduces (1,361 to 1,397 tests, 574/574 per mode); the WP4A selection names the six classes the original filter missed (and two more).
- **Live** mutations through `Invoke-Mutation.ps1` in a disposable clone at `8511519`, baseline green first, restore proven: B1 (freshness edge made exclusive) caught by 4 failing tests and B8 (refresh not owned by the process) caught by 4 failing tests. The WP4B-B record states 4 and 4 for these two mutations. Two of the 16 were run live; the other 14 are covered only by the replay.
- `Compare-Trx.ps1` lists 20 "soft-skip suspects" (SQL tests that passed in under 100 ms). All 20 failed in the unreachable-SQL honesty run, so none can be a silent skip; they are fast tests.

**Differences and tool limitations found (none changes a result):**
1. **Path resolution.** The toolkit resolved a relative `-ArtifactsPath` (and `Compare-Trx -Json`) against the process working directory, which was the primary checkout, not the worktree it was invoked from. The toolkit's build output (2.0 GB, `artifacts/t-final`) was therefore written into the **primary checkout's ignored `artifacts/` folder**. Git state there is unchanged (`HEAD` `6e3ab57`, no modification) and the evidence folder itself is in the worktree, but the build did not stay where the invoker intended. The folder is left for the owner to delete by exact name. The tool should resolve paths against the invoking location or refuse a path outside the repository it is validating; that is a toolkit change and is not made here.
2. `pwsh -File ... -BuildArgs "--no-incremental,..."` fails ("Missing an argument") because `-File` mode reads a leading `--` as a parameter name; calling the script in-process with an array works (the README's `-File` note does not mention this case).
3. The toolkit counts a theory case as an isolation-mode case when `rcsi: False` appears anywhere in its name (574); counting only names that end with it gives 566. Same tests either way.
4. Raw assembly hashes differ between the two builds (different output paths; same sizes). That is the already-known path-dependence (owner ruling on criterion 6a), so assembly identity is **not** claimed here; both builds are tied to the same commit and tree.
5. `tests/test-manifest.json` maps none of the IAM production areas: `Select-Tests.ps1` over the IAM change set reports 66 of 186 changed files UNMAPPED, mostly `AdministratorViability/*`. A rule set for them is a small task for the start of WP4B-C.
6. Linux was not run.

## 9. Negative controls

All in a disposable clone of the branch (never in the validated tree), each mutation committed in the clone so the tree was clean, all against the same disposable SQL server.

| Control | Setup | Result |
|---|---|---|
| Missing execution | the whole assembly with SQL unreachable (the honesty run) | all 36 tests of the two corrected classes **fail** with `SqlTestUnavailableException`, "This SQL Server test cannot run against SQL Server, so it fails" |
| A. A SQL test that returns before touching SQL (first attempt) | an early `return` in a test of a class with an `IClassFixture` | **mis-targeted**: the fixture fails without SQL before the body runs, so the test failed (honesty PASS, as expected for a failing test). Recorded because the control did not exercise the run-level check; a second defence caught it earlier |
| A2. The same, in a fixture-free test | an early `return` (guarded by a condition the lexical scan cannot read) in `HostConfigurationAttestationSqlServerTests` | honesty **FAIL**, exit 1: "2 tests PASSED with SQL Server unreachable and are not on the approved non-SQL allow-list" |
| B / B2. Concealing it in the allow-list | the A/A2 tests added to `approved-non-sql-passes.tsv` without the pinned fingerprint | honesty **FAIL**, exit 1: the allow-listed pin tests (`The_lists_match_their_reviewed_fingerprints` and others) did not pass |
| C. Simulated execution, old assertions | a store that reads the clock and binding from SQL but keeps claims in memory and never writes the table; the original marker-only assertions | both tests **pass** (2 + 2 cases): the old assertion accepted simulated execution |
| C. Simulated execution, corrected assertions | the same store, the corrected tests | both tests **fail** (2 + 2 cases): "Expected `SELECT COUNT(*) FROM dbo.HostConfigurationAttestations` to be 1, but found 0" |
| Sanity | the corrected tests against the real store | 36 of 36 pass on both isolation modes (and in the whole-assembly run) |

A first run of control C in a shared test process was contaminated (the simulated store's in-memory rows are static, so the host test also saw the other test's row); the table above is the rerun with one test per process.

## 10. Configuration C adoption boundary (report only)

Nothing about the validation protocol changed. Configuration C was not used for any of this evidence; `.ai/playbooks/dev-loop.md` is unchanged; the lighter review procedure (audit E.7) remains unapproved. Status of the five prerequisites after this package:

| # | Prerequisite | Status |
|---|---|---|
| 1 | Toolkit-versus-established SQL comparison in both isolation modes | **Met for the established flags**: identical selection, outcomes and modes (section 8). Not yet made with Configuration C |
| 2 | Equivalent regression and mutation outcomes | **Partly**: replay 16 of 16 reproduced; 2 of 16 re-run live with failing counts equal to the record. No Configuration C build has been run on this tree, so equivalence of Configuration C itself is unshown |
| 3 | No compromised SQL honesty or provenance | **Honesty: met.** Provenance: met with one open tool defect (the path resolution above), which should be fixed before the toolkit is relied on |
| 4 | Security-owner agreement | **Not obtained** (outside this package) |
| 5 | A named adoption boundary before WP4B-C implementation | **Not set.** Candidate: after this integration is closed and before the first WP4B-C commit |

Recommendation: **retain the established protocol for WP4B-C** unless the owner and the security owner decide otherwise at that boundary. If they want adoption, the missing evidence is one Configuration C build of this tree and the same focused SQL set in both modes, compared with sections 7 and 8 (about 35 minutes), a fix for the toolkit path-resolution defect, and `dev-loop.md` updated in the same package.

## 11. What remains

**WP4B-C** (unchanged by this package; decision record sections 3.4, 5, 6.2, 8, 9 and 13, WP4B-B record section 11): designate, strict revoke and bare reset as evidence-dependent reducers; the non-destructive MFA replacement (stage, verify possession, switch under the gate) with its candidate store; provision, first enrolment and regeneration of the recovery codes as gated raisers; command-line mapping of busy, unavailable, conflict and unknown outcomes with exit codes and a test seam; the choice of `configurationEvidence` per outcome and the posture-only proofs behind `not_required`; L-P2 and L-P3; the writer guard flips `WP4B` to integrated; retire `ConfiguredDeploymentAuthenticationConfiguration` (R-B9). **Entry obligations from the WP4B-B review, before any reducer consumes the evidence:** L-B1 and L-B2 (section 5.2), and a test pinning one attestation reading per attempt (I-3). Documentation corrections L-B3 and the R-B8 wording. Full WP4B review. Also useful at the start: a manifest rule set for the 66 unmapped IAM production files.

**WP5** (unchanged): the full pair matrix on both isolation modes, including the WP4A items D-A and D-B; the D-C and D-D rulings (section 6); the optional reason enhancement for refusal audit rows; the writer-guard hardening; mutation checks; the closure record; independent review of the integrated implementation. D-E is recorded as a known limitation until IAM-10.4.

**Before the protected IAM branch can advance:** closure of the independent review of this branch, the owner's explicit instruction, then a fast-forward of `feat/iam-10.2b-administrator-viability` to the reviewed commit and a new verified bundle. None of that is done here.
