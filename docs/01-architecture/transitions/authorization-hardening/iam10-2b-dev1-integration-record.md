# IAM-10.2b: DEV-1 integration (master merged into the IAM line)

| | |
|---|---|
| Package | Integration of the DEV-1 development-loop work (SQL execution honesty, source-corpus and determinism fixes, artifact retention, the validation toolkit, development guidance, the build-protocol experiment) into the IAM-10.2b line, before WP4B-C. Not WP4B-C and not WP5. |
| Branch | `integration/iam10-2b-dev1`, from the IAM baseline `6e3ab57b897f0c8c09cdb58664d9694a7f80d057` (WP4B-B). The protected `feat/iam-10.2b-administrator-viability` is **not advanced** by this package. Not pushed, no PR. |
| Master side | `origin/master` `be0737d5219b659ad87065f72ab40982e32867ea` (PR #48 merged; both required checks green on that commit). |
| Status | Written in two parts. This part (the commit that introduces this file) holds the identity of the merge, the compatibility findings and the review-record reconciliation. The validation results are added by a later record-only commit. |

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
