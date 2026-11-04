# DEV-1.3A: tracked and reproducible validation tooling (F8)

Status: **implemented, validated and independently reviewed (section 9) locally on `dev/dev-1-3a-validation-harness` (base `9d367c3`); not pushed.** Scope: [dev1-development-loop-plan.md](dev1-development-loop-plan.md) (DEV-1.3, F8) and [dev-loop-audit-2026-10-29.md](../../../audits/dev-loop-audit-2026-10-29.md) (E.2, E.6, E.8, section K row 8). DEV-1.3B (playbook, templates, guidance corrections) and DEV-1.3C (the validation build-protocol experiment) are not started and nothing here changes a review obligation, a CI check, a SQL honesty script or allow-list, or the authoritative validation build protocol.

The tools are documented for use in [scripts/dev/validation/README.md](../../../../scripts/dev/validation/README.md). This record explains what was chosen, why, and what was and was not shown.

## 1. What existed (inventory, 2026-11-04)

Nothing named `preflight.ps1`, `Invoke-Evidence.ps1`, `Compare-Trx.ps1` or `Invoke-Mutation.ps1` exists anywhere: not tracked, not in any ignored folder, not in the sibling working folders, not in Git history of any ref. What exists is the material they were meant to replace. All of it except the DEV-1.1 clone harnesses lives in git-ignored folders of the primary checkout and was kept under the DEV-1.2C retention rules.

| Kind | Where | Count | Notes |
|---|---|---|---|
| In-place mutation scripts (`mutate.ps1`) | `artifacts/{wp3,wp3r,wp4a,wp4a-rem,wp4ba,wp4ba-rem,wp4b-b,wp4b-d}-logs` | 8, eight distinct hashes | hard-code the primary (IAM) checkout as the workspace and edit it in place |
| Mutation drivers (`run-mutations.ps1`, `driver.ps1`) | the same folders | 7 | a table of mutations per package, passed to `mutate.ps1` |
| Disposable-clone mutation scripts | `F:\Code\Apperture\dev11a-review`, `dev11b-review` | 2 | mutate a clone, restore with `git checkout`, rebuild afterwards (DEV-1.1 reviews) |
| TRX comparison | `F:\Code\Apperture\dev11c-harness\compare-trx.ps1` | 1 | compares the sets of failed and passed test names |
| SQL proof | `artifacts/iam-sync-logs/sqlmarkers.ps1`, `sqlwindow.ps1` | 1 + 1 | read `.env`, `docker exec -e SQLCMDPASSWORD`; the window dates are typed into the script |
| Run scripts | `iam-sync-logs/run-int.ps1`, `run-nonsql.ps1`, `sync-final-logs/run-suites.ps1`, `wp4b-*-logs/run-final*.ps1` | 6 | each package's copy of "build, then run the suites with a TRX" |
| Focused filters | `*-logs/{final,focused,review}-int-filter.txt`, `filter-classes.txt` and similar | 11 files | class lists of 9 (PSR), 27 (WP4A), 32 (WP4B-A), 38 (WP4B-A review), 40 (remediation and master sync), 41 (WP4B-D) and 43 (WP4B-B) classes |
| Test manifest, selector, preflight | none | 0 | |

### The eight in-place `mutate.ps1` copies

| Feature | wp3 | wp3r | wp4a | wp4a-rem | wp4ba | wp4ba-rem | wp4b-b | wp4b-d |
|---|---|---|---|---|---|---|---|---|
| Edits the primary checkout in place | yes | yes | yes | yes | yes | yes | yes | yes |
| Byte restore and hash comparison | backup file | backup file | backup file | backup file | backup file | backup file | in memory | in memory |
| Records `git HEAD` and the mutated source hash | no | no | no | yes | yes | yes | no | no |
| SQL container state before and after | no | no | no | yes | yes | yes | no | no |
| TRX per run and full console log kept | TRX only | no | no | yes | yes | yes | no | no |
| Failure classified assertion / infrastructure | no | no | no | yes | yes | yes | no | no |
| Several edits in one mutation | no | no | no | no | yes | no | yes | yes |
| Nth-occurrence selector | no | yes | yes | yes | yes | yes | no | no |
| Builds into a shared output also used by the evidence run | no | no | no | no | no | no | yes (`artifacts/iam-sync`) | yes |
| Outcome recorded as | text | text | text | per-test lines | per-test lines | per-test lines | `restored: True` | `restored: True` |

The two DEV-1.1 clone scripts differ in kind: they never touch the primary checkout, restore with `git checkout` and rebuild after the restore, but they hold no TRX, no hashes and no records of the SQL state.

### Lineage chosen

The audit's own recommendation (E.6) was "the wp4ba lineage plus the wp4b-b `--no-restore` speed-up". The comparison supports the first half and corrects the second:

- **From the wp4a-rem / wp4ba lineage** (the version the WP4A review made mandatory, and the last one with all of it): the TRX per run with the console log kept, the infrastructure-versus-assertion classification, the byte-identical backup with hash comparison, the recorded source hash before and after, the HEAD, the several-edits and Nth-occurrence selectors, and the SQL container state before and after (kept as the SQL-reachability refusal).
- **From the DEV-1.1 clone scripts**: the disposable clone, restoration by Git (proved against the commit's blob), and the rebuild after restore. These answer a weakness that no lineage member has: every one of the eight edits the protected tree.
- **Not taken**: the wp4b-b/-d drop of the TRX, console and classification, and of the recorded hashes and HEAD (that is the regression the audit named; they do still restore from bytes and compare hashes, in memory); a build into the output the evidence run later tests with `--no-build` (stale mutated assemblies); `--no-restore` as a default (a clone restores once at the baseline build; the flags are the caller's, see `-BuildArgs`, because the build protocol is DEV-1.3C's).
- **Added, with no precedent in the lineage**: a mandatory green baseline of the same tests on the unedited tree; proof that the edit reached the compiled assembly (the assembly hash must change); a final rebuild of the restored tree that must reproduce the baseline assemblies; a fingerprint of the protected checkout before and after; and the verdict rules in section 3.
- **Not recoverable, and said so**: no provenance (commit or tree hash) was ever recorded for WP4B-B or WP4B-D; the typed-in window of `sqlwindow.ps1` is for the 2026-10-28 run only; `compare-trx.ps1` cannot be traced to a record that used it as evidence. Nothing in this package pretends otherwise.

## 2. What was built

| File | Purpose | Lines |
|---|---|---|
| `scripts/dev/preflight.ps1` | read-only decision whether a run may claim to be evidence | 174 |
| `scripts/dev/validation/Invoke-Evidence.ps1` | preflight, tests, TRX classification, provenance, checksums | 240 |
| `scripts/dev/validation/Compare-Trx.ps1` | identity-and-outcome comparison of two TRX sets | 177 |
| `scripts/dev/validation/Invoke-Mutation.ps1` | disposable-clone mutation runs; legacy-summary replay | 400 |
| `scripts/dev/validation/Select-Tests.ps1`, `tests/test-manifest.json` | change set to test classes and filter; the advisory manifest | 273 + 114 |
| `scripts/dev/validation/Get-SqlEvidence.ps1` | write markers and catalog start-up window (replaces `sqlmarkers` / `sqlwindow`) | 108 |
| `scripts/dev/validation/Export-Evidence.ps1` | verified copy to a dated archive on another local drive | 74 |
| `scripts/dev/validation/lib/ValidationCommon.ps1` | hashing, checksum files, TRX reader, Git identity, redaction, assembly code hash | 304 |
| `scripts/dev/validation/acceptance/` | the three historical controls, replayed from preserved evidence | 77 + data |
| `scripts/dev/validation/tests/Test-Validation.ps1` | negative controls for every tool | 644 |

The audit estimated "four scripts under 150 lines". The tool code is about 1,750 lines (plus the 644-line self-test) because the refusals are the point: most of `Invoke-Mutation.ps1` is isolation and proof, not mutation. The tools are PowerShell 7.2+ only (they declare `#Requires`), do not use `.env` except through the same resolution the tests use, never print or log a password, and add no dependency beyond Git, the .NET SDK and (for SQL proofs) Docker.

## 3. Rules the tools enforce (each has a negative control)

- **Evidence**: a preflight FAIL stops the run before any test; an existing evidence folder is never written to and a destination outside `artifacts/` is refused before anything is created; a dirty tracked tree, or an untracked file among the sources or build inputs (the build compiles it, whether or not `.gitignore` hides the folder it sits in), is never authoritative, nor is a build under configuration Git cannot see (MSBuild/NuGet files in a folder above the repository, import-hook variables in the environment, files in the user's MSBuild `ImportBefore`/`ImportAfter` folders); the binary must be tied to the commit, by `-Build` or by a stamp the tool wrote for the same commit on a clean tree whose test output folder still holds exactly the stamped assemblies, dependency manifests and runtime configuration (file times can refuse a stale binary but never establish provenance); `Passed` needs executed tests, no failure, no skip or not-executed (unless the plan names that exact, case-sensitive full test name), every expected class present, nothing discovered that did not produce a result, and a host exit of 0; SQL runs need write markers that moved and a catalog start-up in the run window; the folder carries LF `SHA256SUMS.txt` and is verified against it before the tool reports.
- **TRX comparison and reader**: tests are matched by TRX test id and then by exact name, so rows that share a display name are not collapsed and a theory row whose data changed is a removal plus an addition; a TRX that is truncated, empty, has a DTD, was aborted or unfinished, has incomplete counters, or whose counters disagree with its own results is an input error (exit 2), never a comparison.
- **Mutation**: workspace, source, evidence and protected-root paths must be plain drive-letter paths (UNC, `\\?\` and 8.3 spellings are refused); the workspace is a standalone clone made by the tool (no remote), outside the source, the toolkit's own repository, every worktree of either (the primary checkout is one), the current directory and any `-ProtectedRoot`, not behind a link, and its canonical location is re-checked after creation; the evidence folder may not lie in a protected checkout except that repository's `artifacts/`; edits only tracked files inside it; a failed build, an unchanged assembly, an edit that changes no code (comment or whitespace: the assembly's IL, string literals, constants, member attributes and attribute arguments are hashed because the file bytes change with any source text), no executed test, fewer tests than the unedited baseline ran, a host that exited non-zero without a failed test, an unreadable TRX or infrastructure-only failures are `Invalid`; a mutation that should be caught and passes is `Escaped` and fails the run; the source is restored and proved against the Git blob after each mutation, and the restored tree must rebuild to the baseline assemblies.
- **Selection**: reports what it cannot map (`UNMAPPED`, including deleted production files), what the manifest names that does not exist or holds no tests (`MISSING`), names in two projects (`AMBIGUOUS`), wildcards that match nothing and documents that tests read; every `git grep` it makes either answers or stops the run (a failed search is never "no match"); a filter comparison scoped to an unknown or empty project, or a filter with a clause other than `FullyQualifiedName~<class>`, is an input error, not a clean result.
- **Archive**: never overwrites; verifies the source first and the copy after; says in the archive that it is not an off-machine backup.

SQL execution honesty is unchanged. `Test-SqlExecutionHonesty.ps1`, its allow-lists and the pinning tests were not touched, and the preflight additionally refuses a SQL-dependent run when the target is missing, is the development server (port 14333), is not an `Apperture_*` catalog, is LocalDB or is unreachable.

## 4. Acceptance controls (reproduced from preserved evidence; read-only)

Command: `pwsh scripts/dev/validation/acceptance/Invoke-AcceptanceControls.ps1` (needs the primary checkout's ignored `artifacts/` and the IAM Git objects; reports `UNAVAILABLE`, exit 3, when they are missing).

| # | Control | Result |
|---|---|---|
| 1 | **WP4B-B mutation evidence.** `Invoke-Mutation -ReplaySummary` over `wp4b-b-logs/mutations-summary.txt`, with the WP4B-B record's per-mutation table (`acceptance/wp4b-b-mutations.expected.json`) | **16 mutations, 16 caught, 0 escaped, 0 invalid**; each mutation's failing unit and SQL test counts equal the record's table (for example B10: 0 unit, 12 SQL; B7: 13 unit, 2 SQL) |
| 2 | **WP4B-D versus WP4B-B TRX.** `Compare-Trx` over both `IntegrationFocused.trx` | **36 added tests** (28 in `HostConfigurationAttestationSqlServerTests`, 8 in `ManagementHostAttestationSqlServerTests`), **18 per isolation mode, none without a mode**; **2 failures**, both `ManagementHostStartupOrderSqlServerTests` (one per mode), which passed in the baseline; totals 1,361 to 1,397; rcsi False/True/none 556/556/249 to 574/574/249; 0 removed; both files list tests that share a display name (10 names), which a name-set comparison would have collapsed |
| 3 | **WP4B-A test selection.** `Select-Tests` over `git diff 5a77e24 b10a657`, tree read at `b10a657`, against the original 32-class filter | **All six classes the review added are named as missed**: `AuthenticationAuditSqlServerTests`, `ManagementHostAccountHttpTests`, `ManagementHostAccountProfileHttpTests`, `ManagementHostBootstrapHttpTests`, `ManagementHostBreakGlassHttpTests`, `UsernameFoundationSqlServerTests`. It names **two more** the record does not: `SqlUserRepositoryLastAuthenticatedAtSqlServerTests` and `UserSearchSqlServerTests` (they use `SqlUserRepository`, which WP4B-A changed). It also reports 3 production files no rule claims |

What these three do and do not show:

- **Control 1 classifies preserved logs; it does not re-execute the 16 mutations.** Re-running them needs the IAM branch's source, a SQL container and about two hours, and the task bounds this package to using the IAM material read-only. The live behaviour of `Invoke-Mutation` is shown separately on a fixture solution (section 5). A re-execution of the WP4B-B mutations in a clone of the verified bundle remains available if the owner wants it.
- **The record states no separate numeric rcsi split.** It states "28 service-level cases + 8 production-wired host cases, both isolation modes"; the tool derives 18 per mode from the TRX, and that is what control 2 asserts. No figure was invented.
- **Control 3 is calibrated, not predicted.** The identity rules in the manifest were written knowing the WP4B-A outcome. A symbol-reference search alone finds five of the six (it misses `ManagementHostBootstrapHttpTests`, which reaches `FirstAdminBootstrapService` only through HTTP), which is why the manifest is curated and the references are listed as advisory candidates. The two extra classes are over-selection by the manifest or a gap in the review; this package does not decide which.

## 5. Negative controls

`scripts/dev/validation/tests/Test-Validation.ps1` feeds every tool input it must reject or flag and asserts that it did. Last full run: **304 controls passed, 0 failed, 0 skipped sections** (lib 85, Compare-Trx 19, Select-Tests 37, mutation replay 16, SQL helper and preflight 43, live mutation 38, live evidence 60, archive 6). The live sections build a small .NET fixture solution and run the real `dotnet` build and test host; the SQL sections use a fake `docker` and a local TCP listener, never a real SQL Server.

| Tool | Misleading input it must not accept (examples, all asserted) |
|---|---|
| checksums | a changed, missing or unlisted file; `../` in a path; a duplicate or malformed line; no checksum file |
| TRX reader | truncated, empty, non-XML, DTD-bearing, no summary, counters that disagree with the results (total, passed, failed, executed); a real-shaped skip (counter `notExecuted` 0) stays valid |
| `Compare-Trx` | the same file or identical content on both sides, a truncated or counter-doctored candidate, a missing file, a folder with no TRX (all exit 2); renamed ids matched by name; shared display names not collapsed; unmet `-ExpectAdded` / `-ExpectRcsiAdded` / `-ExpectFailed` |
| `Select-Tests` | an unmapped file, a missing class, a class in two projects, a wildcard that matches nothing, a rule class that holds no tests, a shared fixture (full run required), a changed support file, an unknown or empty `-CurrentFilterProject`, a bad or incomplete manifest |
| mutation replay | an escape, a missing restore line or `restored: False`, a truncated summary, a layer that ran nothing, counts that do not add up, a verdict that disagrees with its count, a build failure, an unmet expectation, a recorded count that differs |
| `Invoke-Mutation` (live) | caught; escaped; known residual that escapes; one expected to escape that is caught; does not compile; comment-only edit; ambiguous or empty find text; untracked or climbing target; workspace that is the source, inside it, the active package, inside the package, inside an extra protected root, non-empty, or behind a junction; unknown commit; a filter that matches no test (baseline refused); SQL needed but not configured or unreachable |
| `Invoke-Evidence` (live) | passed; skipped-only; no test selected; fewer than expected; expected class absent; an allowed skip; wrong commit, dirty tree, stale binary (and the build stamp accepted or rejected); a failing test; SQL proof with markers moved, not moved, no catalog start-up, no container, an uninspectable container and an unreachable server; tampering with a TRX after the fact |
| `Get-SqlEvidence` | the password on the command line or in any output (neither); no configuration, port 14333, a catalog that is not `Apperture_*`, a docker failure |
| preflight | wrong or too-short commit, wrong repository, no/dev/non-test/LocalDB/unreachable SQL, output outside `artifacts/`, inside `bin`, or non-empty, a missing selected class |
| `Export-Evidence` | an existing destination, a source that no longer verifies, a destination inside the repository or the source |

Two defects the controls found in the tools while they were written are worth recording because they are the kind this package exists to prevent: the selector first compared against a filter with a mistyped project name and reported "missed 0" (now an input error), and a blanket `catch` returning `$null` hid that the code-hash helper failed under strict mode, so the no-op-edit check silently did nothing (now the baseline refuses, the failure reason is kept, and a control runs the helper strict).

## 6. Effect on validation cost and reproducibility

No wall-time benefit is claimed. Nothing here changes how long a build or a test run takes, and the audit's targets for hand transcription (0.5 to 1 h per record) and for independent-review reruns were not measured, because no record was produced with the toolkit yet. What was measured:

- `Compare-Trx` over the two focused-integration TRX files (35 MB and 40 MB, 1,361 and 1,397 results) takes about 4 s, including the PowerShell start; the numbers it prints are the numbers in the file, not a transcription. The replay of the 16-mutation summary is well under a second.
- `Invoke-Mutation` is slower than the in-place scripts, on purpose: a clone (shallow fetch), a baseline run of the same tests, and a final rebuild of the restored tree are added once per mutation set; each mutation still costs one build and its tests. For a set like WP4B-B (16 mutations, about 3.4 min each in the old scripts) the added cost is the baseline run and one rebuild.
- `Invoke-Evidence` adds one `--list-tests` pass per run and requires a build stamp or a build, so a stale binary costs a rebuild instead of an invalid record.

What changes is reproducibility: every evidence folder carries the commit, tree hashes, dirty state, tool versions, the command, the assembly hash, the TRX hash and a verified checksum file (the WP4B-B and WP4B-D records had no commit or tree hash); mutation work can no longer edit the checkout it is meant to protect; a skipped, undiscovered or stale run cannot be called a pass; and the three historical controls can be re-derived from preserved evidence with one command.

## 7. Risks, omissions and unresolved historical evidence

- **The SQL helpers have not run against a real SQL Server.** `Get-SqlEvidence.ps1` carries the marker and error-log queries over from the old `sqlmarkers`/`sqlwindow` (the marker output format is new). They are exercised here with a fake `docker` and a local TCP listener, because this package did not read `.env` or use a real credential. The first use against `apperture-test-sql` should be a deliberate smoke run by the owner or reviewer (`Get-SqlEvidence.ps1 -Mode Markers`), before a record depends on it.
- **The self-test is not in the hosted CI** (that would add to a required check's coverage, which is outside this package). The tools are PowerShell 7.2+ and were run on Windows only; Linux is unverified. Windows PowerShell 5.1 stops at the `#Requires` line with a non-zero exit.
- **Control 1 is a replay, control 3 is calibrated** (section 4). A re-execution of the 16 WP4B-B mutations in a clone of the verified bundle was not done.
- **The manifest is a seed.** Twelve rules cover the identity and account area, the SQL honesty files and three cross-cutting families. Three of the 20 production files in the WP4B-A diff are `UNMAPPED`. Classes that exist only on the unmerged IAM branch (for example `ManagementHostStartupOrderSqlServerTests`) are deliberately absent until it merges.
- **Restore verification assumes a deterministic build.** The restored tree must rebuild to the baseline assembly; if only the bytes differ and the IL, string literals and constants are identical it is recorded as a note, not a failure. A project with a non-deterministic build step would show the note on every run.
- **The tool is itself a single point of trust.** It is small enough to read, every refusal has a control, and an independent review is part of the package; the self-test cannot prove the absence of a false-pass path it did not think of. The size is larger than the audit's estimate (about 1,600 lines of tool code against four scripts of under 150 lines each).
- **Authoritative evidence is strict about the working copy, so it should be made from a clean worktree.** An untracked or ignored file that the build can pick up (sources, `.user`, `.rsp`, props and targets, anywhere) makes a run not authoritative. A read-only look at the primary checkout found one ignored `Apperture.Management.Api.csproj.user`; evidence from that checkout would carry that reason until the file is removed. The stamp hashes every file in the test output folder (not symbol files, TRX, logs or `TestResults/`), so a test that writes into its own output folder other than those would make the next stamped run fall back to "file times only" (not authoritative, never a false pass).
- **The build-environment check is a list, not a proof.** It names the import mechanisms the review found (ancestor `Directory.Build.*`, environment hook variables, user import folders); any environment variable is also an MSBuild property, so a variable such as `DefineConstants` or `OutputPath` is not covered, and the machine-wide SDK import folders are not examined. For a result that must hold regardless, run from a clean worktree in a clean shell and keep the record's `BuildEnvironment` block with it.
- **Known bounded gaps, none of which can produce a pass on their own**: classes with the same name in different namespaces of one project share a selection key, so a filter that covers only one of them is not reported as missing the other; generic constraints are not part of the assembly code hash; the documentation-reader list is found by a textual `docs/` mention; `Get-SqlEvidence.ps1` does not check that `-Container` is the server named in the connection string; the TRX is stored exactly as the test host wrote it, so a secret printed by a test would be in it (console logs are redacted; tests must not print secrets); Linux and a mapped network drive are unverified.
- **File-time staleness is conservative.** Without a build stamp, a source file that was touched but is byte-identical makes the binary look stale and asks for a rebuild. That costs time, never a false pass.
- **Unrecoverable history.** WP4B-B and WP4B-D recorded no commit or tree hash, so the tested tree cannot be shown for them; `compare-trx.ps1` cannot be traced to a record that used it as evidence; the 2026-10-28 window of `sqlwindow.ps1` was typed into the script.
- **G: is a second local drive, not an off-machine backup.** `Export-Evidence.ps1` says so in every archive; the independent off-machine copy of evidence that must survive the loss of this machine is not made by this toolkit and is still owed by whoever owns that evidence.

## 8. Proposed entry conditions for DEV-1.3B

1. DEV-1.3A is accepted by the owner and merged (this package is local until then), and the independent review's findings are closed.
2. The owner has run one smoke use of `Get-SqlEvidence.ps1` against the real test container and `preflight.ps1 -RequireSql` against the real configuration, and the result is recorded.
3. The owner decides whether the self-test joins the hosted CI (which needs the Linux behaviour verified or the job limited to Windows) or stays a documented local step.
4. Any change to the independent-review procedure (audit E.7: verify hashes and TRX first, rerun only new and changed classes plus a novel mutation per property) is a separate decision with the security owner's agreement; DEV-1.3B documents the toolkit but does not adopt that procedure by itself.
5. The playbook and templates draw their provenance and timing fields from `provenance.json`, `summary.md` and `mutation-results.json`, so the record is generated rather than typed (audit E.8).
6. DEV-1.3C (the validation build-protocol experiment) uses `Invoke-Evidence -BuildArgs` and `Invoke-Mutation -BuildArgs` to carry the protocol flags; neither tool encodes one.
7. The test manifest gains rules for the IAM-10.2b areas when that branch merges, and a rule for any production area a package adds; the first real use at the WP4B-C boundary runs beside the existing procedure, not instead of it.

## 9. Independent review

An independent Opus 5.5 (high) reviewer read the code first and the record afterwards, ran its own negative controls (in a scratch folder, never in the primary checkout or the IAM branch, with no `.env`, no real SQL and no connection to port 14333), and re-tested every finding with its own repro after each round. The self-test passing was never taken as the evidence.

| Round | Verdict | What it found | Outcome |
|---|---|---|---|
| 1 | **REJECT** | 5 High: a binary from another commit accepted as authoritative; untracked source compiled by the build; existing evidence overwritten when the preflight failed; the selector's working-tree mode silently selecting nothing (reproduced on the real repository); UNC and long-path spellings bypassing the workspace protection. 5 Medium: the primary checkout not actually protected; a crashed test host read as an escape; selector under-testing (deleted files, `!~` clauses, documents); assembly hash blind to attribute arguments and visibility; loose skip allowance. Lows: redaction forms, connection-string parsing, duplicate run names, an archive flag, a vacuous control | all fixed, each with a control that fails on the old behaviour |
| 2 | **REJECT, narrowly** | 2 High: the build stamp covered only the test DLL; sources in git-ignored folders still compiled. 1 Medium: custom test attributes (`[BrowserFact]` and others) not recognised. Lows: the preflight probed the forbidden port, redaction tails, qualified filter clauses, interface implementations in the code hash | all fixed |
| 3 | **ACCEPT WITH BOUNDED OBLIGATIONS** | an ignored `.csproj.user` can add sources; non-binary files in the output folder not stamped; `::$INDEX_ALLOCATION` paths; `[Trait(..), Fact]`; escaped quotes in a JSON password | all fixed |
| 4 | **ACCEPT WITH BOUNDED OBLIGATIONS** | `Directory.Build.targets` / `.rsp` in a folder above the repository (invisible to Git) | fixed: preflight, evidence and mutation |
| 5 | **ACCEPT WITH BOUNDED OBLIGATIONS** | MSBuild import hooks set as environment variables and files in the user's `ImportBefore`/`ImportAfter` folders (closed and confirmed); a stale `<project>.*.targets` in the artifacts path's project-extensions folder; three more path-redirecting variables for the hook list | **implemented after this verdict** (list extended; `Get-ProjectImportInjections`; controls added); the self-test passes, but the reviewer has **not re-checked these two** |

Closure statement. The last independent verdict is *accept with bounded obligations*; every obligation the reviewer named has been implemented with a control that can fail, and the two named in round 5 are verified by the self-test only. Nothing the reviewer found remains open except the documented limits in section 7. The reviewer's repeated finding pattern is itself a result: each round found a further way for something outside the Git tree to change what is compiled, so section 7 lists the build-environment check as a list, not a proof, and the working rule it supports is **evidence for a record is produced from a clean worktree in a clean shell, and `provenance.json` is kept with it**.

Reviewer-accepted limits (not defects): same-named test classes in different namespaces of one project share a selection key; generic constraints are not part of the assembly code hash; any environment variable is also an MSBuild property (for example `DefineConstants`), which no list can cover; Linux, a mapped network drive and the case-sensitivity flag of a directory were not exercised.
