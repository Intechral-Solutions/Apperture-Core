# Validation toolkit

Tracked, small and self-tested tooling for producing evidence that can be believed. It replaces the copies of `mutate.ps1`, `run-*.ps1`, `sqlmarkers.ps1` and `sqlwindow.ps1` that used to live in ignored `artifacts/*-logs` folders and drift apart.

The toolkit changes no product code, no CI check, no SQL honesty script or allow-list and no validation build protocol. Selecting tests is for development iteration and for planning evidence: **the required hosted CI always runs every test.**

Requires PowerShell 7.2 or later (`pwsh`), Git, and the .NET SDK that `global.json` names. Docker is needed only for the SQL proofs. Windows PowerShell 5.1 is not supported (the scripts say so and stop).

## The tools

| Tool | What it does | Reads / writes |
|---|---|---|
| `scripts/dev/preflight.ps1` | States whether a run may claim to be evidence: right repository and commit, clean tracked tree, tools, SQL target and reachability, destination, selection exists and is discovered | writes nothing but an optional report |
| `Select-Tests.ps1` | Change set -> test classes and a focused filter, from `tests/test-manifest.json`; reports what it could not map | reads Git only |
| `Invoke-Evidence.ps1` | Runs the preflight, then the tests, then writes `provenance.json`, `summary.md`, TRX, logs and `SHA256SUMS.txt` | writes under `artifacts/evidence/<record>/` |
| `Compare-Trx.ps1` | Added, removed, outcome-changed, skipped and not-executed tests, per-class counts, the isolation-mode split, slowest tests, soft-skip suspects | reads TRX only |
| `Invoke-Mutation.ps1` | Mutation controls in a disposable clone (`-Workspace`), or classification of a legacy summary (`-ReplaySummary`) | writes only to its own clone and evidence folder |
| `Get-SqlEvidence.ps1` | SQL write markers per test catalog, or catalog start-ups in a time window, through the test container | read-only queries |
| `Export-Evidence.ps1` | Copies finished evidence to a dated, verified archive on another local drive | writes the archive |
| `tests/Test-Validation.ps1` | The toolkit's own negative controls | scratch folder next to the repository, removed afterwards |

Exit codes are a contract: `0` = established; `1` = findings, a failed control or non-authoritative evidence; `2` = refused or could not run (nothing meaningful happened). A tool never returns `0` for something it could not check.

When a script is started with `pwsh -File`, list parameters take one comma-separated value (`-Files a,b`); `-ExpectFailed` takes `|`. Inside PowerShell, real arrays work as usual.

## Typical use

```powershell
# 1. Plan: which classes does this change touch, and what would the focused filter miss?
pwsh scripts/dev/validation/Select-Tests.ps1 -Json artifacts/sel.json -CurrentFilter <filter or file> -CurrentFilterProject IntegrationTests

# 2. Iterate with the filter it printed (this is Tier 1/2 iteration, never evidence).

# 3. Evidence, from a clean tree at the commit you will name in the record:
pwsh scripts/dev/validation/Invoke-Evidence.ps1 -Record wp4c-a -Plan plan.json -Selection artifacts/sel.json `
     -ExpectedCommit <sha> -SqlContainer apperture-test-sql -Build

# 4. Regression controls, in a clone that is not your checkout:
pwsh scripts/dev/validation/Invoke-Mutation.ps1 -Mutations mutations.json -Commit <sha> -Workspace D:\scratch\mut-wp4c-a

# 5. Compare against the last accepted run:
pwsh scripts/dev/validation/Compare-Trx.ps1 -Baseline <accepted>/results/IntegrationFocused.trx -Candidate <new>/results/IntegrationFocused.trx -Strict
```

`plan.json`: `{ "runs": [ { "name": "int", "project": "tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj", "selectionProject": "IntegrationTests", "requiresSql": true, "expectedMinTests": 1 } ] }`. A run may instead give `filter` and `expectedClasses` itself, and `allowNotExecuted` lists the **exact, case-sensitive, full test names** (as the TRX spells them) that are allowed to be skipped; a substring or a class name allows nothing. Run names must be unique.

**Which binary was tested.** Evidence is authoritative only when the binary is tied to the commit: either `-Build` (the tool builds it and stamps `<ArtifactsPath>/evidence-build-stamp.json` with the commit, the tree and the assembly hashes) or a later run without `-Build` finds that stamp for the same commit on a clean tree whose test output folder still holds exactly the stamped files (every file in it except symbol files, TRX, logs, coverage and `TestResults/`, by path and content: replacing a dependency or a content file invalidates it). A binary that is merely newer than the sources (file times) can be refused as stale but is never trusted: it may have been built from another commit. Untracked files among `src/`, `tests/`, `poc/` or the build inputs also make a run not authoritative, because the build compiles them; so do files in folders that `.gitignore` hides (for example `Logs/`, `Out/`, `Release/`) when they have an extension the SDK compiles or embeds by default (`cs`, `razor`, `cshtml`, `resx`, `fs`, `vb`, `csproj`, `props`, `targets`). Build configuration that Git cannot see is checked too: `Directory.Build.props/.targets/.rsp`, `Directory.Packages.props` or `NuGet.config` in any folder **above** the repository (or above a mutation workspace), MSBuild import hooks set as environment variables (`CustomBefore*`, `CustomAfter*`, `DirectoryBuild*Path`, `ImportDirectoryBuild*`, `MSBuildExtensionsPath*`, `MSBuildSDKsPath`, `MSBuildUserExtensionsPath`, `MSBuildProjectExtensionsPath`, `BaseIntermediateOutputPath`, `BaseOutputPath`), files in the user's MSBuild `ImportBefore` / `ImportAfter` folders, any `.props` / `.targets` in `<ArtifactsPath>/obj/<project>/` other than the exact names restore writes (`<project>.csproj.nuget.g.props` / `.targets`), and `-BuildArgs` that set MSBuild properties (`-p:`, `/p:`, `-property:`, `-rp:`) or load a response file (`@file`). Any of them blocks the preflight (a warning with `-AllowDirty`) or marks the build not authoritative, and is named in `provenance.json`; a mutation run refuses to start under the environment ones. **The build stamp records these facts too (`BuildAgainst`, `BuildArgs`): a stamp written by a build made under any of them is never trusted by a later run, whatever has changed since (deleting the injected file or unsetting the variable does not launder the binary), a stamp from before that record existed is not trusted, and assemblies of an untrusted stamp are not carried into a newer one.** Other `MSBUILD*` / `NUGET_*` variables are only listed by name. An existing evidence folder is never written to; a destination outside `artifacts/` is refused before anything is created.

`mutations.json`: `{ "mutations": [ { "id": "B1", "target": "src/.../X.cs", "edits": [ { "find": "age <= window", "replace": "age < window" } ], "runs": [ { "project": "tests/.../UnitTests.csproj", "filter": "FullyQualifiedName~.XTests.", "requiresSql": false } ], "expect": "caught" } ] }`. `find` must occur exactly once (or name an `occurrence`); line endings are matched to the file's.

## What each tool refuses to call a pass

| A run looks fine when... | ...but the tool says |
|---|---|
| the host exits 0 and nothing was executed | `NotDiscovered` |
| fewer tests than expected, or a selected class produced no test | `NotDiscovered` |
| some tests were skipped or not executed | `NotExecuted` (unless the plan names that exact test) |
| the assembly is older than the source it claims to test | `StaleBinary` |
| the assembly is newer than the source but nothing ties it to this commit (no `-Build`, no matching stamp) | passes the tests, but **not authoritative** ("file times only") |
| untracked files sit among the sources or build inputs | blocked by the preflight (or, with `-AllowDirty`, not authoritative) |
| the test host crashed or ran fewer tests than the unedited baseline (mutation) | `Invalid`, never `Escaped` or `Caught` |
| a workspace or evidence path in UNC, `\\?\` device or 8.3 short form | refused (exit 2) before anything is created |
| the TRX is missing, truncated, or its counters disagree with its results | `Inconclusive` / input error (exit 2) |
| the tree had uncommitted changes | not authoritative, and the diff hash is recorded |
| SQL tests ran but no catalog write happened | `SqlNotProven`. The proof is moved write markers plus a catalog start-up **or** a write to an `Apperture_*` catalog inside the run window; it is a server-wide signal, not tied to this run's tests, so a concurrent writer on the test server would satisfy it. Use a dedicated test server and treat a SQL proof from a shared one as weaker |
| `-BuildArgs` sets an MSBuild property or loads a response file | not authoritative (not vetted; a reviewed allow-list for protocol flags belongs to the build-protocol package) |
| a mutation's tests passed | `Escaped`: a failed validation result |
| a mutation did not compile, did not change the assembly, ran no test, or failed only on infrastructure | `Invalid`: neither caught nor escaped |
| the source was restored but the tree was not rebuilt | the final rebuild must reproduce the baseline assemblies, or the run fails |

SQL execution honesty is unchanged: the SQL tests fail by design when SQL Server cannot be reached, `scripts/dev/Test-SqlExecutionHonesty.ps1` and its allow-lists are untouched, and the preflight blocks a run that needs SQL when the target is missing, is the development server (port 14333), is LocalDB or a named pipe, is ambiguous, names a catalog that is neither the base name `Apperture` (what the repository's test configuration and CI use; the tests replace it with the disposable `Apperture_*` catalogs they create, and `SqlTestTargetGuard` judges only those) nor `Apperture_<name>`, or is unreachable. Passwords are never printed, stored or put on a command line; logs are passed through a redaction step.

## Provenance contract

"Authoritative" in `provenance.json` and `summary.md` is a bounded claim: **no known channel was found by which the tested binary could differ from the named commit.** It is not a proof that the build environment was hermetic, and the tool says so in every record (`AuthoritativeMeans`). Three tiers sit behind that word:

| Tier | What | Where it comes from | If it fails |
|---|---|---|---|
| 1. Verified, under the supported environment | the commit, tree hashes and dirty state; the binary tied to the commit (`-Build`, or a stamp for the same commit whose test output folder hash still matches); no untracked or ignored file of a build-input kind under `src/`, `tests/`, `poc/`, or anywhere else outside `bin/`, `obj/`, `node_modules/` and `artifacts/` paths (`*.user`, `*.rsp`, `*.props`, `*.targets`, `*.csproj`, `*.sln(x)`); no `.props` / `.targets` in the artifacts path's project folder other than restore's exact names; `-BuildArgs` that set no property and load no response file; **and the same facts about the build that made the stamp, not only about now** | read from Git and the file system, recorded in `provenance.json` and in the stamp | not authoritative, with the reason |
| 2. Known external build-input checks (a list, each with a control that can fail) | `Directory.Build.*`, `Directory.Packages.props`, `NuGet.config` above the repository; environment import hooks (`CustomBefore*`, `CustomAfter*`, `DirectoryBuild*Path`, `ImportDirectoryBuild*`, `MSBuildExtensionsPath*`, `MSBuildSDKsPath`, `MSBuildProjectExtensionsPath`, `BaseIntermediateOutputPath`, `BaseOutputPath`); the user's `ImportBefore` / `ImportAfter` folders | named lists and patterns in `ValidationCommon.ps1`; the common names each have a control that can fail, the wider families the patterns cover (`MSBuildExtensionsPath32/64`, `MSBuildUserExtensionsPath`, `*CSharpTargets`, `ImportByWildcard*`) were exercised by a reviewer's repro but have no control of their own | blocks the preflight, not authoritative (also through a later reuse of the build's stamp); a mutation run refuses to start under the environment ones. `Invoke-Mutation -BuildArgs` is not vetted |
| 3. Residual inputs that cannot be exhaustively proven absent | **every other environment variable is also an MSBuild property** (`DefineConstants`, `OutputPath`, `Configuration`-style overrides); the machine-wide SDK import folders and the installed SDK, workloads and global tools; the contents of the NuGet cache; a non-deterministic build step; another process writing into the tree during the run; generic constraints (not part of the assembly code hash); Linux, mapped network drives and directory case-sensitivity (never exercised) | not checked | nothing: the tool cannot see it |

So the known-hook list in tier 2 is a list of the channels reviewers found, not a boundary around all of MSBuild. For a result that must hold regardless, **produce evidence from a clean worktree in a clean shell** (no `MSBUILD*`, `DOTNET_*` or `NUGET_*` customisation, nothing in the user's MSBuild folders), keep `provenance.json` with the record, and treat a tier-3 concern as a reason to rebuild elsewhere, not as something this tool cleared. The primary checkout carries an ignored `Apperture.Management.Api.csproj.user`, which tier 1 counts as a build input; evidence made there is not authoritative until it is removed or the run is made from a worktree.

## Historical acceptance controls

`acceptance/Invoke-AcceptanceControls.ps1` reproduces three historical results from preserved evidence. Read them for what they are:

- **WP4B-B mutations (16 caught) is a replay.** It classifies the preserved `mutations-summary.txt` and checks each mutation's failing counts against the record. It does not re-execute the 16 mutations. Live `Invoke-Mutation` behaviour is shown on a fixture solution and in the real-repository smoke run recorded in the package record.
- **WP4B-D versus WP4B-B (36 added tests, 2 failures) reads two TRX files.** The numbers are the files' own.
- **WP4B-A selection (six missed classes named) is calibrated.** The manifest rules were written knowing the outcome; a symbol search alone finds five of the six. It shows the manifest can express the lesson, not that a manifest written in advance would have predicted it.

## CI decision (not taken here)

The self-test is **not** in the required hosted CI, and this package does not add it. Making it a CI-enforced component is a separate, owner-approved hardening decision to take before the toolkit becomes a relied-upon part of the WP4B-C validation workflow. It needs either the Linux behaviour verified or a Windows-only job, and a passing repository CI run says nothing about scripts the workflow never executes: **Linux execution of these scripts is unverified.**

## Evidence layout and archival

```
artifacts/evidence/<record>/<utc>-<sha8>/        working evidence (ignored by Git)
    provenance.json   commit, tree hashes, dirty state, tools, command, SQL target (no password), per-run outcome and assembly hash
    summary.md        generated, not typed
    preflight.json    the checks that allowed (or blocked) the run
    results/*.trx     one per run
    logs/*.log        redacted console output, SQL markers
    SHA256SUMS.txt    LF endings; `sha256sum -c SHA256SUMS.txt` from inside the folder
artifacts/evidence-build/                        regenerable build output for Invoke-Evidence (deleted at package close)
```

This follows the retention rules in `docs/01-architecture/transitions/dev-loop/dev1-2c-artifact-retention.md`: evidence is never inside a build folder, and records cite the evidence by its archive location.

At package close:

1. `Export-Evidence.ps1 -Source <folder> -DestinationRoot <other local drive>\Backup\<project>-evidence -Name <record> -Restrict` copies the folder, re-hashes the copy and writes `ARCHIVE-VERIFIED.txt`.
2. A second local drive protects against the failure of one drive. It is **not an off-machine backup**: it does not survive theft, fire, ransomware or loss of the machine. Evidence that must survive those needs an independent copy somewhere else, made by its owner with its own access control. Nothing in this toolkit uploads anything.
3. Keep the working copy until the owner agrees to remove it (retention rule 5: cleanup is manual and by exact name).

## The test manifest

`tests/test-manifest.json` maps production paths to test classes (`rules`, `mapped` or `cross-cutting`), lists paths that mean "run everything", and lists paths to ignore. Documents are not ignored: some tests read them, so a changed document is counted, the test classes that mention `docs/` are listed (and, under `-Strict`, reported as a finding). A deleted production file triggers its rules like any other change. A filter given to `-CurrentFilter` may only hold `FullyQualifiedName~<class>` clauses joined by `|`; a negation, `&` or another property is refused rather than guessed. A production file that no `mapped` rule claims is reported `UNMAPPED`; a rule that names a missing class, a class in two projects, or a wildcard that matches nothing is reported too. A simple class name declared in several namespaces of one project (the real tree has `SchedulesControllerTests` in `ControlPlane` and `Governance`) is reported `AMBIGUOUS`: the filter names the simple name and so selects all of them, `Selected[].Qualified` lists each identity, a `-CurrentFilter` covers the class only if it covers every identity, and `Invoke-Evidence` expects a test from each qualified identity. Candidates (tests that name a type declared in a changed file) are listed but not selected unless asked, because a reference shows exposure, not coverage. Add or widen a rule in the package that introduces the production area; classes that exist only on an unmerged branch are added when that branch merges.

## Self-test

`pwsh scripts/dev/validation/tests/Test-Validation.ps1` feeds every tool corrupted, missing, mismatched and misleading input and asserts that each is rejected. The live sections build a small .NET fixture solution and need the xunit packages from the local NuGet cache or a package source; if they cannot run they are reported `SKIPPED` and the exit code is `3`. The self-test is not part of the hosted CI.
