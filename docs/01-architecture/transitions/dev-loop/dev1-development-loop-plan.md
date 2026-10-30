# DEV-1: Development-loop improvement plan

| | |
|---|---|
| Status | **Plan. DEV-1.0 is delivered; every other package is a recommendation that starts only on an explicit owner instruction** (the same rule as [RC1 gate](../../rc1-program-gate.md) section 9). |
| Date | 2026-10-29 |
| Nature | Cross-cutting, bounded workstream for the cost, speed and honesty of the development loop. **Not an RC1 epic, not a gate, and not on the RC1 critical path.** |
| Evidence | [Development-loop audit, 2026-10-29](../../../audits/dev-loop-audit-2026-10-29.md) (historical record; the report is preserved verbatim there). Finding numbers below ("F1" to "F26") are the report's register numbers in its section G. |
| Roadmap entry | [Pre-release product roadmap](../../../00-overview/pre-release-product-roadmap.md), "Development loop (DEV-1)". |
| Changes to existing contracts | None. The RC1 definition, the Decisions, the epic sequence and every product dependency in the RC1 gate are unchanged. |

## 1. Purpose and boundaries

The audit found the development loop **fit for RC1, optimizations recommended**: the suites are effective, and the cost is mostly infrastructure and reporting honesty. DEV-1 collects the follow-up work in one place so it is scheduled, owned and verified, instead of being rediscovered in each work-package record.

**What DEV-1 may do.** Fix tests that can pass without checking anything; make local and CI runs cheaper and more diagnosable; track the validation tooling that review depends on; correct dev-loop guidance and test-target safeguards; preserve the checkpoints and evidence reviews rely on.

**What DEV-1 may not do.**

- Change product behaviour, the workflow model, or any RC1 Decision.
- Weaken or remove an assertion, a required CI check, or a Tier 4 gate (audit section H). Every test change in DEV-1 is proven by the break it catches (audit K, rows 1, 3, 14).
- Add a hard dependency from an RC1 epic or package to a DEV-1 package. No RC1 requirement is satisfied by DEV-1 completing.
- Restructure the runtime or the test projects beyond what a package states. Module and process isolation, backend portability and single-tenant instance rules (AGENTS.md rules 2 to 5) are untouched.

**Ownership boundaries.**

| Concern | Owner | How DEV-1 relates |
|---|---|---|
| Release-candidate test assurance: honest SQL tests, one real end-to-end run test, browser smoke, accessibility baseline, upgrade test, operator smoke runbooks, whole-product security review (RC1 requirement 8; [RCV-1](../../rc1-program-gate.md)) | **RCV-1** | DEV-1.1 gives developers and CI the mechanism early (fail instead of silently pass). That is an **input**. RCV-1 still verifies requirement 8 on the release candidate itself and does not inherit DEV-1's result. DEV-1 builds no upgrade, restore or end-to-end release test. |
| Operational deployment protections: Windows Service hosting, health, explicit migration, backup and restore, first-admin hardening, release engineering, `CHANGELOG.md` (OPS-1.7), L-D1 (OPS-1.4) | **OPS-1** | DEV-1's bundles and test-target safeguards protect **developer checkpoints and developer databases**. They are not the product's backup and restore, and they add nothing to OPS-1's scope. DEV-1.5 is limited to test-job diagnostics and reliability; release and packaging pipelines stay with OPS-1, and the two coordinate on any shared `ci.yml` edit. |
| Product observations the audit made in passing (F26) | Product owners | Routed out. Not DEV-1 work. |

## 2. DEV-1.0: audit acceptance, provenance and backups (delivered)

DEV-1.0 changed no product source, test, workflow, dependency or runtime service. It added documents, backup files and one worktree.

**Checkpoint.** `feat/iam-10.2b-administrator-viability` at `6e3ab57`, 959 commits, clean tree, no upstream. WP4B-A, WP4B-D and WP4B-B have passed independent review; WP4B-C and WP5 are not started.

**Backup inventory after reconciliation** (all on D: and G:; nothing older was overwritten or removed):

| Directory (under `D:\Backup\Code\` and `G:\Backup\`) | Contents | State |
|---|---|---|
| `Apperture-Core-git-bundles` | branch at `a96f172` (pre master sync), 2026-10-28 | Existing. The audit saw only this directory. |
| `Apperture-Core-git-bundles-wp4b-b` | branch at `478ae7a`, 2026-10-29 04:40 to 04:41 | Existing, **not seen by the audit**. Verified complete history, head `478ae7a`, and SHA-256 matches the recorded sums. Covers `c5062ba` as an ancestor. |
| `Apperture-Core-git-bundles-dev1` | branch at **`6e3ab57`**, `iam-10.2b-administrator-viability-pre-dev1-6e3ab57-2026-10-29.bundle`, 18,195,611 bytes | **New.** SHA-256 `8e0e9f936e6b062c9fc229311a0cd850d5d7c997842e79623abc265a1364298c`, identical on both drives, recorded in `SHA256SUMS-2026-10-29.txt` beside each. |
| `Apperture-Core-audit-evidence` | audit evidence folder as a zip, 16,738,536 bytes | **New.** See the audit record's inventory. |

**Verification of the new bundle.** `git bundle verify` passes on both copies and reports a complete history; `git bundle list-heads` shows `6e3ab57b897f0c8c09cdb58664d9694a7f80d057`; a clone from the G: copy checks out at `6e3ab57` with 959 commits, the same tree hash as the live branch (`3468ba2e9fa757e1ae9497b2ff9191bca8a81873`), 3,619 tracked files, and a clean `git fsck`. The clone is in the ignored folder `artifacts/dev1-0-restore-check/` and may be deleted at the owner's convenience.

**Preserved untouched.** The IAM checkout, the four other worktrees (`artifacts/v94`, `wt-ci-ubuntu24`, `wt-dotnet-security`, `wt-post-smoke`), the master stash, `.env` files, the dev and test databases and containers, and every `artifacts/` folder.

**Observed, not changed.** `.git/index.lock` (0 bytes, 2026-10-29 12:15, no git process running) is stale and is the trap recorded after the 2026-10-28 merge. Bundling and creating a worktree do not need it, so it was left. Remove it before the next index-writing operation in the IAM checkout.

**Residual backup gaps** (owner decisions, section 9): both drives are local external drives, so there is no off-machine copy; the branch has no remote; the `.bak` database backups have no checksums (audit F2).

**Acceptance for DEV-1.0** (met): bundle verified on both drives with head, checksum and isolated restore; audit record contains the original report with a matching SHA-256; this plan and the roadmap entry added; no tracked file outside `docs/` changed; the IAM checkout still at `6e3ab57` and clean.

## 3. How readiness is stated

| Readiness | Meaning |
|---|---|
| **Delivered** | Done and verified. |
| **Ready on instruction** | The finding is verified (audit class V), the fix is specified, and no measurement or ruling is missing. Starts when the owner says so. |
| **Benchmark first** | The finding is an SH or UO, or its payoff is an estimate. The package's first step is the measurement; the result decides whether to proceed. |
| **Owner ruling** | A product, security or repository-owner decision is needed before work. |
| **Later** | Scheduled alongside or after RC1, or on a named trigger. |

Throughput figures in this plan are the audit's **targets**, not results. They are accepted only after the package's own before-and-after measurement.

## 4. Packages and audit-finding map

| Package | Title | Findings | Position relative to IAM-10.2b WP4B-C |
|---|---|---|---|
| DEV-1.0 | Audit acceptance, provenance and backups | F2 | Done |
| DEV-1.1 | Test evidence integrity and environment separation | F1, F15, F14 (honesty part) | Before |
| DEV-1.2 | Local feedback performance and deterministic tests | F3, F14 (determinism part), F16 | Before |
| DEV-1.3 | Reproducible validation harness and validation tiers | F7, F8, F19 | Developed alongside; adopted at the WP4B-C boundary |
| DEV-1.4 | SQL fixture and retry efficiency | F4, F5, F6 | F4 and F5 alongside; F6 after the audit owner agrees |
| DEV-1.5 | CI diagnostics and reliability | F9, F10, F11, F25 | F9 alongside; the rest after |
| DEV-1.6 | Longer-term test and infrastructure improvements | F12, F13, F17, F18, F20 to F24 | Alongside RC1 or after, by trigger |
| (routed out) | Product observations | F26 | Product owners |

All 26 findings are mapped once (F14 and F16 are split as shown). Per-finding detail:

| F | Finding (short) | Class | Package | Readiness |
|---|---|---|---|---|
| 1 | Honest SQL outcomes; require-SQL mode in CI | V | 1.1 | Ready on instruction; the `ci.yml` line needs owner consent (required checks) |
| 2 | Back up the branch head | V | 1.0 | Delivered. Repeating it after each accepted package is a 1.3 checklist step; `.bak` checksums remain open |
| 3 | Stop walking build output; shared pruned source corpus | V | 1.2 | Ready on instruction (code). Deleting in-place `bin`/`obj` and stray trees is a separate owner-confirmed local action |
| 4 | Clear the pool before `ALTER`/`DROP`; consolidate scratch helpers | SH | 1.4 | Benchmark first |
| 5 | Align test retry policy with production | V | 1.4 | Ready on instruction |
| 6 | Avoidable catalogs; per-class catalogs with reset | V | 1.4 | Owner ruling (audit owner, per class) |
| 7 | Validation build flags and builds | SH | 1.3 | Benchmark first; protocol change needs owner approval at a package boundary |
| 8 | Track the validation harness | V | 1.3 | Ready on instruction |
| 9 | CI diagnostics: TRX and blame uploads, timeout, SDK pin | V | 1.5 | Ready on instruction (`ci.yml`) |
| 10 | Shard IntegrationTests | SH | 1.5 | Benchmark first; needs 1.1 |
| 11 | Run CI on the feature branch | SH | 1.5 | Owner ruling |
| 12 | Executable composition tests | V | 1.6 | Later (alongside RC1) |
| 13 | HTTP boundary coverage for untested actions | SH | 1.6 | Later (alongside RC1) |
| 14 | Small honesty fixes | V | 1.1 (cannot-fail and vacuous-pass tests), 1.2 (flakes, order dependency, timing windows) | Ready on instruction |
| 15 | Operational separation guard for test targets | V | 1.1 | Ready on instruction |
| 16 | Artifacts retention rule and tiered cleanup | V | 1.2 | Owner ruling (deletion); tier 2 now unblocked by 1.0 |
| 17 | Dead code and inventory | V | 1.6 | Ready for no-ruling items; owner ruling for placeholders, legacy routes, `poc/` |
| 18 | Pin consolidation and UI hygiene | V | 1.6 | Later; only after F12 |
| 19 | Guidance and templates | V | 1.3 | Ready on instruction; the agent-file corrections can be pulled forward as a docs-only slice |
| 20 | Scaffolding consolidation | V | 1.6 | Later (opportunistic) |
| 21 | Build warnings | V | 1.6 | Later (alongside RC1) |
| 22 | Git housekeeping | V | 1.6 | Owner ruling |
| 23 | Template catalog for SQL tests | UO | 1.6 | Benchmark first; Later |
| 24 | Parallel SQL collections | UO | 1.6 | Benchmark first; Later |
| 25 | Docs-only CI classification | SH | 1.5 | Owner ruling; Later (see section 9, item 6) |
| 26 | Product-side observations | SH/UO | routed out | Product owners |

Counts in the audit (for example 46 soft-skipping tests) were taken at `6e3ab57`, which includes IAM-branch test classes that `master` does not have. **Every package re-counts on its own base before it changes anything.**

## 5. Package detail

### DEV-1.1 Test evidence integrity and environment separation

- **Policy decision (owner, 2026-10-30, supersedes the F1 wording below and the "require mode" wording in this package's acceptance).** SQL-dependent tests **fail whenever their intended SQL execution cannot occur. Optional SQL mode is retired. A test may never report Passed solely because SQL was unavailable.** `APPERTURE_REQUIRE_SQL_TESTS` is therefore retired too: it is no longer read, setting it changes nothing, and DEV-1.1C must **not** add it to `ci.yml`. The evidence mode is the only mode. Two independent defences enforce it: the source guard `SqlSoftSkipGuardTests` (cheap, lexical, not exhaustive) and the run-level proof `scripts/dev/Test-SqlExecutionHonesty.ps1`, which runs the IntegrationTests assembly against an unreachable server and refuses any Passed test not on the reviewed non-SQL allow-list (`tests/Apperture.IntegrationTests/SqlExecutionHonesty/`). Delivered by DEV-1.1B (`ad3bafc`, which introduced the interim required/optional switch) and DEV-1.1B-R (which retired it after the independent review's M-1 and M-2). Developer description: `tests/README.md`.
- **Scope.** F1: ~~set `APPERTURE_REQUIRE_SQL_TESTS=1` in both CI jobs~~ (superseded: there is no switch; strict behaviour is unconditional); route the remaining soft-skipping guards through the existing require-SQL helpers (done: they now fail, and `SqlServerScratchDatabase.CreateAsync` never returns null); remove the LocalDB fallbacks (done); add a source guard so a new soft-skip fails (done, plus the run-level proof). F15: catalog allow-list (`Apperture_` prefix, never the bare `Apperture` catalog; governance catalogs must end in `Tests`), refuse port 14333 without an opt-in, fix the `.env` overwrite in `scripts/dev/lib/DevDockerSql.ps1`, delete inert paths, correct READMEs. F14 honesty part: DPAPI early returns, the unawaited leak assertion, the no-op governance launch test, OR-assertions that accept a 500, the tautological marker and constants, Scheduling zone returns.
- **Prerequisites.** None technical. Owner consent for the `ci.yml` change. A re-count of soft-skip sites on the chosen base.
- **Acceptance** (audit K rows 1, 14, 15, with section 8 below).
  - With the SQL target unreachable, every recounted soft-skip test fails with the configuration message and none reports Passed; `Test-SqlExecutionHonesty.ps1` verifies this for the whole assembly (the passes are exactly the reviewed non-SQL allow-list).
  - With SQL present, the pass count equals the baseline, none of those classes is skipped, and no `SqlServerTests` case runs under 100 ms.
  - Four configuration tests pass: bare `Apperture` refused; governance catalog needs the `Tests` suffix; port 14333 refused without opt-in and accepted with; the `.env` merge keeps both keys.
  - One `SqlServerTests` class still creates catalogs on the test container (positive control).
  - A synthetic soft-skip class fails the new source guard.
  - Each F14 fix is shown by the break it catches (leak the reference, reject all documents, add a label to a seed, invariant globalisation).
  - If CI shows reachability failures confined to these classes, add a probe retry. Never restore a soft skip.
- **Independent review.** Yes, a short one: the reviewer re-runs the unreachable-target and mutation checks on their own build. The package changes what "Passed" means for identity and audit proofs.
- **Risks.** CI may expose real reachability or environment failures it hid. Test-target refusals may block a developer's habitual connection string. A `ci.yml` edit touches a file `BrowserSecurityCiGuardTests` pins.
- **Expected benefit.** Assurance, not time. A green run becomes evidence. Zero run-time cost.

### DEV-1.2 Local feedback performance and deterministic tests

- **Scope.** F3: one shared pruned source corpus (skips `bin`, `obj` and `artifacts` while descending; a Migrations-inclusive view; a `.razor` view; a non-empty floor), one repo-root helper that accepts a `.git` file (worktree-safe), `artifacts/**` in `DefaultItemExcludes`. F14 determinism part: the `ConnectionToolInvocationTests.I2` lock, bUnit waits after `Click`/`Input`/`Submit`, the xunit-order dependency in `ManagementHostBootstrapHttpTests` A01 to A05, the vacuous timing windows, the assert-less 10 s diagnostic. F16: a retention rule and tiered cleanup of `artifacts/`.
- **Prerequisites.** None for the code. Deleting in-place `bin`/`obj` and the 56 stray trees needs the stack stopped, VS Code closed, and owner confirmation. Cleanup tier 2 needed F2 (now delivered); tier 3 waits for WP4B-C.
- **Acceptance** (audit K rows 3, 14, 16).
  - Per-site diff of enumerated paths before and after shows the same tracked files reached; a synthetic untracked offender still fails each coverage guard; the non-empty floor trips on an empty corpus.
  - UnitTests run from a linked worktree.
  - Locally, UnitTests at or below 70 s and Ui.Shared at or below 8 s with CI times unchanged. Revert if any guard's file count drops unexpectedly.
  - 30 to 50 runs of the I2 class with and without the lock; 20 runs of the bUnit classes.
  - For cleanup: free space rises by the tier; every `*-logs` evidence folder remains (row counts against the audit's file list); no record references a deleted folder.
- **Independent review.** Peer check of the corpus change (it is a coverage-guard change); no security-style review.
- **Risks.** A narrowed corpus silently weakens a guard. Cleanup deletes data. Mitigations are the per-site diff, the floor, and owner-confirmed tiers.
- **Expected benefit (target).** Local UnitTests 159 s toward 50 to 70 s; Ui.Shared 51 s toward 3 to 5 s; agents' search tools stop timing out; one known flake removed.

### DEV-1.3 Reproducible validation harness and validation tiers

- **Scope.** F8: track `preflight.ps1`, `Invoke-Evidence.ps1`, `Compare-Trx.ps1`, `Invoke-Mutation.ps1`, the test manifest and selector, and promote `sqlmarkers`/`sqlwindow` and the focused filters; an evidence layout with an off-machine archive step. F7: the validation build protocol (drop `-m:1` and the server flags; one solution build; a persistent per-branch path for iteration). F19: a dev-loop playbook, work-package record and independent-review templates, corrected agent and Cursor guidance (AGENTS.md rules 3 and 4), `scripts/ci/test.ps1` and CONTRIBUTING. The validation tiers (audit section H) are documented here, with these rules: Tier 1 never counts as evidence; Tier 2 runs only after preflight and in require-SQL mode; both isolation modes in Tier 2 evidence; Tier 3 is the entry condition for independent review; Tier 4 is never narrowed.
- **Prerequisites.** DEV-1.1 (the harness assumes require-SQL mode) and F2. The protocol change is adopted at a package boundary and stated in that package's record.
- **Acceptance** (audit K rows 7, 8).
  - `Invoke-Mutation` reproduces the WP4B-B caught and escaped counts.
  - `Compare-Trx` over the WP4B-D and WP4B-B logs reports the +36 new cases, the two start-order failures and the stated rcsi split.
  - The manifest helper, given the WP4B-A diff, names the six classes that filter missed.
  - Build experiment: four clean builds (protocol, `-m:1` with servers, `-m:4` with servers, defaults) with memory sampled show 71 warnings, 0 errors, no node crash, trough above 500 MB, at least 35% wall saving, and identical assembly hashes flagged versus unflagged; one mutation rerun identical.
  - Evidence hashes of accepted packages are untouched.
- **Independent review.** **Required.** This package changes how independent reviews are done (audit E.7: verify hashes and TRX first, re-run new and changed classes plus at least one novel mutation per claimed property, no blanket repeat of the full focused set when the first step passes). Review-protocol changes need the security owner's agreement.
- **Risks.** A new tool becomes the single point of trust and must itself be mutation-checked. Changing the protocol mid-package breaks comparability. The audit's ignored-folder scripts are forked, so the lineage to promote must be chosen deliberately.
- **Expected benefit (target).** 0.5 to 1 h per record saved, one review rerun avoided per package, 15 to 20 min per package plus 14 to 21 min per evidence set from builds.

### DEV-1.4 SQL fixture and retry efficiency

- **Scope.** F4: clear the catalog pool before every `ALTER DATABASE ... WITH ROLLBACK IMMEDIATE` and `DROP`; `ClearPool` instead of `ClearAllPools`; consolidate hand-rolled scratch copies. F5: align the test retry policy with production (`EnableRetryOnFailure(3)`) and assert attempts. F6: lazy `InitializeAsync`, per-class catalogs with per-case reset for single-connection IAM cases, a shared hosts fixture, single-mode order theories.
- **Prerequisites.** DEV-1.1 and DEV-1.3 (Compare-Trx is the proof instrument). For F6, the audit owner of each class agrees its list. Fixture edits that touch IAM-branch-only classes wait for those classes to reach `master` or are coordinated at the WP4B-C boundary.
- **Acceptance** (audit K rows 4, 5, 6).
  - Same assertions; unique catalog markers; the retained WP4B-B B3, B8, B10 and WP4B-D M1, M4 mutations still fail; reverting the pool clear restores the roughly 3 s signature.
  - F4 is kept if the moved classes reach the scratch-helper floor in CI and a focused run loses about 5 min.
  - F5: the L1 attempt-count assertions are unchanged, the Deadlock cases still throw the captured `SqlException` on both modes, the WP3 swallowed-failure mutation still fails, and faults equal to `MaxRetryCount` plus 1 fail while equal to `MaxRetryCount` succeed. Kept if the cases take under 10 s.
  - F6: class walls from TRX; a control that skips the per-case reset fails; no absolute audit count on a shared catalog; revert any class whose mutation catch set shrinks.
- **Independent review.** Yes for F6 (it changes isolation between audit proofs). F4 and F5 are covered by the mutation and attempt-count checks plus a peer review.
- **Risks.** Shared catalogs can hide cross-test interference. Process-wide `ClearAllPools` and `PauseAt.ReleaseAll` must not leak into any later parallelisation (F24). The F4 saving is a hypothesis until measured.
- **Expected benefit (target).** F5 about 2 min per focused run; F4 about 5 min per focused run and 8 min of the CI step; F6 8 to 12 min per focused run. Together the focused SQL run from 42 min toward 20 to 25 min.

### DEV-1.5 CI diagnostics and reliability

- **Scope.** F9: upload TRX and blame-hang output `if: always()`, minimal verbosity, `timeout-minutes`, SDK from `global.json`, a Playwright cache-key guard, browser failure capture, a non-blocking UI-evidence dispatch job with an owner. F10: shard IntegrationTests by explicit class list with a remainder-by-negation shard, a fan-in check named `build-and-test`, `TreatNoTestsAsError` and an executed-count assertion per shard. F11: run CI on the feature branch before merge. F25: docs-only classification.
- **Prerequisites.** F9 none. F10 needs DEV-1.1 and a before-measurement. F11 and F25 need owner rulings. Required checks stay `build-and-test` and `browser-security` under the existing ruleset.
- **Acceptance** (audit K rows 9, 10).
  - F9: a forced failure and a forced hang on a scratch branch each yield an uploaded TRX and a dump; a `Directory.Packages.props` version bump without a `ci.yml` edit fails the cache guard.
  - F10: the union of shards equals the pre-shard executed count; the fan-in check fails on any non-success; `BrowserSecurityCiGuardTests` passes; kept if the 10-run median is 24 min or less with shards within 25% of each other, reverted on recurring shard-environment failures.
- **Independent review.** The CI changes get a second reader; F11 and F25 need the repository owner.
- **Risks.** A mis-specified fan-in check can let a failing shard pass. Sharding raises queue and cost. Docs-only classification can contradict the "no selective runs" rule (section 9, item 6).
- **Expected benefit (target).** One-pass diagnosis of any CI failure; CI run from about 38 min toward 20 to 23 min with two shards.

### DEV-1.6 Longer-term test and infrastructure improvements

- **Scope and timing.** None blocks anything. In rough order: F12 (executable composition tests, then retire nine text pins, never before the counterpart test fails its mutation); F13 (table-driven 401/403 for the 158 untested actions, HTTP cases for the two mock-only guards, SQL race tests for three unique-index paths); F21 (build warnings, then `WarningsAsErrors` for those codes); F17 (dead code and inventory: no-ruling items first); F18 (pin consolidation, only after F12); F22 (git housekeeping, owner confirmation); F20 (opportunistic); F23 (template catalog) and F24 (parallel SQL collections) only if their benchmarks say so.
- **Acceptance.** Audit K rows 12, 13, 17 to 22, 23: each kept protection is broken once and fails exactly one test; build and ArchTests pass; `git grep` for removed names is empty. F23 is kept only if restore is under 1.5 s and the focused run loses at least 10 min after F4 and F6; otherwise it is dropped.
- **Independent review.** F12 and F13 add security-relevant tests and get the security owner's review of the cases. The rest are peer-reviewed.
- **Risks.** Retiring pins removes coverage if done in the wrong order. F24 depends on removing process-wide side effects first.
- **Expected benefit.** Defects caught in milliseconds instead of by review (F12); closing the most consequential boundary gaps (F13); lower maintenance load.

## 6. Recommended sequence

None of this blocks WP4B-C. The one ordering preference: do DEV-1.1 first, so WP4B-C's evidence is produced in require-SQL mode.

| When | Package | Order and reason |
|---|---|---|
| Done | DEV-1.0 | Backup and provenance. |
| Before WP4B-C | DEV-1.1 | Honest SQL execution and test-target safeguards (audit P1). Under a day, no run-time cost. |
| Before WP4B-C | DEV-1.2 | Source-scanning efficiency and the I2 flake (audit P2). The code fix does not need the destructive cleanup. |
| Alongside WP4B-C, adopted at its boundary | DEV-1.3 | Tracked evidence and mutation tooling, then the protocol change (audit P3). WP4B-C is the heaviest review in the plan; its evidence should be reproducible. Developed on its own branch and adopted into the IAM branch by a merge from `master`. |
| Alongside WP4B-C | DEV-1.4 (F4, F5) | Measured SQL fixture optimizations (audit P4). Each needs its before-and-after measurement. |
| Alongside WP4B-C | DEV-1.5 (F9) | CI reporting (audit P5): one-pass diagnosis of the first full-suite run of the IAM branch. |
| After WP4B-C, alongside RC1 | DEV-1.4 (F6), DEV-1.5 (F10, F11), DEV-1.6 (F12, F13, F17, F18, F21) | After their prerequisites and rulings. F10 needs F1. |
| After RC1 or on trigger | DEV-1.5 (F25), DEV-1.6 (F20, F22, F23, F24) | Owner rulings or benchmarks first. |

Dependencies, from the audit: DEV-1.1 before F10; F2 (done) before F16 tier 2; F1 and F8 before any review-protocol change; F12 before retiring any text pin; the protocol change at a package boundary, stated in the record.

Each package's first act is a short plan stating its base commit, re-counted scope and measurement method.

## 7. Branch and integration approach

The IAM branch is local-only and carries unmerged work. DEV-1 work must not ride on it.

- **This documentation package** is on branch `docs/dev-1-development-loop-roadmap`, created from the local `origin/master` ref (`28fd63b`, the PR #38 merge of 2026-10-28; no fetch was run) in the worktree `F:\Code\Apperture\wt-dev1`. It has **no upstream**, so a bare `git push` cannot target `master`. It was not pushed. The IAM checkout was not touched.
- **Implementation packages** each get their own branch from the current `origin/master` and their own sibling worktree, following the existing `wt-*` convention (for example `dev/dev-1-1-honest-sql-outcomes` in `wt-dev1-1`). Never branch from `feat/iam-10.2b-administrator-viability`.
- **Before any PR**, run `git log --oneline origin/master..HEAD` (only DEV-1 commits) and `git merge-base --is-ancestor 478ae7a HEAD` (must be false). A PR targets `master` and never the IAM branch.
- **Getting DEV-1 work into the IAM branch** happens only by merging `master` into it after the DEV-1 PR has merged, as was done for the dependency fix at `c5062ba`, at a package boundary and on the owner's instruction. No cherry-picks, so the history stays honest.
- **Conflict note.** DEV-1.1, 1.2 and 1.4 edit test files the IAM branch also grows. Expect conflicts in `IntegrationTests` fixtures. After each merge from `master`, re-run the new source guard (DEV-1.1) so IAM-only test classes that arrived with a soft skip are caught.
- **Fetch.** The base is the local `origin/master` ref. Fetch before cutting an implementation branch; any rebase or retarget is a separate owner-approved step.
- **Alternatives rejected.** Stacking DEV-1 on the IAM branch (a PR would carry unmerged IAM commits) and doing DEV-1 work directly on the IAM branch (contaminates review evidence).

## 8. Verification kept from the audit

Every package keeps the audit's section K proof for its findings, as summarised in section 5. Common rules:

- Instruments: `Compare-Trx` over a baseline and a candidate TRX once it exists (before then, per-class counts); the mutation harness re-applied to the retained WP4B mutations; the SQL execution proof (catalog start-ups in the server log, per-catalog markers); a deliberate break of the protected property.
- **Hidden skips** after any change: flag `SqlServerTests` cases under 100 ms, report skipped and not-executed lists, compare per-class counts; there is no skip to hide behind (the policy decision in section 5 retired optional mode); the per-run catalog count from the server log stays within the predicted range.
- **Unreachable SQL is simulated with an unreachable connection string, not by stopping the shared test container.**
- Nothing is removed from the required CI gates, and the audit's rejected options stay rejected: a test-impact-analysis service, enforced selective runs in CI, an orchestration platform, and anything that lets a change reach `master` with less than the full IntegrationTests project (audit E.9).

## 9. Unresolved owner decisions

1. **Start and order.** Which DEV-1 package starts next, and whether DEV-1.1 and DEV-1.2 precede WP4B-C (recommended: yes, in that order).
2. **`ci.yml` consent.** Running `scripts/dev/Test-SqlExecutionHonesty.ps1` as a required check (F1; the variable `APPERTURE_REQUIRE_SQL_TESTS=1` it once named is retired and must not be added) and the diagnostics edits (F9) change a required check's workflow.
3. **Destructive local cleanup.** Deleting in-place `bin`/`obj`, the 56 stray trees and old `artifacts/` build folders (F3, F16). Needs the stack stopped, VS Code closed, and a tier chosen. Nothing was cleaned in DEV-1.0.
4. **CI on the feature branch (F11).** Requires pushing a branch that has none, with cost and exposure implications.
5. **Validation protocol change (F7).** Adopt the new build flags and review protocol at a named package boundary, with the security owner's agreement.
6. **Docs-only CI classification (F25).** It sits uneasily with the audit's own Tier 4 rule that required jobs are never narrowed and E.9's rejection of selective runs. Recommend deferring past RC1 and ruling explicitly that it is not selective running.
7. **Per-class catalog changes (F6).** The audit owner's agreement for each class.
8. **Inventory rulings (F17).** Placeholder projects, legacy redirect routes, `poc/` archival; the `CHANGELOG.md` item belongs to OPS-1.7.
9. **Git housekeeping (F22).** Worktrees, merged local branches, a stale local `master`, the master stash, the tracked Cursor debug log.
10. **Off-machine backup.** Whether to add a remote or an off-site copy for the IAM branch, and checksums for the `.bak` files. Both drives are local.
11. **This documentation branch.** Whether to open a docs-only PR to `master` now or hold it with the IAM merge.
12. **Stale lock.** Permission to remove the 0-byte `.git/index.lock` in the IAM checkout (verify no git process first).

## 10. Proposed first implementation package: DEV-1.1

Chosen because it is the audit's first priority after backup, is under a day, has no run-time cost, and lets WP4B-C's evidence be produced under the require-SQL mode.

Slices, each its own commit: **A** separation guard (F15: allow-list, port refusal, `.env` merge fix, README corrections, four configuration tests); **B** honest outcomes (F1 test side, then F14 honesty fixes); **C** the `ci.yml` line, last and only with consent.

Acceptance tests, in addition to those listed in section 5:

1. Recount: the list of soft-skip sites on the base commit is recorded before any change.
2. Unreachable-target run (the honesty check): all recounted tests fail with the configuration message; zero Passed among them.
3. Reachable run: unchanged pass count, nothing skipped in those classes, no case under 100 ms.
4. The four configuration tests and the positive-control class pass.
5. Mutation: re-introducing each removed early return, LocalDB fallback or allow-list hole makes the guard or a test fail; record each.
6. Source-guard negative control: a synthetic soft-skip class fails it; removing it passes.
7. No assertion line is weakened (the diff review lists each assertion touched).
8. Slice C: `BrowserSecurityCiGuardTests` passes and the first CI run shows failures confined to the recounted classes or none.
9. UnitTests and ArchTests pass; the working tree is clean afterwards.

## 11. RC1, RCV-1 and OPS-1 consistency

- RC1 definition, Decisions D-01 to D-24, the epic sequence and the hard dependencies are unchanged. DEV-1 appears in the roadmap as a cross-cutting workstream and does not appear in the gate's sequence diagram.
- No RC1 requirement lists a DEV-1 package as a prerequisite, and DEV-1 completion closes none of them.
- RCV-1 keeps requirement 8 and the whole-product security review. DEV-1.1 and DEV-1.3 are inputs to RCV-1's evidence quality; RCV-1 re-verifies.
- OPS-1 keeps deployment, backup and restore of instances, release engineering and `CHANGELOG.md`. DEV-1.5 does not touch release pipelines.
- AGENTS.md non-negotiables hold. In particular DEV-1.3's guidance corrections bring `.ai` and `.cursor` files into line with rules 3 and 4, and nothing here adds internal planning labels to runtime-visible content (rule 6).
