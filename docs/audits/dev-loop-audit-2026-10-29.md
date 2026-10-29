# Development-loop audit, 2026-10-29 (historical record)

| | |
|---|---|
| Status | **Historical audit record. Evidence, not a decision.** Nothing here is a Decision, Direction or ruling under the [RC1 program gate](../01-architecture/rc1-program-gate.md) until the owner records it as one. |
| Audited checkpoint | HEAD `6e3ab57` on `feat/iam-10.2b-administrator-viability` (local-only branch), 2026-10-29 |
| Nature | Read-only audit by one lead agent and fourteen parallel investigators, with an adversarial verification pass on the five backbone claims. No tracked file, branch, backup, database or service was changed. |
| Accepted into the program by | DEV-1.0 (this record's preservation), 2026-10-29. Scheduling and acceptance criteria: [DEV-1 plan](../01-architecture/transitions/dev-loop/dev1-development-loop-plan.md). |
| Original report | Preserved verbatim at the end of this file, under "Original report". SHA-256 of the original file `report.md`: `7755fc75c5495639f7f217067bcce56da0871a457596cf67e95f1f0bb7257423`. |

## How to read this record

- The report grades each finding **V** (verified: observed in code, logs or a run made during the audit), **SH** (supported hypothesis: grounded but not measured) or **UO** (unverified opportunity: needs a measurement first). That grading is kept as written. A throughput figure for an SH or UO row is a target, not a result.
- Historical timings are labelled in the report. They describe one memory-constrained developer machine and one CI run (master run `40687969424`) and will drift.
- The verdict, **development loop fit for RC1, optimizations recommended**, is the audit's opinion. It does not close any RC1 requirement. In particular it does not replace RCV-1 (honest verification, RC1 requirement 8) or OPS-1.
- Where the report and the [DEV-1 plan](../01-architecture/transitions/dev-loop/dev1-development-loop-plan.md) differ on scope or order, the plan governs. Where the report and the RC1 gate differ, the gate governs.
- The report cites local paths under `artifacts/` and a hosted living copy. Neither is a portable reference. See the evidence inventory.

## Provenance and evidence inventory

The supporting data lives in the git-ignored folder `artifacts/audit-2026-10-29/` on the audit machine. It is not committed: it holds about 175 MB of generated logs, TRX-derived data and agent output. DEV-1.0 preserved it off-repo instead.

| Item | Where | Notes |
|---|---|---|
| Original report | Below in this file; also `artifacts/audit-2026-10-29/report.md` (local, ignored) | Verbatim. A hosted living copy is named in the report's first paragraph; it may have been edited since and is not the preserved version. |
| Off-machine evidence archive | `D:\Backup\Code\Apperture-Core-audit-evidence\dev-loop-audit-2026-10-29-evidence.zip` and the identical copy at `G:\Backup\Apperture-Core-audit-evidence\` | 472 zip entries (466 files), 16,738,536 bytes. SHA-256 `a35a46cf2a347c8c9ec27aeb27e06326c2bf4db8985f2faf0ece6ae9223c2d6f`, recorded in `SHA256SUMS-2026-10-29.txt` beside each copy. Created 2026-10-29 by DEV-1.0; the folder was copied, not modified. |
| `ci-logs/` | in the archive | GitHub Actions logs and per-test CSVs for master run `40687969424` (4 files, about 23 MB). The run itself is the primary source while GitHub retains it. |
| `local-unittests-wp4bb.csv`, `local-integration-focused-wp4bb.csv` | in the archive | Per-test local timings at `6e3ab57` (about 1.9 MB and 0.5 MB). Produced by `parse-trx.ps1` from TRX files in ignored `artifacts/` folders that are not in the archive. |
| `parse-trx.ps1`, `scan-artifacts.ps1` | in the archive | The audit's two measurement scripts (about 4 KB and 3 KB). Untracked. DEV-1.3 decides which become tracked tools. |
| `flake/`, `guard-timing/`, `restore-probe/` | in the archive | Flake reproduction runs (42 files), guard-class timings (4 files), NuGet restore timings (322 files, about 23 MB). |
| `artifacts-sizes.txt`, `artifacts-sizes.failed-robocopy.txt`, `evidence-files.csv` | in the archive | Inventory of the ignored `artifacts/` tree at the audit date (sizes, evidence-file list). Point-in-time. |
| `lead-findings.md`, `verify-lead-verdicts.md` | in the archive | The lead's working findings and the adversarial verdicts on the five backbone claims (about 7 KB and 169 KB). |
| `agents/` | in the archive | Raw output of the fourteen investigators (84 files, about 127 MB). Treat as working material; it has not been reviewed line by line. |

To use the archive, extract it into a new empty folder and read the files there. Do not extract into `artifacts/`, and do not treat the extracted paths as repository paths.

## Reconciliation at acceptance (DEV-1.0, 2026-10-29)

DEV-1.0 re-checked the report's checkpoint claims against the repository and the backup drives. The report's own text below is unchanged; this section is the correction of record.

| Report statement | Finding at acceptance |
|---|---|
| Scope table, "Backups": bundles hold the branch at `a96f172` only; `c5062ba`, `478ae7a` and `6e3ab57` exist nowhere else. Register item 2, section A item 4 and sections K and L repeat it. | **Partly incorrect.** The audit inspected only the original bundle directories (`Apperture-Core-git-bundles`). Sibling directories `Apperture-Core-git-bundles-wp4b-b` on D: and G: already held bundles of `478ae7a` (created 2026-10-29 04:40 to 04:41, before the audit). Both verify as complete history and match their recorded SHA-256 sums. `c5062ba` is an ancestor of `478ae7a`, so it was covered. The only unbacked commit was `6e3ab57`. |
| HEAD `6e3ab57` is not in any bundle. | **Correct at the audit.** Closed by DEV-1.0: new bundles on both drives, verified (see the DEV-1 plan, section 2). |
| Four extra worktrees, one stash on master, local `master` 81 commits behind, branch has no upstream. | Re-confirmed on 2026-10-29: four extra worktrees (the audit named `artifacts/v94`, `wt-ci-ubuntu24`, `wt-dotnet-security`, `wt-post-smoke`; a fifth, `wt-dev1`, was added by DEV-1.0), one stash, branch local-only. |
| `.ai/agents` and `.cursor` guidance contradicts AGENTS.md rules 3 and 4; a Cursor debug log is tracked. | Spot-checked: `.ai/agents/qa-agent.md` line 8 still asks for DTFx and Durable Functions adapter validation, and `.cursor/debug-4bd3d8.log` is tracked. |
| Everything else (all timings, counts, test-weakness rows and cost estimates) | **Not re-verified by DEV-1.0.** Each carries the report's own evidence class. Each DEV-1 package re-checks the findings it depends on before it changes anything. |

One further observation, not in the report: the two `478ae7a` bundles differ byte for byte (different SHA-256) although both verify and list the same head. Bundle bytes are not reproducible across runs, so a bundle's checksum identifies a file, not a commit. The head listed by `git bundle list-heads` identifies the commit.

---

## Original report (verbatim, unchanged)

The text from the next heading to the end of this file is the original `report.md`, byte for byte.

# Apperture Core Dev-Loop Audit

Date: 2026-10-29. Checkpoint: HEAD 6e3ab57 on feat/iam-10.2b-administrator-viability. Read-only audit; no tracked file, branch, backup, database or running service was changed. Living copy of this report (editable, commentable): https://claude.ai/artifact/2Zrrp2Gj27GTQvmQ5dCr9u. Supporting data: this folder (ci-logs/, parse-trx.ps1, local-*.csv, flake/, guard-timing/, restore-probe/, artifacts-sizes.txt, evidence-files.csv, lead-findings.md, verify-lead-verdicts.md, agents/*.result.json and agents/_digest-batch*.md).

## Scope and checkpoint

The repository is at the expected checkpoint, with one gap: the current HEAD is not in any backup bundle.

| Item | Expected | Observed |
| --- | --- | --- |
| HEAD | 6e3ab57 | 6e3ab57 (WP4B-B configuration attestation), working tree clean |
| Branch | feat/iam-10.2b-administrator-viability | Same; no upstream, exists only in the local .git |
| Last merged master ancestor | 28fd63b | merge-base with origin/master is 28fd63b; the local master ref is stale at 30e760e, 81 commits behind origin/master |
| Backups | Latest HEAD bundled | Bundles on D: and G: (2026-10-28, SHA256SUMS present) hold the branch at a96f172 only; c5062ba, 478ae7a and 6e3ab57 exist nowhere else |
| IAM-10.2b state | WP4B-A, WP4B-D, WP4B-B accepted; WP4B-C and WP5 not started | Confirmed by the WP4B decision record section 9; that record's header still says WP4B-B awaits review (document drift only) |
| Worktrees and stashes | none expected | 4 extra worktrees (artifacts/v94 detached at f319a35, wt-ci-ubuntu24, wt-dotnet-security, wt-post-smoke) and 1 stash on master |
| Machine during the audit | - | 16 vCPU, 32 GB RAM with 0.5 GB free physical and 93% commit charge; Docker Desktop up with apperture-test-sql (1433) and the dev SQL sql-9dfe7faf (14333); no Aspire or Apperture host processes running |

**Method.** One lead agent verified the checkpoint, took the baseline measurements and wrote this report. Fourteen parallel read-only investigators each covered one area (inventory, SQL fixtures, unit, UI and integration test effectiveness, build and CI, browser suite, workflow and tooling, maintainability, the flaky test, artifacts and backups, startup and source pins). The five backbone claims then went through an adversarial verification pass (refute lens and impact lens, ten verdicts) before ranking.

**Bounded experiments run.** Downloaded GitHub Actions logs for master run 40687969424 (no CI re-run). Re-ran the intermittent unit-test class against the existing HEAD build in artifacts/wp4bb-review with --no-build and an isolated results folder (12 class runs, 8 single runs). Re-ran two source-scanning guard classes twice. Timed solution restore into an isolated folder (fresh, then no-op). Timed recursive directory walks of src and tests. Disk scans of the ignored artifacts/ tree. No solution build, no SQL connection, no container start or stop, nothing pushed.

**Evidence classes.** Verified = directly observed in code, logs or a run made during the audit. Supported hypothesis = grounded in evidence but not measured. Unverified opportunity = needs a measurement before commitment. Historical timings from earlier sessions are labelled as such.

## A. Executive assessment

The development loop is fit for continued RC1 development: the test suites are effective and deliberately layered, the security packages they validated are well evidenced, and the overhead found is cost and reporting honesty rather than weak protection. The cost is large and mostly self-inflicted, and most of it can be removed without touching a single assertion. Verdict: DEVELOPMENT LOOP FIT FOR RC1, OPTIMIZATIONS RECOMMENDED (section L).

The five most consequential findings:

1. **The SQL integration suite's cost is catalog lifecycle, and a third of it is invisible.** Each of about 265 to 300 catalogs per focused run (about 600 in CI) is created from nothing through 96 migrations and then killed while pooled connections still hold it; 94 of them are created inside InitializeAsync, which xunit does not time. That is 25 to 29 of the 42 local minutes and about 18 of the 35 CI minutes, and the CI test step is exactly this one serialised assembly. Two small changes (clearing the pool before ALTER and DROP, aligning the test retry policy with production) recover about 10 min per CI run and 7 per focused run with no semantic change; per-class catalogs for the independent IAM cases and a sharded CI job take the rest.
2. **The local loop is three to ten times slower than CI for reasons unrelated to the tests.** Thirty-seven guard tests re-walk a src tree of 69,399 directories, 97% of them stale bin and obj output and stray build trees copied by the Web SDK glob (694 s locally, 6.9 s in CI); the validation protocol builds serially with the compiler server off, a workaround for file locks adopted in September and copied into every record since (4 min 45 s to 10 min 09 s per clean build against 1 min 20 s in CI); every package starts a cold build folder (61 folders, 170 GB, 2.77 million files); and the machine runs with under a gigabyte free. Each of these has a half-day fix.
3. **Forty-six SQL tests can report Passed without SQL Server, and one does in every Linux CI run.** The repository already decided that SQL tests fail rather than skip (RC1 requirement 8, the audit design gate), the switch that enforces it exists, CI does not set it, and 29 of the 46 were written after the policy. Three DPAPI tests likewise pass on Linux without running, one secret-leak assertion is never awaited, and a handful of OR-assertions accept a 500. None of this saves time; all of it is the difference between a green run and evidence.
4. **The harness that makes the security reviews credible is untracked and regressing, and the branch head is not backed up.** Eight forked copies of the mutation script (the newest missing the safeguards the WP4A review mandated), hand-grown filter strings that missed six classes, hand-transcribed evidence, provenance hashes that lapsed after the first WP4B commit, and scripts that only exist in ignored folders mean an independent reviewer off this machine cannot re-run the 45 mutation checks. The two newest accepted packages exist in one local .git; the bundles stop at a96f172.
5. **The suites are effective and duplication is small; the debt is infrastructure, not tests.** Every security invariant examined is proven at more than one layer with complementary mutation catch sets; dead production code is about 1% of sampled public types; the real maintenance load is 178 GB of build output, 1,483 stale in-place output folders, 69 copies of a repo-root finder that fails in worktrees, and guidance files that contradict AGENTS.md. The genuine coverage gaps are specific: 158 of 270 API actions without an HTTP boundary test, two security guards proven only through mocks, no architecture rule for the CLI, and a composition defect class that text pins cannot see.

**Overall opportunity.** With nothing removed from the required CI gates: a security package from 3 to 4 machine hours toward about 2; the focused SQL run from 42 to 20 to 25 min; the local unit loop from 2.5 min to under a minute; the CI run from 38 to about 20 min. The first six packages in section I are each under a day and precede or accompany WP4B-C; none blocks it.

## B. Development-cost baseline

Every local cost is a multiple of its CI equivalent, and the two long poles are the SQL integration suite (35 min in CI, 34 to 42 min for the focused local set) and local solution builds (5 to 10 min clean). CI runs the nine test assemblies in parallel, so its test step is exactly the IntegrationTests wall time.

Build, restore and startup:

| Operation | CI (ubuntu-24.04, 4 vCPU) | Local (this machine) | Evidence class |
| --- | --- | --- | --- |
| Restore, warm package cache | 18 to 42 s | 88.6 s into a fresh artifacts path; 46.8 s immediate no-op | Local measured today; CI from run logs |
| Clean solution build | 1 min 20 s (Release, default parallelism) | 4 min 45 s to 10 min 09 s (Debug, with the protocol flags --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers) | Historical logs 2026-10-28/29 |
| Clean build of one test project, protocol flags | n/a | UnitTests 5 min 39 s; IntegrationTests 5 min 11 s; ArchTests 3 min 01 s | WP4B-A record |
| Incremental rebuild after a one-line production change | n/a | 31 to 53 s typical, worst 1 min 24 s (34 mutation cycles) | Historical logs |
| Management Host start inside a test fixture | about 11 s from xunit start to the first browser test | 5.7 s floor per case for host-starting SQL classes (catalog plus host); host start alone about 1 to 1.5 s | CI log; local TRX |

Test suites:

| Suite | Tests | CI wall | Local wall | Note |
| --- | --- | --- | --- | --- |
| UnitTests | 5,228 CI / 5,895 local | 39.9 s | 159 s (TRX), 2 min 22 s (log) | 41 source-scanning guard tests take 694 s locally and 6.9 s in CI |
| ArchTests | 38 | 6.7 s | 3 s | |
| Ui.Shared.Tests | 1,497 | 5.7 s | 51 s | source pins that walk src trees |
| Ui.Client.Tests | 529 | 6.7 s | 1 s | bUnit |
| AppHost.Diagnostics, EndToEnd, ModuleSdk.Testing | 7, 1, 6 | about 2 s each | not separately timed | ModuleSdk.Testing is a src project that CI runs as a test assembly |
| IntegrationTests, whole project | 2,341 CI / 2,072 local | 34.9 min | 2 h 11 min (2026-10-15, memory-starved machine) | CI critical path |
| IntegrationTests, focused IAM set | 1,337 to 1,397 | not run as a set | 34 min 18 s to 42 min 13 s | 70% of wall inside test bodies; hidden catalog creation adds 473 s |
| BrowserTests (browser-security job) | 335 (330 run, 5 opt-in skipped) | 14.2 min suite, 17.3 min job | not run locally in recent packages | 705 s of the suite is inside tests |
| Whole CI run | | 37.6 min wall, jobs in parallel | | the 103 and 349 min outliers were second attempts (manual re-runs); every first attempt started within seconds |

**Fixture setup versus assertions.** In the local focused run 1,784 of 2,541 s are attributed to test bodies, but xunit's per-test duration excludes IAsyncLifetime.InitializeAsync, where 94 further catalogs are created (473 s). Counting those and the class-fixture catalogs, about 265 to 300 catalogs cost 25 to 29 of the 42 min: roughly 24 min of migration (about 5 s per catalog) plus about 5 min waiting on ALTER DATABASE ... WITH ROLLBACK IMMEDIATE while pooled connections still hold the catalog (about 3 s each). In CI about 600 catalogs cost about 18 of 35 min (migration 1 to 2 s each, the pooled-connection wait about 3 s each). Two deadlock-retry cases take 61 and 63 s each. The source-scanning unit guards cost 21 to 75 s per directory walk locally because src/ holds 69,399 directories; reading the 3,047 tracked source files takes 1.1 s.

Verification and review cost per security work package (from the WP4B-A, WP4B-B and WP4B-D records and logs):

| Activity | Typical cost | Count per package |
| --- | --- | --- |
| Clean isolated solution or project build | 5 to 10 min | 2 to 4 |
| Whole UnitTests, Ui.Shared, Ui.Client, ArchTests | about 4 min | 2 to 3 |
| Focused SQL set, both isolation modes | 35 to 42 min | 2 to 3, plus partial reruns (19 min for 472 cases) |
| Mutation round | about 40 s incremental build per mutation, a sub-second unit run and about 4 min 20 s per 36 SQL cases | 7 (WP4B-D), 16 (WP4B-B), 15 plus 7 (WP4B-A) |
| Full IntegrationTests and browser suites | 38 min on CI after push; not run locally | 0 locally |
| Evidence folder | 50 to 100 MB of logs and TRX plus a 2 to 7 GB build folder per build | 2 to 4 build folders |

Implementer-side validation of WP4B-A, its remediation, WP4B-D and WP4B-B consumed 9 h 46 min of wall clock by file timestamps (3 h 11 min focused SQL runs, 2 h 32 min for 45 mutation runs, about 1 h 20 min builds, 25 min non-SQL suites); reviewer time is unrecorded (about 1 h per review). Flake reproduction today: the intermittent class failed 1 of 12 runs, the single test 0 of 8.

## C. Test-effectiveness assessment

The suites are effective where it matters most: every security invariant examined is proven at more than one layer (absolute unit expectations, production services over an in-memory world, source-closure guards, SQL and HTTP tests, real browsers), each layer states which property the other proves, and the 45 mutation checks recorded for WP4B-A, WP4B-B and WP4B-D were caught by specific tests. The defects are concentrated in honesty of reporting and in a few coverage gaps.

### C.1 Tests that can pass without checking their intended behaviour

| Item | Where | Effect | Fix size |
| --- | --- | --- | --- |
| 46 SQL tests return early and report Passed when SQL Server is unreachable (45 in 12 classes guarded by a null connection string, plus PersistenceWiringTests' LocalDB check, which takes the soft path in every Linux CI run); 29 fail only under APPERTURE_REQUIRE_SQL_TESTS=1, which CI does not set; 16 are fully silent | IntegrationTests root (UsernameFoundation, UserSearch, HistoricalActorNormalization, AuditCorrelationId, TstB1/B2, Ctx2, ResourceContextMigration, ApiPolling, OperationItemization, ScheduleRunExists, SqlUserRepositoryLastAuthenticatedAt) | A transient 3 s probe failure or a dead container passes identity-foundation and audit-attribution proofs green; contradicts RC1 requirement 8 and the recorded fail-not-skip policy | One env line in ci.yml; about 12 files and 60 to 100 lines after; zero run-time cost |
| Three DPAPI protector tests return early on non-Windows and report Passed | UnitTests/Secrets/DpapiSecretProtectorTests.cs | CI never exercises the production SQL-secret protector | XS |
| A secret-leak assertion is never awaited | SecretManagementSafeHandlingTests.CompositeSecretResolver_throws_without_leaking_ref_when_no_resolver (:61-73) | Cannot fail | XS |
| A governance launch test is a permanent no-op; three siblings skip silently when readiness flips | ManagementHostGovernanceHttpTests :885, :907, :1001, :1037 | The 409 integration_hard_blocked contract is never exercised at HTTP | S |
| OR-assertions and NotBe(Forbidden) accept outcomes the README forbids, including 500 | ManagementHostReliabilitySmokeTests :115, :147, :188; ConnectionTools :152; AccountProfile :481 | A 403 regressing to 200 passes | S |
| Bootstrap HTTP tests A01 to A05 depend on an order xunit does not provide (CI ran A04, A03, A05, A02, A01) | ManagementHostBootstrapHttpTests | Latent order dependency; whole-catalog wipe mid-collection | XS |
| Three timing-window assertions in a DMV-driven suite | IntegrationSettingSecretDisposition :587 (4 s), ResourceContext :276 (2 s), AuditClassification (< 1 s) | Pass vacuously on a slow server or flake on a fast one | S |
| EXECUTED-against-catalog marker asserted only as a tautology; absent from CI output | 50 Audit files | Proves nothing in CI | XS |
| Tautologies and duplicates: two Epic40 tests assert a test-local constant; a duplicated step-up method; an ArchTests guardrail enumerating only loaded assemblies; the dev-metadata guard's prefix list predates IAM/SEC/AUD/OPS/CTX | Governance/Epic40GovernanceUiTests; EntraStepUpSecurityGuardTests :114-148; ArchTests/IntegrationSdkBoundaryDependencies :58-84; RuntimeVisibleDevMetadataGuardTests :15-28 | Cannot fail from the production change they name | XS each |
| Assert-less 10 s timing diagnostic on every UnitTests run | Accounts/PasswordEvaluationPerformanceDiagnostics :168-219 | 10 s local, 2 s CI | XS |
| bUnit assertions immediately after Click/Input/Submit | Ui.Client.Tests/UsersListUiTests :108-114 and nine siblings | The known flake and nine latent ones | XS |
| Six Scheduling tests return when the IANA zone is unresolvable | ScheduleClaritySwm10Tests :13; ScheduleTimezoneSwm3Tests :38 etc. | Pass vacuously under invariant globalisation | XS |
| ConnectionToolInvocationTests.I2 | unsynchronised List in the test's own AuditSink | Reproduced 1 in 12 class runs; test-only lost update; product W2 ordering is correct | XS |

### C.2 Coverage gaps at integration boundaries

- 158 of 270 Management API actions have no HTTP-boundary test (recovery, scheduler operations, runtime shutdown, module lifecycle, platform settings, emergency-access read, audit read, run control, schedule mutations). The 401 half is structurally guaranteed by the unit-side controller authorization guard; the end-to-end 403-with-no-side-effect property is unproven for these.
- Two security properties are proven only through mocks: the recent-account-verification gate and the restart-view lockout guard (unit tests assert only NotBeNull).
- No architecture rule covers the CLI assemblies; a ProjectReference from Apperture.Cli.Host to Management.Host would pass every test.
- The Worker Host composition cannot be composed by any test; its contracts live in text pins only.
- The five opt-in UI evidence tests were last run on 2026-10-05 and have no owner, trigger or checklist item.
- CI never runs on the feature branch; 28 of the 97 Audit test files on this branch have never executed in CI.
- Three unique-index race paths (schedule claim TryClaimFireAsync, notification dedupe, append) are covered only by InMemory tests that never reach the DbUpdateException branch.

### C.3 Source pins versus executable tests

110 test files read source text (65 Ui.Shared, 34 UnitTests, 4 IntegrationTests, 4 Ui.Client, 3 BrowserTests); 69 to 74 carry a private repo-root finder recognising only a .git directory (fails in worktrees).

- Keep as text pins: closed-set writer and call-site inventories, csproj connector references, harness isolation, the ci.yml guard, the two-host script registration pin, in-method ordering pins (lock before write, gate before locks), Ui.Server and Ui.Client Program.cs, the Development-only CORS placement. Only their enumeration cost needs fixing.
- Replace with one executable composition test: nine registration-order and presence pins on Management Host Program.cs and the Governance extension. A no-SQL shape test over the real Program.cs with hosted services captured and removed would have caught the WP4B-B factory-registration defect in milliseconds (it passed the text pin).
- Replace with render tests or consolidate: Uix2 pins on two source comments; Epic111 and Epic120 pinning identical strings; Swm4 and Swm12 on the same ControlCenter copy; Ui.Server Program.cs pinned by three files; six per-surface anti-planning-label pins instead of one corpus-wide guard. Land render tests before removing pins (the roadmap re-entry requires Uix2 to Uix6, Bds5/6, Sdk3D/4C, AuthShell and Epic101 classes to stay green).

Of the 45 recorded mutations, 12 are SQL-only, 7 were caught by source text alone (A-M1, A-M3, A-M4, B15, D-M5, D-M7 and the AuthController recheck behind review finding L-1) and 26 have behavioural unit coverage.

### C.4 Redundancy versus defence in depth

Unit and SQL pairs on the administrator-set gate, role authority, browser request trust and secrets boundaries are defence in depth (call order on fakes versus observed APPLICATION lock waits; mutation tables show complementary catch sets: B10 SQL-only; B1, B2, B4 to B6 unit-only). The four gate-removed negative controls each prove a different writer. The rcsi parameterisation and the three-engine browser matrix are contract. Genuine duplication is small: PersistenceWiringTests; the duplicated step-up method; the Epic111/Epic120 and Swm4/Swm12 pins; two hosted-service order theories on both isolation modes (19 s per focused run); the AdministratorViabilityHostHttpTests.Hosts fixture (four catalogs and four hosts, about 29 s) built twice; four per-slice bridge tests subsumed by the Aud14 G4 guard.

### C.5 Determinism

The Audit suite observes every lock wait in sys.dm_tran_locks and sys.dm_exec_requests with 25 ms polling; its 60 and 30 s deadlines are upper bounds never waited in the passing path. Remaining risks: the C.1 items, the 500 to 700 ms observe windows in two lock probes (vacuous pass under load), the persistent governance catalog whose first seed block is never re-synced, the process-wide PauseAt.ReleaseAll and SqlConnection.ClearAllPools calls (must change before any SQL collection is parallelised), two env-mutating unit classes without a serialising collection, and the memory-starved AuditRecorderSqlServerTests failure (environmental).

## D. Performance bottleneck analysis

Three costs dominate, in this order: per-test catalog creation in the SQL suite, directory walks over in-place build output on the developer machine, and the serial clean-build protocol. CI's critical path is the integration assembly alone.

**D.1 SQL integration suite.** Catalog creation is two costs: CREATE DATABASE plus 96 platform migrations (about 5 s per catalog locally, 1 to 2 s in CI), and a wait of about 3 s on every ALTER DATABASE ... WITH ROLLBACK IMMEDIATE issued while pooled connections are still open (the READ_COMMITTED_SNAPSHOT switch and most drop helpers; the one helper that clears the pool first does not pay it, and the SqlServerScratchDatabase-based classes cost 4 s per catalog in CI where AuditSqlScratch costs 1 to 1.5 s). The focused IAM set creates about 265 to 300 catalogs, 94 of them inside IAsyncLifetime.InitializeAsync where xunit's per-test duration does not see them; CI creates about 600 to 700. Where a focused-run minute goes (local, 1,397 tests, 42.4 min): about 57% migrating catalogs from nothing, about 13% waiting on ALTER DATABASE while pooled connections hold the catalog, about 30% everything else (test bodies including two 61 to 63 s deadlock-retry cases, Management Host starts at about 1 to 1.5 s each, discovery, serialised scheduling; class-fixture idle at 42 class boundaries is 276 s). In CI about 18 of 35 min are catalog cost; 71 classes run strictly serially for 33.7 min under the DisableParallelization collections (Audit 28.2 min); only 22 classes run in the 68 s parallel phase. The rcsi parameterisation (1,148 of 1,397 cases twice) is intentional coverage. Per test the local machine is about twice as slow as CI; the 2 h 11 min full local run of 2026-10-15 was memory-starved and not representative. This branch adds 931 integration cases in 20 catalog-heavy classes that the measured CI run (master) does not include, so the unsharded CI job will grow to roughly 46 to 51 min at merge.

**D.2 Directory walks over build output: a local-only 100x tax.** Eleven guard classes in UnitTests and most Ui.Shared source pins enumerate src with SearchOption.AllDirectories and discard bin and obj paths only after enumeration (the filters are separator-safe; the cost is the walk). The local src tree contains 69,399 directories, and 79% of them sit under Management.Host/bin, where the Web SDK's content glob has copied 56 stray per-project artifacts/ trees (left by isolated builds on 2026-09-26) into every build output, 110,484 files deep. One walk costs 21 to 33 s on a quiet machine and 47 to 75 s under load; a walk that prunes bin, obj and artifacts while descending reaches the same 1,812 tracked .cs files in 0.2 s. The same 41 tests sum to 694 s locally and 6.9 s in CI; Ui.Shared takes 51 s locally and 5.7 s in CI (ReturnTargetSourceGuardTests 50.9 s, DebugInstrumentationGuardTests 24 s). The critical path is the serial re-walk per test inside three classes (AdministratorViabilityWriterCoverageGuardTests 152.6 s for 38 tests). CI builds in place too, so no local guard scans files CI does not; UseArtifactsOutput alone would not fix this (it leaves existing trees, breaks ci.yml:144 and Epic77UatDocumentationTests' fixed-depth root).

**D.3 Builds and restores.** The validation protocol builds with --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers, under which a clean solution build takes 4 min 45 s to 10 min 09 s locally and a single test project 3 to 6 min, against 1 min 20 s for the CI Release build on a 4-vCPU runner. The flags entered the protocol in September 2026 as a workaround for Aspire file locks and memory pressure, not for reproducibility; no local default-flag build has been timed, so the exact share of the gap they cause is unmeasured, and -p:UseSharedCompilation=false is redundant with --disable-build-servers. Provenance rests on a clean tree, staged-tree hashes, --no-incremental and a fresh artifacts path, none of which depend on parallelism or the compiler server. Incremental builds in the mutation cycles take 31 to 53 s (33 s floor for an up-to-date 38 to 42 project graph traversed serially). Each evidence set builds UnitTests, IntegrationTests and ArchTests as three separate clean builds (13 min 48 s to 20 min 53 s) where one solution build would do. Every work package starts a fresh --artifacts-path folder; 61 such folders hold 166 GB. Restore is 88.6 s into a fresh folder and 46.8 s as a no-op (CI 18 to 42 s). Memory starvation multiplies all of it: the same clean build took 10 min 09 s on 2026-10-28 and 4 min 45 s on 2026-10-29.

**D.4 CI.** The nine test assemblies run in parallel, so the 35 min test step is the 34.9 min IntegrationTests assembly; restore, build and container start add about 2.5 min; the 17.3 min browser-security job runs alongside. Splitting unit or UI tests into separate jobs saves nothing. The browser suite spends 705 of 852 s inside tests, with three clock-driven cases (61, 35, 30 s) and the 145-case Chromium F5 matrix (305 s) dominating; harness readiness is about 11 s. The two CI outliers were second attempts; queueing is not a constraint on this public, free-plan repository.

**D.5 Verification protocol repetition.** A security package costs 3 to 4 machine hours before reruns; the independent reviewer repeats roughly half in a fresh build folder; the mutation harness exists as eight diverging copies in ignored folders.

## E. Workflow and tooling assessment

The work-package lifecycle is rigorous but hand-driven, and the tooling that makes it rigorous is untracked, forked and regressing.

**E.1 Lifecycle as practised** (handoff section 5 and the three records): checkpoint and environment by hand each session (5 to 10 min; failure modes seen: mutation rows against a stopped container, a merge failing on a stale index.lock); clean isolated build per package with protocol flags (5 to 10 min, 2 to 4 per package); non-SQL suites via a copied run-nonsql.ps1 (about 4 min); a focused SQL set selected by a one-line FullyQualifiedName filter string copied and appended per package (27 to 40 to 45 classes; 35 to 42 min, 2 to 3 per package); SQL execution proof via untracked sqlmarkers.ps1 and sqlwindow.ps1; mutation checks via copied mutate.ps1 and run-mutations.ps1 (7 to 16 mutations at about 3.4 min each); staged-tree hash provenance (lapsed after the first WP4B commit); a prose record with numbers transcribed from console output (0.5 to 1 h; one transcription error corrected in the WP2 review); an independent review that rebuilds in its own folder and re-runs a 35 min set (about 1 h machine time, unrecorded).

**E.2 The validation harness is untracked and forking.** Eight copies of mutate.ps1 have eight distinct hashes in three lineages; the newest (WP4B-D and WP4B-B) dropped the docker-inspect, TRX, console retention and infrastructure-failure classification that the WP4A review made mandatory, and builds into the same artifacts/iam-sync output that the final evidence run then tests with --no-build. The focused filter missed six classes in the WP4B-A review. The WP4B-B and WP4B-D records embed no commit or tree hash of the tested tree; WP4B-D never names its evidence folder.

**E.3 Defects in tracked tooling.** scripts/ci/test.ps1 (the entry point CONTRIBUTING.md names) dot-sources a script deleted on 2026-06-18. scripts/dev/lib/DevDockerSql.ps1 line 33 rewrites the whole repo-root .env with only MSSQL_SA_PASSWORD, which would delete the test connection string and turn the SQL suites into green soft-skips on the next native start. Show-DevStatus knows only the native stack's container.

**E.4 Agent guidance.** The .ai agent and prompt files (2026-03-02) and .cursor/rules/project.mdc still ask for DTFx and Durable Functions adapter validation, contradicting AGENTS.md rules 3 and 4. None of the dev-loop facts sessions keep re-learning (gh path, MSYS path conversion, the SQLCMDPASSWORD recipe, artifacts-path builds, soft-skip semantics, the worktree .git-file trap, the Epic77 fixed-depth root finder) is in the repository; they live in 31 private memory files. docs/superpowers holds 29 completed plans with imperative directives and no completion marker; a Cursor debug log is tracked; two absolute-path Python one-offs sit under scripts/tools.

**E.5 CI integration.** The workflow never runs on the feature branch. build-and-test uploads no TRX or blame output on failure, logs at normal verbosity (21.8 MB, with a Fluent Assertions banner containing the word Failed), has no timeout-minutes, and installs SDK 10.0.403 against a local 10.0.402. The Playwright cache key is a hand-written literal. Browser failures are not diagnosable remotely in one pass; the recorded local browser runtime (30 to 35 min) is stale (14 to 18 min observed). Required checks are build-and-test and browser-security under ruleset 24158882; BrowserSecurityCiGuardTests pins parts of the YAML. The repository is public on a free plan; extra jobs cost nothing; no queueing occurred.

**E.6 What to standardise.** Four PowerShell scripts under 150 lines, one playbook page, two templates and one advisory manifest: scripts/dev/preflight.ps1 (read-only state report); scripts/dev/validation/Invoke-Evidence.ps1 and Compare-Trx.ps1 (TRX into artifacts/evidence/<record>/results, provenance.txt with HEAD, write-tree subtree hashes, SDK, container StartedAt and SQL markers; a TRX diff with totals, rcsi split, new, removed and outcome-changed tests, skipped and not-executed lists, slowest N, SqlServerTests cases under 100 ms flagged as soft-skip suspects); scripts/dev/validation/Invoke-Mutation.ps1 (wp4ba lineage plus the wp4b-b --no-restore speed-up, own output path, mandatory no-op baseline, refuses when SQL is down); tests/test-manifest with an always-run section (Program.cs or ServiceCollection changes imply the composition and start-order classes; Migrations imply the model snapshot and all SqlServerTests; any .cs or .razor change implies the source-guard classes; fixture, ci.yml or Directory.Packages.props changes imply everything) and an 80-line selector that prints always-run rows unconditionally, emits a filter and never runs or enforces anything; .ai/prompts/work-package-record.md and independent-review.md; .ai/playbooks/dev-loop.md plus corrections to qa-agent.md, architecture-review.md and .cursor rules.

**E.7 Independence of review without repetition.** Step 1 verifies what hashes and TRX already prove (subtree hashes equal the reviewed commit; TRX totals, rcsi split and timestamps match the record; SQL markers and StartedAt consistent; build log 0 errors). Step 2 always re-runs independently on the reviewer's own clean build: every new or changed test class on both isolation modes, the classes exercising changed production files that the implementer's filter omitted, at least one novel mutation per claimed property, any skipped or not-run item judged material. Step 3 does not repeat the whole unit, UI and architecture suites or the full focused set when step 1 passes. Review records gain the same provenance and timing block as implementation records.

**E.8 Generated, not summarised.** Counts and outcomes, per-class durations, the rcsi split, skipped and not-executed lists, SQL write-marker deltas, build warning counts, staged-tree hashes, the baseline diff, the slowest tests. Acceptance test for Compare-Trx: over artifacts/wp4b-d-logs and wp4b-b-logs it must report the +36 new cases, the two ManagementHostStartupOrderSqlServerTests failures and the stated rcsi split.

**E.9 Rejected.** A test-impact-analysis service, enforced selective test runs in CI, an orchestration platform, and anything that lets a change reach master with less than the full IntegrationTests project.

## F. Maintainability assessment

Little dead production code (about 1% of 1,357 sampled public types); almost everything that looks stale is documented design intent or scheduled work. The maintenance cost sits in accumulated build output, duplicated test scaffolding, and drifted tooling and guidance.

**F.1 Infrastructure accumulation.** artifacts/ holds about 178 GB on a drive that is 92% full: 61 isolated build folders and one detached worktree total 170 GB and 2.77 million files; 17 folders classified as evidence are old-layout builds or tree copies (6.5 GB); only about 1.1 GB is irreplaceable (153 TRX, 641 logs, scripts, screenshots), plus 92 MB of TRX in the test projects' own TestResults folders. Under src/ and tests/, 1,483 in-place bin and obj folders hold 156,379 files from builds on 2026-10-13 and 2026-10-23, and 56 stray per-project artifacts/ trees under src/ left by isolated builds on 2026-09-26 are copied by the Web SDK content glob into every Management.Host build (110,484 files). The C# Dev Kit build host wrote to the test projects' obj folders today. No retention rule exists; 79 tracked lines in 24 documents point at artifacts/ paths and 20 of the 42 referenced folders are already gone.

**F.2 Git housekeeping.** Four extra worktrees (all merged; v94 3.7 GB), 31 merged local branches, local master 81 commits behind, a stash of six .vs files from the POC commit (listed as preserve in the WP4B-A handoff), a tracked Cursor debug log, a CHANGELOG recording only the foundation.

**F.3 Genuinely obsolete.** Five secret-provider stubs (every member throws; 31 of 32 TODOs; zero references; DbSecretStore already [Obsolete]; forced a SEC-2.2 edit). Thirteen public types plus IWorkflowScheduler mentioned exactly once (about 250 lines, including two deny-all authorization stubs). Two legacy redirect stub pages kept alive by a Control Center link, with a 299 deprecation header on the supported Execute path. Eleven micro placeholder projects (Shared.Security, Shared.Serialization, Shared.Observability, Azure.Shared, RuntimePlane.Persistence.SqlServer; SchedulerHost, InstanceAgent and ModuleHost undecided) and three unused ProjectReferences; Apperture.slnx lists six RuntimeHost projects twice. One-off scripts: two absolute-path Python patchers, the unreferenced scripts/ci wrappers (test.ps1 broken since June), epic smoke scripts, the poc/ solution (two projects that carried two High advisories). One unreferenced test helper; a runbook step for a removed setting. Inert test configuration: MSSQL_TEST_DATABASE is overridden by code, the Aspire runtime-file discovery has no writer, two tests hard-code LocalDB.

**F.4 Duplicated test scaffolding.** 69 to 74 private repo-root finders (57 .git-directory-only); 51 InMemory DbContext factories, 27 RepoRoot copies, 5 StripComments helpers in UnitTests; 23 StubPrincipalContext definitions in nine variants; 17 pass-through or unused secret protectors; 16 TimeProvider doubles; byte-identical seed helpers; two .env parsers; three test secret protectors; four WebApplicationFactory subclasses; three scratch-catalog helpers with different failure semantics; eleven DROP DATABASE sites with four behaviours; eight mutation-harness lineages. Five Ui.Shared test files declared in Apperture.UnitTests namespaces. Apperture.ModuleSdk.Testing is a shipped harness and a test project (IsTestProject under src). The BrowserTests copies are justified by CI job isolation.

**F.5 Guidance drift.** .ai agents and prompts and .cursor rules contradict AGENTS.md rules 3 and 4; README-ManagementHostGovernance documents an inert knob; tests/README.md points developers at the Aspire dev server; SEC-1 gate and ADR-0045 record a 30 to 35 min browser suite that takes 14 to 18; SchedulerHost is both "suggested placement" and "legacy".

**F.6 Intentionally retained.** DTFx, DurableFunctions, Azure.Template, Azure.Secrets (AGENTS.md rule 4, ADR-0003); ADR-0046 dormant Trust types; the ADR-0038 [Obsolete] alias; ConfiguredDeploymentAuthenticationConfiguration and the pending writer-guard entries (WP4B-C); the PendingMfaEnrollment control; L-D1 (OPS-1.4); the 25 Enabled switches; all 31 Options classes; scripts/sec2; the deployment scaffold; the 74 epic-named test files; the EndToEndTests slot.

## G. Prioritized findings register

Class: V verified, SH supported hypothesis, UO unverified opportunity. Effort: XS under an hour, S half a day to a day, M two to five days, L more.

| # | Finding and proposal | Class | Cost today | Expected effect | Effort | Risk | Conf. |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Honest SQL outcomes: APPERTURE_REQUIRE_SQL_TESTS=1 in both CI jobs, then route the remaining guards through RequireConnectionString or CreateRequiredAsync, remove the LocalDB fallbacks, make SqlServerScratchDatabase honour the switch, add a source guard | V | 46 tests can pass without SQL; one does in every Linux CI run | assurance only | S | low | high |
| 2 | Back up the branch head: new bundle to D: and G: with checksums (or a push with the owner's consent); checksums and copies for the .bak files | V | HEAD 6e3ab57, 478ae7a and c5062ba exist only in the local .git | durability | XS | low | high |
| 3 | Stop walking build output: delete the in-place bin/obj and the 56 stray artifacts trees (stack stopped, VS Code closed); add artifacts/** to DefaultItemExcludes; shared pruned corpus (RepositorySource with a Migrations-inclusive view, a non-empty floor, the .razor view) and one repo-root helper accepting a .git file | V | 694 s local UnitTests time; Ui.Shared 51 s; agent tool time-outs; 110,484 junk JSON copies per Management.Host build | UnitTests 159 s to 50 to 70 s; Ui.Shared to 3 to 5 s; worktree-safe suites | S | low | high |
| 4 | SQL fixture step 1: clear the catalog pool before every ALTER DATABASE ... WITH ROLLBACK IMMEDIATE and DROP; ClearPool instead of ClearAllPools; consolidate hand-rolled scratch copies onto AuditSqlScratch/AuditIsolation | SH (three independent analyses agree) | about 3 s per affected catalog: about 8 min of the CI step, 5 min of a focused run | CI 35 to about 27 min; focused 42 to about 37 | S | low | medium |
| 5 | Align the test retry policy with production (EnableRetryOnFailure(3)), assert attempts | V | 123 s per focused run in two deadlock cases | about 2 min per run; fidelity | XS | low | high |
| 6 | Avoidable catalogs: lazy InitializeAsync in the UseIsolationAsync classes; per-class catalogs with per-case reset for single-connection IAM cases; share the Hosts fixture; single-mode order theories; drop BootstrapHttpFixture's second migration | V | about 70 extra catalogs per CI run, 45 per focused run; 48 s duplicate fixtures | focused run 8 to 12 min less; CI 3 to 5 min | M | medium (audit owner per class) | medium |
| 7 | Validation builds: drop -m:1 and the server flags (keep --no-incremental plus a fresh path for the final evidence build); -m:4 with the compiler server and build-server shutdown; one solution build instead of three per-project builds; persistent per-branch path for iteration; bounded four-build experiment; change at the WP4B-C boundary | SH | 4 min 45 s to 10 min 09 s per clean build; 13 min 48 s to 20 min 53 s per evidence set | 15 to 20 min per package plus 14 to 21 min per evidence set | XS | low | medium |
| 8 | Track the validation harness: Invoke-Mutation, Invoke-Evidence, Compare-Trx, preflight, test manifest and selector; promote sqlmarkers, sqlwindow and the filters; evidence layout with an off-machine zip | V | eight forked copies; filters that missed six classes; hand-transcribed evidence; lapsed provenance | 0.5 to 1 h per record; one review rerun avoided per package | S to M | low | high |
| 9 | CI diagnostics: TRX and blame-hang uploads if always(); minimal verbosity; timeout-minutes 90; SDK from global.json; Playwright cache-key guard; browser failure capture; non-blocking ui-evidence dispatch job with an owner | V | 21 MB log triage; a hang runs 6 h; evidence tests last ran 2026-10-05 | one-pass diagnosis | XS to S | low | high |
| 10 | Shard IntegrationTests by an explicit class list with a remainder-by-negation shard, fan-in check named build-and-test, TreatNoTestsAsError and an executed-count assertion per shard; rebuild per shard | SH | 35 min test step (46 to 51 after this branch merges) | two shards 20 to 23 min; a third reaches the 17 min browser floor | M | medium | medium |
| 11 | Run CI on the feature branch before merge (owner decision) | SH | full IntegrationTests never run before merge; 28 of 97 Audit files never ran in CI | first full-suite signal before merge | XS | medium | medium |
| 12 | Executable composition tests: Management Host no-SQL shape test; ServiceCollection order test; CLI architecture rule; Worker Host extraction and shape test; then retire nine text pins | V | the WP4B-B defect passed the text pin; Worker Host and CLI boundaries rest on text | defects caught in milliseconds | S to M | low | high |
| 13 | Boundary coverage: table-driven 401/403 class for the 158 untouched actions; HTTP cases for the two mock-only guards; SQL race tests for the three unique-index paths | SH | end-to-end 403 property unproven for powerful mutations; exactly-once dispatch proven by no test | closes the most consequential gaps | M | low | medium |
| 14 | Small honesty fixes (bundle; see C.1) | V | tests that cannot fail, pass vacuously or flake | truthful counts; flakes removed | S | low | high |
| 15 | Operational separation guard: catalog allowlist (Apperture_ prefix, never the bare Apperture catalog; governance catalogs must end in Tests), refuse port 14333 without an opt-in, fix the .env overwrite, delete inert paths, correct READMEs, four configuration tests | V | a copied Aspire string in APPERTURE_TEST_SQL would migrate and seed the developer catalog | structural separation | S | low | high |
| 16 | Artifacts retention rule and tiered cleanup: about 106 GB now, 48 GB after row 2, 18 GB at WP4B-C; keep every *-logs folder | V | 178 GB on a 92% full drive | 150 GB reclaimed | S | low | high |
| 17 | Dead code and inventory: secret stubs, 13 unreferenced types, unreferenced helper, Python patchers, unreferenced scripts, slnx duplicates, unused ProjectReferences; owner decisions on placeholders, legacy routes, poc archival, CHANGELOG | V | 31 misleading TODOs; dead edits | clearer inventory | S | low | high |
| 18 | Pin consolidation and UI hygiene: one pin file per surface; render tests before removing comment pins; one corpus-wide rule-6 guard; fix five namespaces | V | 3 to 8 files break per markup move | behavioural shell coverage | S to M | low | high |
| 19 | Guidance: dev-loop playbook; correct the agent files; record and review templates; superpowers README; fix scripts/ci/test.ps1 and CONTRIBUTING; correct stale figures | V | re-derivation every session; contradicting agent files | less re-discovery | S | low | high |
| 20 | Scaffolding consolidation (opportunistic): TestSupport folder and a small shared library; keep BrowserTests isolation | V | 10 to 23 files per interface change | maintenance | M | low | medium |
| 21 | Build warnings: fix 16 CS0618 and 10 xUnit1030, then WarningsAsErrors for those codes | V | 71 persistent warnings | fewer future breakages | S | low | high |
| 22 | Git housekeeping (owner confirmation): worktrees, merged branches, fast-forward master, untrack the debug log | V | 3.7 GB; stale master | hygiene | XS | low | high |
| 23 | Template catalog (BACKUP COPY_ONLY per process keyed on the Persistence assembly MVID, RESTORE WITH MOVE per test, both modes, 23 to 25 tests on the real path) | UO | 96 migrations per catalog | 13 to 18 min per focused run if restore is about 1 s; worthless at 2.5 s | L | medium | low |
| 24 | Parallel SQL collections in one process | UO | 71 classes serialised 33.7 min in CI | unknown; bounded by a 4-vCPU SQL container | M | medium | low |
| 25 | Docs-only CI classification (owner ruling; conservative; keeps check names; still runs unit and UI suites) | SH | 7 of 39 merged PRs were docs-only | about 35 min on 18% of runs | S | medium | medium |
| 26 | Product-side observations for owners: ConnectRetryCount=0 on connection-tool strings; module-discovery audit row and sync-over-async per host start; HistoricalActorNormalization scan cost unmeasured at production scale; notification polling in every test host | SH/UO | 42 s of CI; determinism questions | out of remit | - | - | - |

## H. Proposed validation tiers

| Tier | When | What runs | Today | After the section I packages |
| --- | --- | --- | --- | --- |
| 1 Fast developer feedback | every edit cycle | incremental build of touched projects into one persistent per-branch artifacts path; UnitTests, ArchTests, Ui.Shared, Ui.Client, AppHost.Diagnostics filtered to the manifest rows of the changed area plus the source-closure guards for that area; no SQL | about 4 min | 1 to 2 min |
| 2 Work-package validation | before a record; after each mutation | the manifest's SQL classes for the changed area on both isolation modes; the mutation round; SQL write-marker proof; Compare-Trx against the last accepted baseline; the always-run cross-cutting rows | 35 to 42 min per focused run; 3 to 4 h per package | 15 to 25 min per focused run |
| 3 Broad integration validation | before independent review or a PR | the whole IntegrationTests project (locally or by a CI dispatch on the branch: 35 min on CI versus 2 h locally); whole non-SQL suites; the browser suite when authentication, session, shell, request trust or host pipeline changed; the UI evidence opt-in when governance UI changed | not run before merge today | 35 to 45 min, mostly on CI |
| 4 Full CI and release verification | every push and PR to master; the RC commit | build-and-test and browser-security exactly as today with TRX uploaded; the ui-evidence dispatch job; the RCV-1 checklist | 37.6 min | 20 to 23 min with two shards |

Always-run rows: any Program.cs, ServiceCollection extension, hosted service or options class -> the composition shape test, both host start-order classes, the CLI composition test; any Migrations or DbContext model change -> the model-snapshot test, every SqlServerTests class touching those tables, the migration up/down tests, the host start-order classes; shared fixtures or collection definitions -> the whole IntegrationTests project; any .cs or .razor under src -> the source-closure guard classes; ci.yml, Directory.Packages.props, global.json -> Tier 4 on the branch; authentication, session, request trust, UI shell, Ui.Server pipeline -> the browser suite.

Rules: Tier 1 never counts as evidence. Tier 2 runs only after the preflight passes and only in the SQL-required mode. Both isolation modes are mandatory in Tier 2 evidence runs; a single mode is for iteration only. Tier 3 is the entry condition for an independent review. Tier 4 is never narrowed: no paths-ignore on the required jobs, no selective runs, every shard asserts its executed count.

## I. Proposed implementation sequence

Before WP4B-C starts (each under a day; none blocks resumption):

| Package | Contents | Why first |
| --- | --- | --- |
| P0 Backup | register 2 | the two newest accepted packages exist in one .git |
| P1 Honest SQL outcomes and separation guard | register 1 and 15, the .env overwrite fix, README corrections | WP4B-C's evidence should be produced under the require-SQL mode |
| P2 Local loop | register 3 plus the flake fixes in 14 (I2 lock, ten bUnit waits) | halves local UnitTests and Ui.Shared time for every WP4B-C iteration |
| P3 Tracked harness and protocol | register 8 and 7 | WP4B-C's full review is the heaviest of the plan; its evidence should be reproducible and its builds not serial |
| P4 SQL fixture step 1 | register 4 and 5 | about 7 min per focused run with no semantic change |
| P5 CI diagnostics | register 9 | one-pass diagnosis of the first full-suite run of this branch |

Alongside RC1: register 6 (after the audit owner agrees the per-class list), 10 (after register 1), 11 (owner decision, could precede the WP4B-C merge), 12 and 13 (add tests, retire pins afterwards), the rest of 14, 16 tier 1 and 2, 17 (no-ruling items), 18, 19, 21.

After RC1 or on trigger: 23 (only if register 4 and 6 leave the per-catalog floor above about 3 s locally and restore measures under 1.5 s), 24 (after the process-wide side effects are removed and SQL headroom is measured), 20 (opportunistic), 22 (owner confirmation), 25 and the owner rulings in 17; 26 goes to product owners.

Dependencies: P1 before register 10; register 2 before register 16 tier 2; register 1 and 8 before any review-protocol change; register 12 before retiring any text pin; the protocol change at a package boundary, stated in the record.

## J. Non-actionable observations

Expensive tests whose value justifies their cost: per-test catalogs for tests that count audit rows, the first-administrator race, the bootstrap writers, migration up/down and host-start tests; both isolation modes for 1,148 IAM cases; the 45 mutation checks per cycle; the 35 to 42 min focused SQL set; the browser engine matrix, the 145-case F5 matrix, the F9, F10 and F6 clock waits and about 100 s of negative-evidence sleeps; the single serial browser collection and the 8 s SQL settle sleep; the 60 and 30 s Patience deadlines and 25 ms DMV polls; RbacEvaluatorDifferentialTests and EntraStepUpCallbackTests; ArchTests' build cost; the AppHost.Diagnostics incident pins; the in-method ordering pins; the guard self-tests; the three 14 s connection-tool cases (real SqlClient retry behaviour).

Apparent duplication that is defence in depth: in-memory world versus SQL suites; the three request-trust layers; the four gate-removed negative controls; the notification coordinator, parity and escalation suites; Slice schema tests versus lifecycle audit tests; the two DTO parity suites; the two SDK API snapshots; the ToastDelta behaviour and translation pair; UsernameFoundation contract tests and their SQL twin; Aud14ClosureGuardTests; the BrowserTests helper copies; the two-host script registration pin; the CSS pipeline and the Ui.Server and Ui.Client Program.cs pins.

Old code retained for compatibility or scheduled work: DTFx, DurableFunctions, Azure.Template, Azure.Secrets; ADR-0046 dormant Trust types; the ADR-0038 alias; ConfiguredDeploymentAuthenticationConfiguration and the pending guard entries (WP4B-C); the PendingMfaEnrollment control; L-D1 (OPS-1.4); the 25 Enabled switches; the EndToEndTests slot; the 74 epic-named test files; scripts/sec2; the deployment scaffold; the .planning inventory and docs/superpowers plans; the POC stash.

Infrastructure whose redesign would cost more than it saves: isolated artifacts-path builds (fix is a persistent path and retention); DisableParallelization on the SQL and Bootstrap collections for now; a NuGet cache in CI; caching the migration script or an EF compiled model; the Playwright apt step; concurrency cancel-in-progress; the SQL and SFTP wait loops; splitting unit, UI or architecture tests into separate jobs.

Things that looked like defects and are not: the 85 and 300 min "queue waits" (second attempts); Release versus Debug divergence (none); the product W2 ordering (correct; the I2 fix is in the test double); the 2 h 11 min local run (memory-starved, not representative).

## K. Verification strategy

Common instruments: Compare-Trx over a baseline and a candidate TRX; the mutation harness re-applied to the retained WP4B mutations; the SQL execution proof (catalog start-ups in the server log, per-catalog markers); a deliberate break of the protected property.

| Register item | Proof that protection is preserved | Keep or revert |
| --- | --- | --- |
| 1 | IntegrationTests with SQL stopped: exactly 46 failures with the configuration message, zero soft passes; with SQL present 2,341 passed, 0 skipped, durations unchanged | keep; if CI shows reachability failures confined to these classes, add a probe retry, never soft-skips |
| 2 | bundle verify on both drives; list-heads shows 6e3ab57; checksums match | keep; repeat after every accepted package |
| 3 | per-site diff of enumerated paths before and after; a synthetic untracked offender still fails each coverage guard; a non-empty floor; Migrations-inclusive view still reaches the Components check; UnitTests run from a linked worktree | keep if UnitTests <= 70 s and Ui.Shared <= 8 s locally with CI unchanged; revert if any guard's file count drops unexpectedly |
| 4 | same assertions; unique catalog markers; retained WP4B-B B3, B8, B10 and WP4B-D M1, M4 mutations still fail; reverting the pool clear restores the 3 s signature | keep if the moved classes reach the AuditSqlScratch floor in CI and the focused run loses about 5 min |
| 5 | L1 attempt-count assertions unchanged; Deadlock cases still throw the captured SqlException on both modes; the WP3 swallowed-failure mutation still fails; faults = MaxRetryCount + 1 fails, = MaxRetryCount succeeds | keep if the cases take under 10 s |
| 6 | class walls from TRX; retained mutations still fail the same cases; a control that skips the per-case reset fails; both modes exercised; no absolute audit count on a shared catalog | per class with the audit owner's agreement; revert any class whose catch set shrinks |
| 7 | four clean builds (protocol; -m:1 with servers; -m:4 with servers; defaults) with memory sampled: 71 warnings, 0 errors, no node crash, trough above 500 MB, >= 35% wall saving; assembly hashes identical flagged versus unflagged; one mutation rerun identical | adopt -m:4 with servers unless defaults are faster with no lower trough; change at a package boundary |
| 8 | Invoke-Mutation reproduces the WP4B-B caught and escaped counts; Compare-Trx over WP4B-D and WP4B-B reports +36 cases, two start-order failures, the rcsi split; the manifest helper fed the WP4B-A diff names the six missed classes | keep if all three hold; evidence hashes untouched |
| 9 | forced failure and forced hang in a scratch branch yield an uploaded TRX and a dump; a props version bump without editing ci.yml fails the cache guard | keep |
| 10 | union of shards equals the pre-shard executed count; TreatNoTestsAsError on; fan-in check named build-and-test fails on any non-success; BrowserSecurityCiGuardTests passes | keep if the 10-run median is <= 24 min with shards within 25%; revert on recurring shard-environment failures |
| 12 | B15, A-M7 and a factory-registration mutation fail the shape test in under 2 s; no SQL connection opened; a Cli.Host -> Management.Host reference fails the architecture rule | retire a pin only after its counterpart has failed the mutation |
| 13 | the endpoint matrix shows every listed mutation route with 401 and 403 cases; removing a permission check fails the class; the schedule-claim race passes only because of the index and catch | keep |
| 14 | each fix proven by the break it catches (leak the reference; reject all documents; add a label to a seed; invariant globalisation; 20 bUnit runs; 30 to 50 I2 runs with and without the lock) | keep |
| 15 | four configuration tests (bare Apperture refused; governance Tests catalog accepted; 14333 refused without opt-in, accepted with); the .env merge keeps both keys; one SqlServerTests class still creates catalogs on the test container | keep |
| 16 | free space rises by the tier; every evidence folder still present (row counts against evidence-files.csv); no record references a deleted folder | keep |
| 17 to 22 | build and ArchTests pass; git grep for removed names empty; each kept UI property broken once fails exactly one test | keep |
| 23 | TRX outcomes and marker counts identical over two local and three CI runs; migration history and seeds identical migrated versus restored; both modes asserted; a staleness control forces a rebuild; a missing template fails loudly | keep only if restore < 1.5 s and the focused run loses >= 10 min |

Detecting hidden skips after any change: Compare-Trx flags SqlServerTests cases under 100 ms, reports skipped and not-executed lists and compares per-class counts; the require-SQL mode turns a silent pass into a failure; the per-run catalog count from the server log must stay within the predicted range.

## L. Final recommendation

**Verdict: DEVELOPMENT LOOP FIT FOR RC1, OPTIMIZATIONS RECOMMENDED.**

Nothing found in the development loop should block the resumption of IAM-10.2b WP4B-C. The loop has produced three independently accepted security packages whose evidence is layered, mutation-checked and traceable, and no finding undermines a result already accepted: the hidden-skip paths were not taken in the recorded runs, the flaky test is a test double, and the CI critical path is long but honest.

Three actions are worth doing in the days before WP4B-C starts, each under a day: bundle the branch head to both backup drives (register 2); put the SQL suites into the require-SQL mode in CI and fix the two ways a local run can silently lose its SQL evidence (register 1 and 15); clear the in-place build trees so the local unit loop and the agents' tools stop paying for them (register 3). The tracked harness, the build-flag change and fixture step 1 (register 8, 7, 4, 5) should land at the WP4B-C boundary so its full review runs on reproducible tooling and parallel builds.

Expected effect of the recommended set, with nothing removed from Tier 4: a security package from 3 to 4 machine hours toward about 2; the focused SQL run from 42 to roughly 20 to 25 min; the local unit loop from 2.5 min to under a minute; the CI run from 38 to about 20 min once the shards land after the honesty fixes.

## Not measured in this audit (and what would settle it)

- RESTORE time for a small catalog in the local and CI containers (decides register 23); the restore-probe folder holds restore timings for NuGet only.
- A local clean build with default parallelism (decides how much of the 4 to 10 min is the flags versus memory); the four-build experiment in K row 7.
- The cause of the local 5 s per-catalog cost versus 1.3 to 1.6 s in CI (container memory cap, Docker Desktop disk, model database size).
- SQL CPU headroom of the 4-vCPU runner for parallel catalog creation (decides register 24).
- Whether PersistenceWiringTests and SqlUserRepositoryLastAuthenticatedAtSqlServerTests' LocalDB strings are relics (simplest register 1 fixes).
