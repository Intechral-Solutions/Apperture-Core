# DEV-1.3C: validation build-protocol experiment (F7)

| | |
|---|---|
| Package | DEV-1.3C (audit F7; plan row DEV-1.3) |
| Branch / base | `dev/dev-1-3c-build-protocol`, base `origin/master` = `210bca6` (DEV-1.3B merged, post-merge CI green) |
| Commits | Build trials and mutation comparison: `210bca6cc8330c9fa94453b801c07d6aaa69f128` (tree `2249c104e1826682f32a073a9dcce5b6288ec912`), clean worktree. First toolkit-backed record: `1322ac4ebda665a081712963571083c2854d67b7` (tree `378071e3fc2374f966df64c32383312cab36da9b`, the commit that adds the allow-list and the tooling), in a clean standalone clone |
| **Adoption status** | **NOT ADOPTED. Nothing in this package changes the mandatory validation build protocol.** The established flags remain the only mandatory procedure. A faster configuration is **recommended for a separate owner decision** (section 11), which needs the security owner's agreement and a named package boundary. The lighter independent-review procedure (audit E.7) is not approved and not touched. **Owner disposition 2026-11-05 (section 14): the experiment and its review are accepted and the criterion 6a ruling is given; the build protocol is still not adopted.** |
| Review | Independent Opus review of the measurements and the tooling: section 12 (final verdict ACCEPT at `a458f87`) |

This record states what was measured, with what controls, and what the data does and does not show. Historical numbers (4 min 45 s to 10 min 09 s, 71 warnings; audit 2026-10-29) are reference measurements, not expectations.

## 1. Closeout of DEV-1.3B

PR #47 merged as `210bca6` (2026-11-05); `build-and-test` and `browser-security` green on the PR head `8039d46` and on the post-merge run (id 42032301772). The branch tip `8039d46` is an ancestor of `origin/master`; the worktree was clean. Preserved before removing it, with the original manifests untouched: the 436-file archive (re-hashed, 436/436), the 419-file TRX supplement and the 3-file second supplement (both re-hashed), and a new 170-file supplement `wt-dev1-3b-post-merge-supplement` (PR and CI JSON, the PR body, 126 raw TRX files that had no copy) with its own checksums. The branch was deleted with `git branch -d`; `git worktree remove` deregistered the worktree, but the folder `F:\Code\Apperture\wt-dev1-3b` could not be deleted because a process this session did not start holds it, so it was left and reported (no process was stopped). The protected IAM checkout (`6e3ab57`, clean, stash and archive tag, the stale `index.lock`) was not touched.

## 2. The established protocol, as recorded

Taken from the accepted IAM work-package records (`iam10-2b-wp2-review-record.md`, `wp3`, `wp4a`, `wp4b-a`, `wp4b-b`, `wp4b-d`), the audit (D.3) and the DEV-1.3B playbook:

```
dotnet build <project or Apperture.slnx> --artifacts-path artifacts/<name> --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo
```

Debug (the SDK default; no `-c`), SDK `10.0.402` (`global.json` `10.0.100`, `rollForward latestFeature`), a fresh `--artifacts-path` per build, from the repository root, in the repository's own ignored `artifacts/` folder. The records show no further flag; `-v:q -nologo` only affect output. The flags entered the protocol in September 2026 as a workaround for Aspire file locks and memory pressure, not for reproducibility (audit D.3). The 71-warning baseline is a clean `--no-incremental` build of `Apperture.slnx` on the IAM branch (WP4B-B, WP4B-D); it is a different source tree from `master`.

## 3. Experiment design

**Solution** `Apperture.slnx` (70 projects). **Source** the clean worktree `F:\Code\Apperture\wt-dev1-3c` at `210bca6` (tree clean, no untracked file) before every trial. **Restore** was measured separately: a warm-up restore into a throw-away path populated the NuGet global-packages cache once, so every timed restore is a cache hit; each trial then ran `dotnet restore` (same parallelism and server flags as its build, no `--no-incremental`, which restore does not take) and `dotnet build --no-restore`. **Output**: every trial used a new `--artifacts-path` under `artifacts/dev13c/out/`, never shared or reused.

| Config | Build flags (after `dotnet build Apperture.slnx --artifacts-path <fresh> --no-restore`, all with `-v:q -nologo`) |
|---|---|
| **A** established | `--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers` |
| **B** serialised, servers on | `--no-incremental -m:1` |
| **C** parallel 4, servers on | `--no-incremental -m:4` |
| **D** SDK defaults | `--no-incremental` (default parallelism and servers) |
| C0, D0 (supplementary, not part of the four) | C without `--no-incremental` (C0); D0 was planned and not run |

The exact commands were written to `PLAN-commands.txt` and to each trial's `command.txt` before any command ran. Retaining `--no-incremental` in B, C and D keeps the clean-build setting constant, so that the parallelism and server flags are the only difference; C0 measures whether `--no-incremental` is additionally necessary on a fresh path (section 5).

**Order and repeats.** Pass 1 in the specified order (A1 B1 C1 D1, unique output paths). Pass 2 in reverse order (D2 C2 B2 A2) into **one shared path `S`** that was moved aside after each trial (the strict assembly-identity control, section 6); this also gives a second timing sample per configuration. C0 followed, in the same path.

**Controls.** Pre-trial: tree clean and `HEAD` equals the expected commit; no `MSBUILD*`/`DOTNET_*`/`NUGET_*`/`COMPlus_*` variable and no `Configuration`/`Platform`/`OutputPath`/`DefineConstants`/`ArtifactsPath` variable; at least 2,048 MiB of available memory; **no foreign build-related process** (the harness refused to start when it found one and never touched one); the output and evidence folders did not exist. During a trial: available memory, paging, CPU and the build processes' private and working-set memory sampled every second; the trial's own build is stopped if available memory fell below 600 MiB (above the 500 MiB acceptance floor). After: warnings and errors counted from the logs, crash patterns searched in the logs and the Windows Application event log, and `dotnet build-server shutdown` run only if processes that did not exist before the trial were left. **That command is global**: it stops every build server of the user and SDK, not just this trial's. It is safe here only because of the start precondition (no foreign build process was running, so every server present afterwards was this trial's); the precondition is relaxed under the explicit `-AllowVisualStudio` switch (used for C0 only), where a Visual Studio build node was present. Trials were sequential; no two builds overlapped.

**Harness version.** The scripts were corrected while the first trials ran (the defects under "Events"), so the trials did not all run the same version of the harness, and the earlier versions were not kept. The copies in `artifacts/dev13c/harness/` are the final versions (last written 09:01 to 09:18 on 2026-11-05, after A2 finished at 08:58); their **full SHA-256** is in `evidence/harness-as-run-full.sha256` (the original `harness-as-run.sha256` keeps only the first 16 hex digits and is unchanged): `Invoke-BuildTrial.ps1` `343ea740d459bd1d93884e2f6a406ade4579bd2f828b1f6b384eecdfb8f2b064`, `Run-Pass.ps1` `af9f619f75e0bc7b0f37e92734459b7e827ed8292c41708485b22068e13056ff`, `Get-AssemblyInventory.ps1` `74ead369635f616883a54531eda9e359e583f84e4f95df903ba5292e95911a96`. A1 ran on a version that still had the empty-process-list sum defect (it did not trigger for A1), and A1 to A2 ran before `-AllowVisualStudio` existed. The tracked scripts differ from these copies only by `#Requires` lines and removed hard-coded paths (confirmed by diff in the review). `Get-AssemblyInventory.ps1` was last written at 09:06, after the timed trials, so their inventories were made by an earlier version of it; this is harmless to the conclusions (the reviewer re-hashed four builds' files against the inventories, and its own trial run with the final version reproduced the pass-1 counts 3,483 / 477 / 69), but it is another reason to treat the harness as measurement tooling, not as a validated instrument.

**Environment** (one machine): Windows 11 Pro 10.0.26300, AMD Ryzen 7 2700X (8 cores, 16 logical), 31.9 GiB, PowerShell 7. **Antivirus:** Microsoft Defender real-time protection was off, but the independent review found **Bitdefender Antivirus registered and enabled** (Security Center product state `0x41000`; `bdagent` and five `bdservicehost` processes running, one of 488 to 547 MiB visible in the before-trial process lists of B2 and D2). Whether on-access scanning covered `artifacts\` (an exclusion) was **not determined**, so none of the timings can be called scan-free, and the historical-figure comparison below is correspondingly loose. **Other load:** a WSL virtual machine (`vmmemWSL`, 4.8 to 5.0 GiB) was running, which explains part of the 4 to 8.7 GiB available at trial starts. Present throughout, untouched: the user's browsers and editor, and the VS Code C# Dev Kit build host (one idle `dotnet` process). Differences that cannot be eliminated: other user load (the pre-trial CPU samples are recorded; D2 started under a 100 % CPU spike), file-system cache state, thermal state.

### Events during the experiment (all reported)

- The harness was developed during the first runs: three attempts failed **before any build ran** (a bad parameter split, a null command line in the process snapshot, a strict-mode sum over an empty process list). A1 therefore ran once, B1 once after its first start was aborted by the sum defect after about 20 s (its partial output was deleted, its evidence kept in `evidence-aborted/`), and the other trials once.
- The harness correctly refused to start while idle build servers from this session's own earlier ad-hoc builds were running; they were shut down with `dotnet build-server shutdown`.
- C0 ran while the user had Visual Studio open on another solution (devenv and idle MSBuild nodes appeared at 09:08). The harness was extended with an explicit, recorded `-AllowVisualStudio` switch used **only** for that non-timing control. Its timing is excluded from every comparison.
- No trial was aborted for memory, none crashed, and no trial was repeated because of a failure; every trial that ran is in the tables.

## 4. Results: time, memory, stability

Times in seconds (restore + build); **bold** = the trial's total. Build output: 4,615 files per trial.

| Trial | Config | Restore | Build | **Total** | Saving vs A (same pass) | Lowest available (MiB) | Available at start | Peak build-process private (MiB) | Peak build processes | Errors | Crash signals |
|---|---|---|---|---|---|---|---|---|---|---|---|
| A1 | A | 9.4 | 293.6 | **303.0** | | 5,673 | 7,184 | 1,432 | 11 | 0 | 0 |
| B1 | B | 8.4 | 132.7 | **141.1** | 53.4 % | 5,317 | 7,460 | 1,679 | 11 | 0 | 0 |
| C1 | C | 7.4 | 114.6 | **122.0** | 59.7 % | 5,878 | 8,555 | 2,062 | 14 | 0 | 0 |
| D1 | D | 13.1 | 120.9 | **134.0** | 55.8 % | 3,604 | 8,697 | 2,778 | 39 | 0 | 0 |
| D2 | D | 16.4 | 125.1 | **141.5** | 54.6 % | 3,108 | 4,123 | 2,548 | 38 | 0 | 0 |
| C2 | C | 8.4 | 132.3 | **140.7** | 54.9 % | 3,562 | 6,125 | 1,974 | 14 | 0 | 0 |
| B2 | B | 9.6 | 166.1 | **175.7** | 43.6 % | 2,033 | 6,027 | 1,592 | 9 | 0 | 0 |
| A2 | A | 8.3 | 303.4 | **311.7** | | 3,844 | 6,283 | 1,472 | 7 | 0 | 0 |

| Config | Mean total (s) | Mean saving vs A | Slowest trial vs A's fastest | Lowest available memory in any trial | Servers left after the build (all shut down by `dotnet build-server shutdown`) |
|---|---|---|---|---|---|
| A | 307.4 | | | 3,844 MiB | 0 (the flags work) |
| B | 158.4 | 48.5 % | 42.0 % | 2,033 MiB | 1 |
| C | 131.4 | **57.3 %** | 53.6 % | 3,562 MiB | 4 |
| D | 137.8 | 55.2 % | 53.3 % | 3,108 MiB | 16 |

Reading it: every trial succeeded with 0 errors, no crash signal (log patterns and event log), and a memory trough far above 500 MiB; the lowest of all eight trials is 2,033 MiB (B2, which also had the slowest candidate time). B2 started with 6,027 MiB available, 1.4 GiB less than B1 (7,460 MiB). Its trough came about 79 s in and was **caused outside the build**: available memory fell by about 3 GiB while the build had 3 processes and 1.15 GiB, CPU ran at 96 to 100 %, and it recovered to 5.5 GiB two seconds later. **B2's timing is therefore treated as contaminated by other load** (it is the worst candidate pair; the conclusions do not depend on it). Page-file use stayed at 7 to 8 % in every trial. (The `Pages/sec` counter peaked at 72,000 to 123,000 in every trial, A included; it counts file-cache reads as well as hard faults, so it was recorded but does not discriminate.) No instability was seen that would make a measurement unreliable, apart from the other-load noise named above. A is slower and lighter on memory (peak build memory about 1.4 GiB; at most 11 processes); the candidates use 1.6 to 2.8 GiB. D spawns 38 to 39 processes and left 16 server processes. C and D are within noise of each other (C faster in pass 1, equal in pass 2); B is slower than both. Two samples per configuration on one machine under uncontrolled user load is a small sample: the A-to-candidate gap (at least 42 % in the worst pair, 53 % to 60 % typically; the worst pair is 42.0 % when B2 is set against A1 across the two passes and 43.6 % against A2 within pass 2, both pairings being legitimate) is far larger than the run-to-run spread (A1 versus A2: 3 %, C1 versus C2: 15 %, B1 versus B2: 25 %), but the ranking among the candidates is not established.

The historical 4 min 45 s to 10 min 09 s for A is reproduced at its fast end (303 s and 312 s, 5 min 3 s and 5 min 12 s, on a machine with 4 to 8 GiB free, Microsoft Defender real-time protection off and Bitdefender active; see Environment). The clean-CI figure (1 min 20 s on a 4-vCPU Release build) is a different configuration.

## 5. Warnings and errors

Every trial: 0 errors, build succeeded. Counted from the logs per phase (the console prints each warning once while building and again in the end summary, so raw lines are twice the summary figure).

| Config | Restore warnings | Build warnings (MSBuild summary) | Raw warning lines | Unique lines |
|---|---|---|---|---|
| A, B, C (all 6 trials) | 1 (NU1903) | **72** | 145 | 73 |
| D (2 trials), C0 (1 trial) | 1 (NU1903) | **73** | 147 | 74 |

- The 72 code and analyser warnings (CS8602 16, CS0618 8, CS8625 6, xUnit1030 5, CS8767 5, CS8603 5, ... ASPIRE010 1) are an **identical set in all nine trials** (compared line by line after removing the one line below).
- NU1903 (`Microsoft.AspNetCore.Components.WebAssembly.Server` 10.0.13 has a known high severity vulnerability, GHSA-g27p-qf6j-c96c) is reported by every restore. D and C0 print the same advisory **once more** in the build output (the NuGet-logged warning replayed from the assets file); A, B and C do not. The project assets files hold the same single NU1903 in every trial. The cause of the extra replay (it is present for the SDK-default parallelism and for `-m:4` without `--no-incremental`, absent for `-m:4` with it) was **not determined**; it adds no new finding. It is the only warning difference between configurations, and it does not touch the recommended candidate C.
- Against the historical 71: the current `master` tree gives 72 in the established configuration (a different tree from the IAM branch the 71 was taken on; the extra warning is not attributed). NU1903 is a vulnerability advisory that depends on the advisory feed at the time of the run.

## 6. Assembly comparison

**Inventory** (script `scripts/dev/build-experiment/Get-AssemblyInventory.ps1`): every file under `<artifacts>/bin` (4,615 files; 2,926 managed assemblies, plus 202 reference assemblies that the inventory tags separately, so 3,128 managed files, which are raw-identical in every comparison below; the counts in this section are for the 2,926), ordinal-sorted by relative path, raw SHA-256; for managed assemblies also a **normalised** SHA-256 that zeroes only the COFF time stamp, the debug-directory time stamps and the CodeView/PdbChecksum data (PDB path, PDB id, checksum), and the module's MVID (a content hash in deterministic builds). The normaliser was validated on a two-path build of a tiny project: raw hashes differ, normalised hashes are equal. **Limit of the normaliser (review F6):** it zeroes the whole data range of each CodeView and PdbChecksum debug entry without checking where the entry points, so a crafted file whose entry points into a method body would hide an IL change (the reviewer built such a counter-example). Compiler output does not do this, and the same-path control and the 2,926-of-2,926 result use raw hashes and do not depend on the normaliser; but "equivalent under normalisation" (here and in section 8) assumes compiler-produced files, and it is not a tamper-evidence tool.

**Result 1, the four unique-path trials of pass 1 (the specified design).** The raw hashes are **not identical**. Between any two of A1, B1, C1, D1 the same 4,591 common files compare as: 3,483 raw-identical; of the 2,926 managed assemblies 477 differ only in the normalised fields, **69 still differ after normalisation**; 562 other files differ; 24 files exist in one and not the other. The counts are identical for every pair, including B1 against C1, which indicated an effect independent of the flags. Investigation (byte diffs, metadata, strings):
- 56 of the 69 differ only in a hash that the C# compiler puts in the name of a file-local type, `<GeneratedFile>F<SHA-256 of the source file path>__Type`: the **Regex source generator's** helper classes (`<RegexGenerator_g>F...__...`). Masking exactly those 64 digits leaves 13 differing.
- 11 more differ only in the same compiler-made hash for the **OpenAPI XML comment support** generator (`<OpenApiXmlCommentSupport_generated>F...__...`). Generated source lives under each trial's own `obj` folder, so its path, and therefore the hash, differs per trial. Masking `>F<64 hex>__` in general leaves 2 differing.
- The last 2 embed the output path explicitly as assembly metadata: `Apperture.AppHost.dll` (`intermediateoutputpath`) and `Apperture.BrowserTests.dll` (`UiServerAssemblyPath`, an absolute `...\out\A1\bin\...` path). With the file-local hashes masked they differ only in that string and in the normalised fields.
- The 562 other files are 546 `.pdb` files (they name their own path), 14 `.json` files (static web asset manifests and similar that name the path) and 2 `_framework` files; the 24 one-sided files are fingerprinted names. The 69 normalised differences are copies of **10 distinct assemblies** (each built into several projects' output folders); the 546 raw differences cover 65 distinct file names.

**Result 2, the strict control: pass 2 and C0 built into the same path `S`** (each moved aside afterwards, so the recorded path of each result is unique and no output was mixed):

| Comparison (reference A2) | Files | Raw-identical | Managed assemblies raw-identical | Differences |
|---|---|---|---|---|
| A2 vs B2, C2, D2, C0 | 4,615 each | 4,608 | **2,926 of 2,926** (and all 3,128 managed files, reference assemblies included) | 7 `*.staticwebassets.endpoints.json` manifests, which differ only in their `Last-Modified` header values (a clock time); identical in all four once those values are masked |

So **with the output path held constant, the assemblies of the established protocol, of `-m:1`, `-m:4`, the SDK defaults, and `-m:4` without `--no-incremental` are byte-identical** (raw SHA-256, no normalisation). The flags do not change the compiled output; the output path does, through the compiler's own path-derived names and two explicit path strings in the build.

**`--no-incremental`.** On a fresh output path it adds nothing to the output (C0 versus C2 identical); it is kept in the candidate because it costs nothing measurable (C0 109.5 s was not a valid timing, see section 3) and states the intent.

## 7. Mutation equivalence

`Invoke-Mutation -BuildArgs` is not used (it was not vetted when this was run; it is now behind the allow-list of section 9 but is still not the instrument here). Instead `scripts/dev/build-experiment/Compare-MutationBuilds.ps1`, in a disposable standalone clone at `210bca6`, builds the `Apperture.UnitTests` project **clean into a fresh path with the same flags** for each configuration, runs the same focused tests, and for each mutation applies one exact edit (must match once), builds, runs, takes the **code hash** of the mutated production assembly (`Get-AssemblyCodeHash`: method-body IL, string literals, constants and shape; independent of the output path), restores the file and checks its blob hash. A mutation whose code hash equals its configuration's baseline is classed `Invalid (code unchanged)`.

**Correction after the independent review (F2).** The first run of this comparison (timings in the table below) classed a mutation as valid when the *raw file hash* of the assembly differed from the baseline. Every build goes to a different path and a raw hash differs between paths even for identical source (section 6; the two unmutated baselines already differed), so that check could not fail. The script now compares the code hash, adds a control `M0` (a comment inside the edited expression, which must compile to identical code and must be classed "code unchanged"), and records a path-independence control (the baselines of A and C must have the same code hash although their raw hashes differ). The comparison was **re-run** with the corrected script (`evidence/mutation2/`, a fresh standalone clone at `210bca6`): A and C baseline code hash `01d2f683...2e3be` (raw hashes `9EBADC5F...2677` and `FD0BE345...F705`, different), M0 **Control ok (code unchanged)** under both, M1 **Caught** (the same two failing tests) under both, M2 **Escaped** (17/17 pass) under both; build times A 210.1, 169.7, 181.5, 179.8 s and C 78.0, 45.9, 37.8, 37.9 s (this run overlapped an unrelated self-test run, so its times are not part of the timing comparison). The reviewer's IL-level check of the first run's two mutations (M1 replaces the `get_MinimumLength` call with `ldc.i4.1`; M2 turns the `>=` sequence into `cgt`) agrees.

The two mutations are the DEV-1.3A real-repository smoke mutations (the source moved since, so the same rule is edited at its new place: `password.Length >= profile.MinimumLength` in `PasswordPolicyEvaluator.cs`), filter `PasswordPolicyEvaluatorTests`, 17 tests, no SQL:

| Run | A (established) | C (candidate) |
|---|---|---|
| baseline | 17/17 pass, build 194.7 s | 17/17 pass, build 78.2 s |
| M1 `>= 1`, declared caught | **Caught**: 2 failures, 185.5 s | **Caught**: the same 2 failures, 43.4 s |
| M2 `>` (boundary), declared escaped | **Escaped**: 17/17 pass, 179.4 s | **Escaped**: 17/17 pass, 38.4 s |

The failing tests are identical under both configurations (`Ordinary_profile_requires_fifteen_characters`, `Recovery_profile_rejects_an_ordinary_length_password`), and match the DEV-1.3A record (M1 caught by those two, M2 escaped: no test pins the boundary at exactly 15 characters; unchanged here). That the mutations were really compiled is shown by the corrected run (code hash differs from the baseline for M1 and M2, not for the no-op M0) and by the reviewer's IL comparison; the first run's own "assembly differed" statement was not evidence of it (see the correction above). The mutated assemblies of A and C are not compared byte for byte with each other (they sit in different paths, see section 6); their code hashes are compared only through the baseline path-independence control.

## 8. First toolkit-backed record, beside the established procedure

**Scope (bounded, no SQL).** Commit `1322ac4` (the first commit that contains the allow-list), a clean standalone clone, Debug. Two runs: the `Apperture.UnitTests` namespace `Apperture.UnitTests.Accounts.` (221 tests) and the whole `Apperture.ArchTests` project (38 tests). Neither needs SQL Server, so **no SQL execution evidence, no isolation-mode coverage and no write-marker proof was produced or claimed**; a SQL-backed scope remains to be compared (section 10). Scripts: `Run-FirstRecord.ps1`, `Run-FirstRecordPass2.ps1`.

**Methods.** *Established*: clean builds of the two projects into `artifacts/est` with `--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo`, then `dotnet test --no-build --filter ... --logger trx`. *Toolkit*: `Invoke-Evidence -Build -Configuration Debug -BuildArgs --no-incremental,-m:4,-v:q,-nologo` (the candidate flags, accepted by the new allow-list; the tool's default configuration is Release, so Debug was passed to keep the two builds comparable), which runs the preflight, builds, tests, classifies and writes `provenance.json`, `summary.md`, TRX and checksums.

| Item | Established procedure | Toolkit (pass 2, authoritative) | Agreement |
|---|---|---|---|
| Selected test identities | 221 + 38 (by id) | the same 221 + 38 | `Compare-Trx -Strict`: matched by id 221 and 38, **added 0, removed 0, outcome changed 0**, both runs |
| Executed / skipped | units 220 passed, 1 not executed; arch 38 passed | units 220 passed, 1 not executed; arch 38 passed | identical |
| The one skipped test | `PasswordEvaluationDesktopTimingDiagnostics.Records_desktop_zxcvbn_timings_for_representative_passwords` (an opt-in diagnostic, `Skip` unless an environment variable asks for it) | the same test | identical |
| Source identity | clone at `1322ac4`, tree `378071e3...`, clean | provenance: same commit and tree, 0 modified tracked files, preflight passed | identical |
| Binary identity | `artifacts/est` | `artifacts/tkt` (same name length as `est`, see below) | 1,225 managed assemblies in common: **all identical after normalisation and with the Regex-generator file-local-type hash masked** (`Get-AssemblyInventory.ps1 -MaskRegexGeneratorHash` zeroes any `>F<64 hex>__` byte sequence anywhere in the file; both inventories were built that way, `binary-identity.txt` says "normalisation and masking"; section 6 shows that this hash is path-derived); 838 raw-identical, the other 387 differ only in path-derived fields; 24 files exist on only one side each (fingerprinted Blazor WebAssembly names) |
| Provenance record | logs and TRX only | `provenance.json`: command, build arguments, tools, SQL target (none), preflight checks, assembly and binaries hashes, checksum file verified | the toolkit adds, it does not replace |
| Wall time (build + test) | 176.8 + 142.9 s builds, 5.2 + 6.0 s tests = 330.9 s | 146.2 s (build `-m:4` + tests 96 s and 40 s) | not like for like (different flags) |

**Disagreements, and what each is.**
1. *First toolkit pass: NOT AUTHORITATIVE.* The plan did not name the one skipped test, so the toolkit classified the units run `NotExecuted` (a skip is never a pass unless the plan names that exact test). The established procedure simply reports "1 skipped". This is the toolkit's honesty rule working, not a disagreement about the tests; with the exact test name in `allowNotExecuted` the same run is **AUTHORITATIVE** (pass 2). `Compare-Trx -Strict` still exits 1 for the units run because it counts any skip as a finding; the finding is the skip, present on both sides.
2. *Two attempts failed before reaching the tests*: the toolkit refused an evidence folder outside the clone's own `artifacts/` (by design), and its preflight refused a clone whose origin was a local path rather than the Apperture-Core remote. The harness script was corrected (default evidence location, the origin URL of the source repository). The attempts are kept (`first-record-attempt1-...`, `-attempt2-...`).
3. *Binary comparison needed equal-length paths.* The first comparison used `artifacts/est` against the toolkit's default `artifacts/evidence-build`: 387 of 1,225 managed assemblies differed after normalisation. The differences were in PE header fields and offsets: the length of the embedded PDB path differs by 11 characters, which changes the CodeView entry size and therefore section sizes and offsets, which the normaliser does not undo. The toolkit was re-run with `-ArtifactsPath artifacts/tkt` (the same length as `est`) and then no managed assembly differed. This is a limit of the comparison method, recorded as such; it is not evidence about the toolkit.
4. The toolkit builds each project separately and its default configuration is Release; the established records build the same projects separately in Debug. Both were run in Debug.

**What this establishes and what it does not.** On a bounded non-SQL scope the toolkit and the established procedure select the same tests, run the same number, produce the same outcomes, tie the binary to the same commit and tree, and the binaries are equivalent under the documented normalisation. It does **not** show equivalence for SQL-backed tests or for a full focused set, and it is one run on one commit. **The toolkit has not replaced the established procedure** and no completed adoption is claimed; the comparison is incomplete for the SQL-backed and isolation-mode portion.

## 9. Proposed build-argument allow-list

**What exists today.** `Invoke-Evidence -BuildArgs` accepted `^(--?|/)(nologo|v|verbosity|m|maxcpucount|clp|consoleloggerparameters|nr|nodereuse)([:=].*)?$` (free-form values; any other argument made the evidence non-authoritative); `Invoke-Mutation -BuildArgs` forwarded **anything** (not vetted, as its help and the README said). Both reach `dotnet build`.

**Implemented here, separable from adoption** (commit `619603a`, `Get-UnvettedBuildArgs` in `lib/ValidationCommon.ps1`, used by both tools): default deny; accepted only

| Form | Why it is safe |
|---|---|
| `-nologo`, `-v:<q\|quiet\|m\|minimal\|n\|normal\|d\|detailed\|diag\|diagnostic>`, `-clp:<Name[=value];...>`, `-nr:true\|false`, `-m`, `-m:<1 to 3 digits>` | only logging and scheduling |
| `--no-incremental` | forces more work; cannot reuse a stale binary |
| `--disable-build-servers` | uses no server; section 6 shows the output does not depend on it |
| `-p:UseSharedCompilation=false` (exactly) | selects the in-process compiler; the **only** accepted property |

Everything else is refused: any other `-p:`/`-property:`, `-o/--output`, `--artifacts-path`, `-c`, `-f`, `-r`, `-t/-target`, `--no-build`, `--no-restore`, `--no-dependencies`, `-restore`, `-getProperty/-getItem/-getTargetResult`, `-bl`, `-graph`, `-isolate`, `@file`, an unprefixed word, an embedded second argument, a trailing newline (the match is anchored with `\z`), a null. `Invoke-Evidence` keeps its behaviour for a refused argument (non-authoritative, the stamp is never trusted); `Invoke-Mutation` now **refuses** (exit 2) before creating anything.

**Controls.** Self-test section `buildargs`: 180 controls (the accepted forms and both protocols, 120+ refused forms, the tools' use of the function). Mutation check of the function and its two call sites (`Mutate-AllowList.ps1`): the first 14 single-site mutants: the first run caught 12 and two escaped (a trailing newline on a regex-matched flag, an optional leading dash), each a real gap in the controls; controls were added for each and the second run caught 14/14 (`artifacts/dev13c/evidence/allowlist-mutation`, `-mutation2`). **The independent review (F1) then found two more regressions that all of those controls missed**: dropping the pattern's start anchor `^` (an argument that merely *ends* in an accepted switch, such as `-o:D:\x\-m` or `-p:BaseOutputPath=C:\evil\-nologo`, would be accepted, reopening output redirection and property injection) and accepting a leading `+`. Fifteen refused controls were added (arguments whose tail is an accepted form, and `+`-prefixed switches), four refused controls that had never run (an inline comment had swallowed them: F5) were repaired, and the two regressions became mutants AL15 and AL16: **16/16 caught** (`evidence/allowlist-mutation3`). The earlier "14/14" was true for those 14 mutants only. Full toolkit self-test before the review changes: 505 passed, 0 failed, 0 skipped sections (re-run recorded in section 12).

**Accepted forms MSBuild itself rejects (review I-1).** The pattern accepts `-m=2`, `-nr=false`, `-clp=Summary`, `-m:0` and, because `-match` is case-insensitive, odd-cased spellings such as `-v:dİag`. The reviewer ran a real `dotnet build` with 29 accepted forms: each fails loudly or does nothing, none redirects output or writes a file (`-clp:LogFile=evil` writes none). No bypass; the allow-list is a safety net, not a syntax validator.

**Not done / not claimed.** The allow-list is not wired to any flag the experiment's trials used (those ran `dotnet build` directly, outside the toolkit). It does not change which flags are mandatory. It does not govern `Invoke-Evidence`'s separate `-Configuration` pattern. A flag that reaches the toolkit through a response file, environment or `Directory.Build.*` remains a tier-2/3 provenance matter (README).

## 10. Acceptance criteria (plan row DEV-1.3, audit K row 7)

Criteria as set by the work package; each is judged against the data above. The criterion is not changed to fit a result. The table is the result as reported at review; the owner's later ruling on 6a is in section 14 and does not alter the row.

| # | Criterion | Result |
|---|---|---|
| 1 | Zero build errors | **Met**: 0 errors in all 9 trials (8 timed + C0) |
| 2 | No build-server crashes | **Met**: no crash pattern in any log, no crash event in the Windows Application log, no abnormal exit; servers left running by B, C, D were shut down gracefully after each trial |
| 3 | No unaccounted warning differences | **Met for A, B and C** (an identical set of 72 build warnings and the same restore advisory). **Partly met for D and C0**: one extra printing of an advisory every restore already reports (NU1903, section 5); accounted as the same advisory, **cause not determined**. Not relevant to the recommended candidate C |
| 4 | Available-memory trough above 500 MiB | **Met**: lowest 2,033 MiB (B2); lowest for A 3,844 MiB, C 3,562 MiB, D 3,108 MiB. Caveat: all trials started with 4.1 to 8.7 GiB available; behaviour near the floor (the 2026-10 condition of about 0.6 GiB free that motivated the flags) was **not tested** |
| 5 | At least 35 % wall-clock saving versus the measured established protocol | **Met** by B, C and D: mean saving 48.5 %, **57.3 %**, 55.2 %; worst single pair 43.6 % (B2 against A2), 54.9 % (C), 54.6 % (D) |
| 6a | Equivalent assemblies, compared by a documented deterministic inventory and SHA-256 | **NOT MET as specified** for the four unique-path trials: raw hashes differ for 546 of the 2,926 common managed assemblies between any two trials, **entirely** because of the output path (compiler-made hashes of generated source paths in 67 assemblies and the path itself in 2, plus the PE identifiers; section 6). **Met in the strict same-path control**: all 2,926 managed assemblies byte-identical across A, B, C, D and C0 (4,608 of 4,615 files; the other 7 differ only in a `Last-Modified` time). Because the differences are benign but the hashes are not identical, the criterion is reported as not met and **an owner ruling is requested** on whether the same-path control satisfies it (section 11) |
| 6b | No regression in the selected mutation controls | **Met** for the one pair run: M1 caught by the same two tests, M2 escaped, under both configurations, as in DEV-1.3A. One class, two mutations: a representative check, not a sample of the 16 WP4B-B controls |
| 7 | Memory conditions of the work package (at least 2 GiB before a trial; regular sampling; lowest reading kept; stop if the boundary is approached; no unrelated process terminated) | **Met**; no process was terminated, no trial hit the abort threshold |

**Unmet or open**: 6a (owner ruling); the NU1903 replay cause for D and C0; a SQL-backed comparison and isolation-mode coverage for the first toolkit-backed record (section 8); behaviour under real memory pressure; Linux and non-Windows hosts (the harness uses Windows performance counters); a single machine with an active third-party antivirus (Bitdefender; Microsoft Defender real-time protection off), a running WSL virtual machine, and two samples per configuration.

## 11. Recommendation and what adoption would need

*(Written before the owner ruling. Section 14 records the ruling; the recommendation to retain the established protocol today is unchanged.)*

**Today: retain the established protocol.** This package adopts nothing, and criterion 6a is open.

**If the owner rules that the same-path control satisfies 6a, the data support recommending configuration C** (`--no-incremental -m:4`, compiler and build servers enabled, a fresh `--artifacts-path`, then `dotnet build-server shutdown`) for the validation builds, in place of `--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers`:

- it was the fastest or equal-fastest configuration in both passes (mean 131 s against 307 s, a 57 % saving, at least 54 % in both pairs);
- its warnings are identical to the established protocol's;
- with the output path held constant its assemblies are byte-identical to the established protocol's, and its mutation outcomes are identical;
- its memory use is moderate (peak build-process memory about 2.0 GiB, 14 processes) and its lowest available-memory reading was 3,562 MiB (3.48 GiB);
- D (the SDK defaults) is not better (equal time, 38 to 39 processes and 2.5 to 2.8 GiB, an extra advisory replay), and B (`-m:1` with servers) is slower and was the most variable.

What the data **do not** support: the ranking among B, C and D beyond "C is not worse"; any claim about a machine under real memory pressure (the flags were introduced for Aspire file locks and memory starvation; a separate fresh `--artifacts-path` already avoids the file locks, but memory pressure was not reproduced); a one-solution-build-instead-of-three-project-builds saving (not measured; the first toolkit record built per project); Linux.

**What adoption would require** (none of it is done or authorized here):
1. An owner decision, including the ruling on 6a, and a named package boundary (the plan names the WP4B-C boundary; the protocol must not change halfway through an existing review).
2. The security owner's agreement (a validation-protocol change; the same agreement the plan requires for any review-protocol change).
3. At that boundary, a repeat of the first toolkit-backed record beside the established procedure, with a SQL-backed scope in both isolation modes, so the comparison is complete; and ideally one trial at a low-memory floor.
4. Updating `.ai/playbooks/dev-loop.md`, which states that the flags must not be changed, in the same package as the decision; the allow-list of section 9 already accepts the flags the candidate needs, and accepting them is not adoption.
5. The lighter independent-review procedure (audit E.7) stays unapproved and is unrelated to this decision.

## 12. Independent review

**Round 1 (Opus, at `2e548d95f727feab499cd4b73bf310ece5c90e76`): ACCEPT WITH BOUNDED OBLIGATIONS.** The reviewer reproduced the trial integrity of all nine trials from the raw sample and command files, recomputed the table and savings, re-hashed every file of four builds against the inventories, built its own A and C pair, re-ran both mutations under A and C with an IL-level check, ran about 75 allow-list bypass probes, a real `dotnet build` probe of 29 accepted forms and its own mutants, re-ran `Compare-Trx -Strict` on the first record, and ran every self-test section. Verdict on the substance: the experiment is a sound basis for the conditional recommendation of section 11 on this machine (C about 2.3 times faster than A, identical warnings, byte-identical assemblies under a constant path, identical mutation outcomes); it is not a basis for claims about other machines, memory pressure, Linux or SQL-backed scopes, and the record says so. Nothing in the diff adopts a protocol or the E.7 procedure, or weakens SQL honesty, provenance or stale-binary checks. Not checked by the reviewer: the full self-test as one run, B and D re-runs, Linux, SQL, low memory, the NU1903 cause.

| Finding | Severity | Disposition |
|---|---|---|
| F1 allow-list controls miss a dropped `^` anchor and a `+` prefix | Medium | Fixed: 15 controls added, mutants AL15 and AL16, 16/16 caught (section 9) |
| F2 the mutation validity check (raw hash) could not fail | Medium | Fixed in the script (code hash, no-op control M0, path-independence control); comparison re-run; section 7 corrected |
| F3 the plan claimed a review that had not happened | Medium | Fixed: the plan says "independent review in progress" until closure |
| F4 "Defender off" hid an active Bitdefender and a WSL VM | Medium | Corrected in section 3, 4 and 10; whether `artifacts\` is excluded was not determined |
| F5 four refused controls never ran | Low | Fixed (comment moved) |
| F6 the normaliser zeroes whole debug-entry ranges without checking them | Low | Limit stated in section 6; not changed (compiler output does not do this; the same-path control uses raw hashes) |
| F7 section 8 omitted the Regex-hash masking | Low | Stated in section 8 |
| F8 record accuracy (B2, 562 files, 3,128 managed files, worst pair, 3.6 GiB) | Low | Corrected in sections 4, 6, 11 |
| F9 the harness version of the timed trials is not preserved | Low | Full SHA-256 of the final copies recorded (section 3, `harness-as-run-full.sha256`); earlier versions are not recoverable and the record says so |
| F10 `dotnet build-server shutdown` is global | Low | Precondition stated in section 3, the README and the script header |
| I-1 to I-4 | Info | I-1 recorded in section 9; I-2 section reference fixed; I-3 (environment names and command lines are copied into the evidence folders meant for archiving) is residual, see section 13; I-4 (72 warnings seen against the plan's 71) was already disclosed in section 5 |

**Re-run after the corrections:** `Test-Validation.ps1` complete, **525 passed, 0 failed, 0 skipped sections** (`evidence/selftest-full-round1-fix.log`; the reviewer's two preflight failures came from its clone's local-path origin and do not occur in this worktree). The corrections are commit `1104696` (tooling and controls) and `6f030c2` (record).

**Closure (same reviewer, at `6f030c2e34fc9c916d6732ad138301048da904c9`): ACCEPT. No obligation remains open.** The reviewer checked out that commit in its own clone (HEAD and a clean tree verified) and: re-ran the corrected `Compare-MutationBuilds.ps1` in a fresh clone under configuration C (M0 "Control ok (code unchanged)", 0 of 31 methods changed in IL; M1 caught by the same two tests and M2 escaped, one method each changed; raw hashes still differ in every row while code hashes do not depend on the path, and equal ours); confirmed `buildargs` 180/180, the four formerly swallowed controls now run (155 refused controls), 16/16 of our mutants and its own R03 and R04 caught, R01, R02 and R06 still caught, R05 still escaping `buildargs` but caught by 5 live `evidence` controls, R07 (a path inside a console-logger value, harmless: the console logger writes no file) still escaping; confirmed the record corrections against its own round-1 analysis and the harness hashes against the six copies; confirmed that the plan line was truthful; and confirmed that nothing since `210bca6` touched `.github/`, `src/`, test sources, `scripts/ci/`, `Test-SqlExecutionHonesty.ps1`, `AGENTS.md`, `.ai/`, `CONTRIBUTING.md`, `LICENSE` or `CLA.md`, that `Invoke-Evidence.ps1` and `Invoke-Mutation.ps1` are unchanged since `2e548d9`, that nothing adopts the protocol or E.7, and that no provenance check is weakened. The reviewer's 525-control count: 505 plus 19 new `buildargs` controls plus 1 `lib` control (the independent `sha256sum -c` agreement, which runs only where `sha256sum` exists; environmental).

**Not checked by the reviewer at closure:** the full self-test as one run (it ran `buildargs` and both mutation sets; the other sections are unchanged since round 1), the mutation comparison under configuration A (it re-ran C and relied on our A rows, which agree hash for hash with its C results), trial re-runs, Linux, SQL, low memory.

**Residual, informational, accepted by the reviewer:** `Get-AssemblyCodeHash` covers method IL, string literals and constants, so a mutation that changed only signatures or attributes would not change it (irrelevant to M1 and M2); the owner ruling on criterion 6a remains open; the inventory-script version point recorded in section 3. The review request and both verbatim reports are in the evidence archive (section 13).

**Record-only commit `f192173` (same reviewer): ACCEPT WITH OBLIGATIONS, one obligation (O-A).** The diff and the closure text were accurate, all 492 listed files re-hashed with 0 mismatches, the redaction statement was accurate (12 process snapshots, two redacted values each; no credential-like string remains outside synthetic self-test fixtures), and the reviewer's two reports in the archive are faithful. O-A: section 13 and the archive README said 492 files and "every file", but the manifest had left out three nested `SHA256SUMS.txt` files (a name filter). Fixed in the commit that follows `f192173`: the manifest was rebuilt and re-verified (495 listed, 497 files on disk including the manifest and the root `ARCHIVE-VERIFIED.txt`, 0 mismatches), and section 13 and the README were corrected. The reviewer did not check binary files for embedded secrets, the archive's access-control list (set to the current user, read back by the implementer), or any other copy of the archive.

**Final confirmation (same reviewer, at `a458f87febccef5336f6b6b9a225888bb2c22076`): ACCEPT. O-A is closed and no obligation remains.** The reviewer re-hashed all 495 listed files (0 mismatches). It did not check file contents beyond the hashes, binary files, or the archive's access-control list.

## 13. Evidence

**Archive:** `G:\Backup\Apperture-Core-worktree-evidence\2026-11-05\wt-dev1-3c-dev13c` (outside the repository, restricted to the current user): **497 files on disk**, of which **495 are listed** in the root `SHA256SUMS.txt` (every file except that manifest and the root `ARCHIVE-VERIFIED.txt`; the three nested `SHA256SUMS.txt` files of the first record's toolkit evidence are listed like any other file), re-hashed from disk after writing (495 listed, 495 on disk, 0 mismatches), manifest SHA-256 `5F5E9B4B5EED3E42E083AA0E43132EC6F115B5344D498FCB1FA99A5CB9E47DE4`. (The first manifest of this archive, 492 entries, wrongly excluded those three nested lists because of a name filter; the independent review found it, and the manifest was rebuilt and re-verified. Its first hash, `0D0F9F35...3D71`, is superseded.) Contents: the nine trial folders, comparisons, both mutation-comparison runs, the three allow-list mutation runs, the first-record folders and their two failed attempts, the self-test logs, the as-run harness copies with their full SHA-256, the review request and the reviewer's two verbatim reports (`evidence/review/`), the aborted first start of B1, and the reviewer's own small logs and scripts (`reviewer-clone/`; its crafted counterexample assemblies are not archived). `README.txt` in the archive states the same.

**Not archived:** `artifacts/dev13c/out` (about 32 GiB of build output; the inventories hold the per-file hashes) and the disposable clones. The build output was deleted from the worktree after the archive was verified.

**Redaction (review I-3).** The pre-trial process snapshots list every process on the machine with its command line (667 lines each). In the archive copies, two credential-like values in third-party command lines (a conferencing application's join password and a file-sync client's session token) were replaced by `<redacted>`; nothing else was changed. (At archive time the ignored originals in the worktree were unchanged; they were sanitized afterwards, see section 14.) The `env.txt` files (environment variables filtered by name) contain machine, user and session identifiers and no credential; an archive that leaves the owner's machine should be reviewed again for that reason. A harness that records full command lines should filter them before writing; this one was not changed.

**DEV-1.3B archives:** the original 436-file archive, the 419-file and 3-file supplements and the 170-file post-merge supplement were not modified (section 1); this archive is a separate directory with its own manifest.

## 14. Owner disposition (2026-11-05)

This section is an addition. Sections 1 to 13, the measurements, the original criterion wording and the review verdicts above are unchanged; where they say "an owner ruling is requested" or "criterion 6a is open", this section is the answer.

**What the owner accepted.** The DEV-1.3C experiment, its tooling and the independent ACCEPT verdict at `a458f87` (section 12).

**Criterion 6a ruling.** The literal requirement of identical hashes across different output paths was not met. The owner accepts **byte-identical assemblies from independently clean builds into the same output path** as the appropriate substitute for evaluating the effect of build-protocol flags. Both facts stand and must be quoted together:

- **Different output paths:** 546 of 2,926 managed assemblies differ, because of path-dependent generated data (section 6).
- **Same output path:** 2,926 of 2,926 managed assemblies match across A, B, C, D and C0 (4,608 of 4,615 files; the other 7 differ only in a `Last-Modified` time).

**Limits of the same-path evidence.** One Windows machine, Debug configuration, SDK 10.0.402. Each configuration was built clean into the same path (build, move aside, rebuild into the same path), so the comparison shows what the flags do to the output; it does not show that the build is reproducible across output paths, machines, SDK versions or operating systems, and **the project must not be described as reproducible across arbitrary output paths.** The assembly normaliser zeroes whole debug-directory ranges without checking them (review F6), so the normalised comparison is not a tamper-evidence tool; the same-path control uses raw hashes.

**Configuration C is the preferred future candidate, not the adopted protocol.** C is `--no-incremental -m:4`, with the compiler and build servers enabled (a fresh `--artifacts-path`, then `dotnet build-server shutdown`, section 11). The existing validation protocol is not replaced or modified by this approval: the established flags `--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers` stay mandatory, and `.ai/playbooks/dev-loop.md` is unchanged.

**What was measured, and how far it goes.**

| Item | Fact |
|---|---|
| Time | C: mean 131.4 s against 307.4 s for the established protocol, a **57.3 %** mean saving (worst C pair 54.9 %); B 48.5 %, D 55.2 % (section 4) |
| Sample | Two samples per configuration on one machine, with other load and a third-party antivirus active (section 3). The B2 timing is **contaminated** by load outside the build (section 4); the ranking among B, C and D is not established, only that each is far faster than A |
| Memory | The lowest available memory in any trial was **2,033 MiB** (B2, caused outside the build). Every trial started with 4.1 to 8.7 GiB available. **Low-memory stress was not tested**, so this shows no behaviour near the 500 MiB floor |
| Warnings | **72** code warnings, an identical set in all nine trials. D and C0 print the NU1903 advisory (which every restore already reports) once more in the build output; the cause was not determined and C is not affected (section 5) |
| Mutation | **Unchanged:** M1 caught by the same two tests and M2 escaped, under both the established protocol and C (section 7); M0, the no-op control, came out "code unchanged" |

**Incomplete comparisons.** The first toolkit-backed record compared a bounded non-SQL scope only; a **SQL-backed comparison in both isolation modes was not done**. **Linux was not run** (the harness uses Windows performance counters). These stay open (section 10, "Unmet or open").

**The measured Windows experiment and operational adoption are separate things.** This pull request carries the measurement record, the experiment tooling and the hardened, default-deny build-argument check (section 9; accepting a flag is not adopting a protocol). It changes no validation protocol, no review protocol, no required CI check or workflow, no production code and no SQL schema. Adoption of C needs **all** of: a separate owner decision; the security owner's agreement; a **SQL-backed comparison in both isolation modes** (section 11, item 3); and a named package boundary (section 11, item 1), with `.ai/playbooks/dev-loop.md` updated in the same package. The lighter independent-review procedure (audit E.7) remains unapproved and unrelated.

**Final verification on 2026-11-05** (before the pull request): branch `dev/dev-1-3c-build-protocol`, base `210bca6`, the eight commits above it (`619603a` to `a458f87`), tree clean apart from this amendment; the build-argument check and its 180 controls are unchanged since `1104696` (no file under `scripts/` changed after that commit); the full self-test run again on this tree gives 524 passed, 0 failed, 0 skipped sections in a shell without `sha256sum`, and the one control that runs only where `sha256sum` exists (the independent `sha256sum -c` agreement) passes in a second run of the `lib` section (86 of 86), matching the accepted 525. The four-build experiment was not repeated.

**Evidence security.** The archive (section 13) was re-verified: 497 files on disk, the 495 listed in the root manifest re-hashed with 0 mismatches and none missing, the two unlisted files being the root `SHA256SUMS.txt` and `ARCHIVE-VERIFIED.txt`; the manifest hash is unchanged. The twelve archived process snapshots carry `<redacted>` for both values, and an archive copy was shown to equal its original with exactly those two lines replaced. The tracked diff against `210bca6` contains no credential-like string and no process command line. After that check, the ten ignored original process snapshots in the worktree (nine trials and the aborted first start of B1) were **sanitized in place**: each was overwritten with the verified redacted archive copy, so their remaining content and provenance are unchanged. This is a best-effort overwrite on an ordinary file system, not forensic erasure. No other evidence was altered. Copies outside the worktree (a reviewer's working clone and local tool session logs) were not modified and are the owner's to handle. Whether to revoke or rotate the two credentials is the owner's decision; it is advisable as a precaution because their values were printed in local tool output.
