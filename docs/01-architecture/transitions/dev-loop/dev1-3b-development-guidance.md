# DEV-1.3B: development playbook, guidance corrections and review templates (F19)

Status: **implemented on `dev/dev-1-3b-development-guidance` (base `8cebffb`, local, not pushed); independent review: see section 10.** Scope: [dev1-development-loop-plan.md](dev1-development-loop-plan.md) (DEV-1.3, F19) and [dev-loop-audit-2026-10-29.md](../../../audits/dev-loop-audit-2026-10-29.md) (E.4, E.6, F.5, G row 19, H). Preceded by [DEV-1.3A](dev1-3a-validation-harness.md) (merged, PR #46, `8cebffb`). DEV-1.3C (the validation build-protocol experiment) is not started.

This package changes documentation, templates and one test entry-point script. It changes **no** product code, no required CI check, no workflow file, no SQL honesty script or allow-list, no validation build protocol, no independent-review procedure, and no licensing, CLA or copyright text.

## 1. Closing DEV-1.3A (checks made before the old worktree was removed)

| Check | Result |
|---|---|
| PR #46 | `MERGED` at `2026-11-05T01:23:14Z`, merge commit `8cebffb08f4c48c07f812a7abb5ef7f478fc3013`, head `c00f22d` |
| Post-merge CI on `8cebffb` | run `41948173535`, `success`; `build-and-test` and `browser-security` both `success`. PR-head run `41928510417` on `c00f22d`: `success` |
| Previous branch tip | `c00f22d52b282a04354b9e618a00624e8c51003a`; `git merge-base --is-ancestor c00f22d origin/master` exit 0 |
| Previous worktree | `wt-dev1-3a` clean (0 tracked changes; only the ignored `artifacts/`) |
| G: archive | `...\2026-11-04\wt-dev1-3a-dev13a`: manifest 172 lines, all 172 files re-hashed, 0 mismatches, 0 missing, nothing unlisted besides the two bookkeeping files; `ARCHIVE-VERIFIED.txt` says 172 of 172 |
| Unique ignored evidence | Hash comparison of the worktree's `artifacts/{dev13a,evidence,dev13a-archive-stage,evidence-build}` against the archive: `evidence` 68 of 68 already archived; `dev13a` 102 of 103 (only `pr46-checks.txt`, the post-PR check transcript, was not); `dev13a-archive-stage` equals the manifest; `evidence-build` (2,256 files, 1.4 GB) is regenerable build output. `F:\Code\Apperture\dev13a-final-review-logs` (the final independent review's two logs) was **not** in the archive |
| Preserved before cleanup | Added `post-pr-supplement\` to the archive with its **own** checksum file (8 files, re-hashed, 0 bad): the two final review logs, the PR #46 check transcript, PR metadata, both CI run summaries, the build stamp, and a README. The original `SHA256SUMS.txt` is byte-identical to before (`8DE17CEB...7D4C`) |
| Retirement | `git worktree add -b dev/dev-1-3b-development-guidance ..\wt-dev1-3b origin/master` first; then `git worktree remove ..\wt-dev1-3a` (ordinary, exit 0) and `git branch -d dev/dev-1-3a-validation-harness` (ordinary; git warned the branch is merged to its `origin` ref but not to HEAD, expected). No force operation |
| Untouched | The IAM checkout stayed on `feat/iam-10.2b-administrator-viability` at `6e3ab57` (clean); its branch, tag, stash, bundles and the other references were not touched |

The archive is a copy on a second local drive, not an off-machine backup (as every archive says).

## 2. Inventory of existing guidance

Read: `AGENTS.md`; `.ai/` (README, 3 agents, 4 prompts, 6 playbooks plus `anti-patterns`, 3 memory files); `.cursor/rules/project.mdc`; `CONTRIBUTING.md`; `tests/README.md`; `scripts/README.md`; `scripts/ci/{build,test,pack}.ps1`; the DEV-1 plan, audit, DEV-1.3A record and toolkit README; the work-package records on `master` (IAM records live only on the unmerged IAM branch); `docs/superpowers/` (29 plans, 6 specs).

### 2.1 Contradictions with AGENTS.md rules 3 and 4, and their resolution

Authoritative positions (AGENTS.md, RC1 gate D-23, [instance-plane.md](../../instance-plane.md)): module/process isolation is a supported deployment option, not a current invariant; workflow definitions are backend-agnostic; DTFx and Durable Functions adapters are a design intent and not implemented; today's path is the SQL start-request queue with inline worker execution. Verified in code before editing: `Apperture.RuntimePlane.DTFx` registers only `DurableTaskRuntimeAdapterStub`, and `DurableFunctionsRuntimeStub` throws `NotImplementedException`.

| File | Statement found | Fix |
|---|---|---|
| `.ai/agents/qa-agent.md` item 2 | "Validate workflow execution paths for both DTFx and Durable Functions adapters where applicable" | Rewritten to the current path; no adapter to validate yet. Added the module-isolation rule and a pointer to the playbook (an agent that validates must know a skipped test is not a pass) |
| `.ai/agents/coding-agent.md` item 3 | "backend-specific logic goes in adapters" (implies adapters exist) | Rewritten: no backend-specific code or assumptions in the model; adapters are a design intent; both rules named. Added a pointer to the playbook |
| `.ai/prompts/architecture-review.md` items 3 and 4 | "One-module-per-process compliance" | Rewritten as rule 3 and rule 4 |
| `.cursor/rules/project.mdc` | "one module per process" | Rewritten to rules 3 and 4; added a pointer to the playbook; fixed one broken relative link (`../.ai/...`) |
| `.ai/playbooks/runtime-portability.md` | present-tense sections "On-prem: DTFx adapter", "Cloud: Durable Functions adapter" ("Uses DTFx", "Hosted in Worker Service", "Each module maps to one Azure Function App") | Both headed "design intent, not implemented", with the verified stub status, and the verbs made intentional |
| `.ai/memory/context-pack.md` | "supports on-prem (DTFx + SQL Server) and cloud (Azure Durable Functions) runtimes" (the same file's rule 4 said the opposite) | Rewritten to the current path |
| `.ai/memory/glossary.md` (Runtime Plane, Instance plane), `.ai/playbooks/anti-patterns.md` (control/instance plane), `.ai/playbooks/project-map.md` (two lines) | "DTFx/Durable Functions execution" and "wire up adapters" as present facts | Qualified as a design intent / stubs; the dependency rule itself is unchanged |
| `.ai/memory/decisions-index.md` | ADR-0001 titled "one-module-per-process", ADR-0003 "DTFx + Durable Functions adapters" with no qualifier | Titles annotated with the D-23 amendment and "not implemented". **The ADR files themselves were not edited** |

Already consistent and left alone: `AGENTS.md`, `.ai/playbooks/principles.md`, `.ai/playbooks/module-sdk.md` (cloud labelled "not implemented"), `.ai/memory/context-pack.md` rules 3 and 4.

### 2.2 Other findings: fixed here, or reported

| Finding | Disposition |
|---|---|
| `scripts/ci/test.ps1` dot-sourced `scripts/dev/Get-DevDotNetSdk.ps1`, which does not exist (reproduced: exit 1 on every invocation) | **Fixed** (section 7) |
| `CONTRIBUTING.md`: "pipeline runs on push and pull requests to `main`" (the default branch is `master`); the PR section omits the honesty step and `browser-security`; the test section did not separate focused from full runs or mention that SQL tests fail without SQL | **Fixed**: licensing, CLA and copyright text untouched |
| `tests/README.md`: "(or, in optional mode, an announced skip)": optional mode is retired | **Fixed** (one clause) |
| `scripts/README.md`: `test.ps1` row, and "PowerShell 5.1 or PowerShell Core" where the preflight, toolkit and honesty script need 7.2+ | **Fixed** |
| `docs/superpowers/`: 29 plans and 6 specs, imperative "REQUIRED SUB-SKILL" headers, no completion markers | **Fixed** by an index (`docs/superpowers/README.md`); the plans are unedited |
| `.ai/` had no development-loop playbook or record and review templates | **Added** (sections 3 and 4) |
| Audit F.5 "README-ManagementHostGovernance documents an inert knob" and "tests/README points developers at the Aspire dev server" | Re-checked: both already corrected by DEV-1.1A. No action |
| **Reported, not changed:** `docs/01-architecture/instance-plane.md` "Cloud (RuntimeHost.Azure)" lists the Function-App-per-module template in the present tense (project-map says "scaffold only; outside RC1 support") | Architecture document, not subordinate agent guidance; owner to decide whether it gets a status label |
| **Reported, not changed:** `docs/02-decisions/ADR-0001-runtime-boundaries.md` still states "One module per process ... a core isolation boundary" in its Decision, with no amendment note; the amendment lives in the RC1 gate (D-23) and AGENTS.md | An ADR is an architectural decision record; adding an amendment note is an owner decision. The decisions index now carries the pointer |
| **Reported, not changed:** `.cursor/debug-4bd3d8.log` is tracked | DEV-1.6 / F22 (git housekeeping, owner ruling) |
| **Reported, not changed:** the SEC-1 design gate records a 30 to 35 minute browser suite (audit measured 14 to 18) | Historical measurement in a design gate; not an active instruction |
| **Reported, not changed:** two absolute-path Python one-offs under `scripts/tools`; `scripts/dev/maintenance/reset-db.ps1` is documented as dropping the `Apperture` database and is known from earlier sessions to target a different server than the one the development stack uses | DEV-1.6 / F17 and a product-script question; not verified here, not F19 |
| **Reported:** the DEV-1 plan's DEV-1.3 status bullet (updated here) and the pre-release roadmap's DEV-1.3 row ("Recommended; adopted at the WP4B-C boundary") predate the DEV-1.3A merge | Plan bullet updated; the roadmap row is left to the owner |

## 3. The playbook

[.ai/playbooks/dev-loop.md](../../../../.ai/playbooks/dev-loop.md): rules that never bend; starting a package; protecting preserved references; a clean environment; focused iteration; authoritative validation; evidence and provenance; mutation checks; independent review; findings; the PR and hosted CI; closing and archiving; verified platform notes; the proposed validation tiers; where to look.

Every step carries one of three labels. **Current** means required today (AGENTS.md, CONTRIBUTING, the RC1 gate, the DEV-1 plan, the required CI, or the established practice in the work-package records and their reviews). **Toolkit** means the DEV-1.3A tools, usable alongside the current procedure and **not** a replacement for any current step. **Proposed** means not adopted. The playbook says in its first paragraph that where it differs from AGENTS.md or a package's gate, those win, and section 0 restates the rules the guidance can never relax (CI always runs everything; SQL honesty; independent validation is not self-test; no secrets; no push, PR, merge or force action without the owner; the IAM branch is untouched).

Platform facts included were checked in this package: PowerShell 7.2+ for the toolkit versus 5.1 for `ci/*.ps1` (`#Requires` lines read; the new self-test also passes under Windows PowerShell 5.1); `git worktree remove` deleting ignored files (observed: a 1.4 GB `artifacts/` went with the DEV-1.3A worktree, after archiving); the `.git` file in a linked worktree (read from `RepositoryRoot` and the tests README); MSYS path conversion (reproduced: `pwsh -File x.ps1 /var/opt/mssql` receives `C:/Program Files/Git/var/opt/mssql`; `MSYS_NO_PATHCONV=1` and `//var/...` do not rewrite); `.env` discovery by walking up from the test search roots, so a new worktree has none (read from `SqlServerTestConfiguration`); passing a SQL password to a container by environment variable name (from `Get-SqlEvidence.ps1`). No credential, connection string, token or user name is in any tracked file added or changed here (scan in section 8).

## 4. Templates

| File | Purpose |
|---|---|
| [.ai/prompts/work-package-record.md](../../../../.ai/prompts/work-package-record.md) | One-page record: identity (base, branch, reviewed HEAD, tree), scope and acceptance, changes, environment and commands, test counts (discovered, executed, passed, failed, skipped), SQL execution and both isolation modes, mutation and regression-catch table, evidence and checksums, review verdicts and remediation history, residual risks and owner decisions, PR and closure. It says that numbers come from generated files and that the audit is not to be reproduced |
| [.ai/prompts/independent-review-request.md](../../../../.ai/prompts/independent-review-request.md) | Exact commit, protected properties with intended controls, evidence, what the reviewer reproduces on their own build, bounded questions, required output (verdict, exact acceptance scope, severity-classified findings with failure scenario and required remediation). Tells the implementer not to hand over a conclusion |
| [.ai/prompts/review-closure-verification.md](../../../../.ai/prompts/review-closure-verification.md) | Closure of named findings at a new exact commit: the reviewer's own repro closes a finding; a control must fail on the old behaviour; unlisted changes in the range are reported; fixes after the verdict are recorded as not re-checked |

They preserve the existing obligations (independent reviewer, exact commit, own clean build, both isolation modes where SQL is in scope, a novel control, honesty check where SQL test code changed). They state in the text that the lighter procedure of section 5 is **not** adopted.

## 5. Proposed independent-review procedure (audit E.7): described for later approval, NOT adopted

The audit proposed (E.7, "Independence of review without repetition"):

1. **Verify what hashes and TRX already prove**: subtree hashes equal the reviewed commit; TRX totals, rcsi split and timestamps match the record; SQL markers and container start time consistent; build log 0 errors.
2. **Re-run independently on the reviewer's own clean build**: every new or changed test class on both isolation modes; the classes exercising changed production files that the implementer's filter omitted; at least one novel mutation per claimed property; any skipped or not-run item judged material.
3. **Do not repeat** the whole unit, UI and architecture suites or the full focused set when step 1 passes.
4. Review records gain the same provenance and timing block as implementation records.

Status: **proposal only.** Adoption is a change to how independent validation is done. It needs the owner's and the security owner's explicit authorization, at a named package boundary (DEV-1 plan, section 5 DEV-1.3 and section 9 item 5). Until then the three templates keep the current obligations, including the reviewer's own re-run of the focused set. The DEV-1.3A experience is a caution rather than support: each review round found a further way for something outside the Git tree to change what was compiled, and the finding came from re-running and attacking, not from re-reading hashes. Step 3 in particular assumes the hashes and TRX are themselves trustworthy, which is what a first-use toolkit has not yet earned (open condition 1 below).

## 6. Validation tiers and toolkit guidance: adoption status

The four-tier model (audit section H) is documented in the playbook, section 13, labelled **Proposed, not adopted**. It states the four essential restrictions: Tier 1 is fast feedback and never evidence; Tier 2 needs the preflight and genuine required SQL execution, on both isolation modes; Tier 3 is the entry condition for independent review; Tier 4 is the complete hosted CI and is never narrowed. Focused selection never permits bypassing CI. The toolkit's provenance classification covers known environmental influences only and is not proof of a hermetic build.

Unresolved adoption conditions (restated from the DEV-1.3A record and README, none changed by this package):

1. The first real toolkit-backed work-package record must run alongside the established procedure and be compared with it.
2. The toolkit self-test is not in the hosted CI; adding it is a separate owner decision.
3. Linux compatibility of the scripts is not established (Windows only was exercised). The new `Test-TestEntryPoint.ps1` shim for Unix is written and unverified.
4. `Invoke-Mutation -BuildArgs` is unvetted.
5. DEV-1.3C owns the measured build-flag experiment and any proposed build-argument allow-list. The established build flags (`--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers`) stand and the playbook says not to change them.

## 7. `scripts/ci/test.ps1`: defect and repair

**Verified defect.** On the base commit `pwsh scripts/ci/test.ps1 -Filter NoSuchClass` exits 1 with "The term '...\scripts\dev\Get-DevDotNetSdk.ps1' is not recognized". The helper was deleted on 2026-06-18 (audit E.3). `CONTRIBUTING.md` named the script as the supported test command, so every documented invocation failed.

**Repair (minimal, same contract as the sibling `build.ps1`).** The SDK-resolution lines are gone: the script uses `dotnet` from `PATH` (global.json is applied by `dotnet` from the repository root) and stops with a clear message if there is none. Two small additions serve the "never silently select the wrong scope" requirement, and nothing else changed:

- `-Filter` accepts only `[A-Za-z0-9_.]` (a `ValidatePattern`), so it cannot carry `|`, `&`, `!`, `=`, `~` or a second property into `FullyQualifiedName~<fragment>`.
- Each run writes TRX files to a fresh folder `artifacts/test-results/<utc>/`, and after `dotnet test` exits 0 the script requires at least one TRX with at least one test **executed**, no recorded failure, and prints passed, skipped and failed counts. `dotnet test` exits 0 when a filter matches no test (observed in this package: nine TRX files, zero executed), which previously could not be told from success. A non-zero test-host exit is still a thrown failure, so the exit code of the script is non-zero for failing tests, a crashed host, an unreachable prerequisite and a missing SDK.

Not changed: no environment file is read, no credential is touched or printed, `APPERTURE_*`/`MSSQL_*` variables pass through untouched, SQL tests keep failing without SQL (the script has no skip or optional-SQL switch), `.github/workflows/ci.yml` is untouched (CI does not use this script), no redesign of test orchestration. A `-Project` or multi-class filter was considered and left out as scope growth.

**Verification** ([scripts/ci/tests/Test-TestEntryPoint.ps1](../../../../scripts/ci/tests/Test-TestEntryPoint.ps1), a stand-in `dotnet` on `PATH`, a throw-away repository root; not part of the hosted CI):

| Check | Result |
|---|---|
| 26 controls under PowerShell 7.6 and under Windows PowerShell 5.1 | **26 passed, 0 failed** on each |
| What they cover | passing run and its arguments; `-Configuration`/`-NoBuild`/`-Filter` reach `dotnet`; failing run; crashed host (137); host exit 0 with a failing TRX; filter matching nothing; all tests skipped; skipped reported but not fatal; counts summed over assemblies; seven unsafe filter strings rejected before `dotnet` is called; no `dotnet` on `PATH`; missing solution; a TRX from an earlier run cannot stand in; the removed helper is not referenced; no `.env`/credential/skip switch in the text; every repository file the script names exists |
| Mutation checks on `test.ps1` (copies, via `-ScriptPath`) | **9 mutations: 8 caught, 1 equivalent**, plus a corrected variant caught. Caught: exit code ignored (2 controls fail), zero-executed accepted (3), filter unvalidated (6), TRX failure count ignored (1), removed helper referenced again (12), missing `dotnet` undetected (1), shared results folder (2), missing solution undetected (1). M8 (the *executed* accumulator replaced by assignment) is an **equivalent mutant**: that counter only feeds a `> 0` test; the corresponding *passed* accumulator (M8b) is caught by "counts are summed over every test assembly". The original `test.ps1` hash was unchanged afterwards |
| Real run, bounded selection | `pwsh scripts/ci/test.ps1 -Filter PasswordPolicyEvaluatorTests`: solution built, **17 passed, 0 failed, exit 0**, nine test assemblies (eight ran nothing) |
| Real failure propagation | `-NoBuild -Filter ApiPollingDurableSyncStateSqlServerTests` with no SQL target configured (`APPERTURE_TEST_SQL_PROCESS_ENV_ONLY=1`, connection variables removed): both SQL tests **fail** with `SqlTestUnavailableException`, "Tests failed (dotnet test exit code 1)", **exit 1**. SQL unavailability is not a green skip |
| Real wrong-scope runs | `-Filter NoSuchClassAnywhere`: **exit 1**, "No test was executed (9 result file(s) ...)". `-Filter 'A\|B'`: rejected by parameter validation, **exit 1** |
| Real CI-adjacent test class | `Swm12WorkerHostingHygieneTests` (the tests that read `CONTRIBUTING.md` and `scripts/README.md`): 9 of 9 passed through the repaired entry point |

Two defects in the self-test itself were found and fixed while it was written (the kind the package exists to prevent): a `[string]` parameter defaulting to `$null` becomes the empty string and silently emptied `PATH`; and an `if`-expression unrolled a one-element array so the call-log assertions looked at the first *character* of the line, which made three positive controls fail for the wrong reason. The first run also showed that Windows PowerShell 5.1 leaves `$PSScriptRoot` empty inside a `param` default and turns a child's stderr into error records under `$ErrorActionPreference='Stop'`.

## 8. Documentary validation

| Check | Result |
|---|---|
| Referenced files exist | Every markdown link and every `scripts/`, `tests/`, `docs/`, `src/`, `.ai/`, `.github/` path named in the 21 changed or added documents and scripts was checked by script. The only misses were prose (`docs/tests/comments`), generated or gitignored paths the existing text already named (`scripts/.runtime/...`, an `appsettings.Development.json` the reader creates), the intentionally absent `start-worker.ps1`, and one pre-existing broken relative link in the Cursor rule (fixed) |
| Referenced commands and parameters exist | Every script and parameter the playbook names (`preflight`, `Invoke-Evidence -Record -Plan -ExpectedCommit -SqlContainer -Build`, `Compare-Trx -Baseline -Candidate -Strict`, `Invoke-Mutation -Mutations -Commit -Workspace -BuildArgs`, `Export-Evidence -Source -DestinationRoot -Name -Restrict`, `Select-Tests`, `Get-SqlEvidence`, `Test-SqlExecutionHonesty -NoBuild`) exists with that parameter name |
| Guidance agrees with AGENTS.md | Grep of `.ai`, `.cursor`, `CONTRIBUTING.md`, `tests/README.md`, `scripts/README.md` for module-per-process, DTFx, Durable Functions and adapter wording after the edits: no remaining statement that presents an adapter as implemented or one-module-per-process as current. ADR-0001's own text and `instance-plane.md` are reported (section 2.2) |
| Nothing suggests reducing mandatory CI | The playbook states the rule first (section 0), repeats it in the tier table (Tier 4 never narrowed, "no path filters, no selective runs"), and the templates say the same. The word "narrow" appears only to forbid it |
| No sensitive local information | Pattern scan of the 21 files (passwords, connection-string credentials, tokens, keys, user and machine names, user profile paths): no hit in anything added or edited here. Three hits in `tests/README.md` are the existing `Password=...` placeholders in connection-string examples |
| Toolkit negative controls | The toolkit was **not** modified, so its 345-control self-test (DEV-1.3A) was not re-run; the changes here touch no file it reads (`tests/test-manifest.json` is unchanged; it lists `docs/` mentions, and the toolkit README's statement that documents are counted is unchanged) |
| Existing tests that read changed documents | `Swm12WorkerHostingHygieneTests` 9/9 (above). A scan of `tests/` for tests that read `.ai`, `.cursor`, `CONTRIBUTING.md`, `scripts/README.md`, `tests/README.md` or `docs/superpowers` found only that class, plus mentions in comments, the test manifest and the integration-test READMEs |

**Walkthrough.** The package was itself run through the playbook where non-destructive: starting from updated `origin/master` in a sibling worktree (section 1), protecting the IAM branch (read-only checks of `6e3ab57`), a focused iteration run through `test.ps1` (section 7), and closing the previous package (section 1: merge, post-merge CI, ancestor check, archive, hash comparison, post-PR supplement, ordinary `worktree remove` and `branch -d`). No evidence run, no mutation cycle of product code and no build-flag change was done: they would validate the toolkit or the build protocol, not this documentation.

## 9. Changed files

New: `.ai/playbooks/dev-loop.md`; `.ai/prompts/{work-package-record,independent-review-request,review-closure-verification}.md`; `docs/superpowers/README.md`; `scripts/ci/tests/Test-TestEntryPoint.ps1`; this record. Edited: `.ai/README.md`; `.ai/agents/{qa-agent,coding-agent}.md`; `.ai/prompts/architecture-review.md`; `.ai/playbooks/{runtime-portability,anti-patterns,project-map}.md`; `.ai/memory/{context-pack,glossary,decisions-index}.md`; `.cursor/rules/project.mdc`; `CONTRIBUTING.md`; `tests/README.md` (one clause); `scripts/README.md`; `scripts/ci/test.ps1`; `dev1-development-loop-plan.md` (status bullet).

## 10. Independent review

To be completed. (Opus 5.5 high, bounded to F19; see the review request and verdicts recorded here.)

## 11. Remaining risks and owner decisions

- **The playbook is guidance, not enforcement.** Nothing tests that it stays true. Section 14 of the playbook lists where each fact lives, so a change to one of them is a cue to update it. No test pins the playbook text, on purpose: a pin would block honest edits and prove nothing about the commands.
- **"Current" for the established procedure rests on records that live on the unmerged IAM branch.** The playbook describes the procedure from the audit (E.1), the DEV-1 plan and the DEV-1.3A record; it cites no IAM-only file. If the IAM records state a stricter rule for a package, that rule governs.
- **`test.ps1` was exercised on Windows only.** The Unix shim of its self-test is unverified, and the script is not run by the hosted CI.
- **`-Project`/multi-class selection is absent from `test.ps1`**; developers needing it call `dotnet test` directly (the playbook shows how, with `TreatNoTestsAsError`).
- **Owner decisions** (none needed to merge this package): whether ADR-0001 gets an amendment note and `instance-plane.md` a status label (section 2.2); whether the roadmap's DEV-1.3 row is refreshed; whether, and when, to authorize the section 5 review procedure (owner **and** security owner); whether the toolkit self-test and `Test-TestEntryPoint.ps1` join the hosted CI (which would need the Linux behaviour verified).
- **Not done, by instruction:** no push, PR or merge; no DEV-1.3C work; IAM WP4B-C untouched; no `ci.yml` change; no deferred artifact cleanup.

## 12. Recommended DEV-1.3C entry conditions

1. This package is reviewed, accepted and merged, and its post-merge CI is green.
2. The owner chooses the build-protocol experiment's acceptance numbers from the DEV-1 plan (four clean builds, 71 warnings and 0 errors, no node crash, trough above 500 MB, at least 35% wall saving, identical assembly hashes flagged versus unflagged) or restates them for the current tree; the plan's targets were measured on a memory-starved machine, so the experiment needs a stated free-memory floor.
3. The experiment runs from a clean worktree in a clean shell (the toolkit's own rule), with `Invoke-Evidence -BuildArgs` still an allow-list; the proposed flag allow-list is reviewed on its own, and `Invoke-Mutation -BuildArgs` is vetted or kept out of the protocol.
4. The established build flags stay in force for every record until a package boundary adopts a change, stated in that package's record, with the security owner's agreement (DEV-1 plan, section 9 item 5).
5. The first toolkit-backed record, run beside the established procedure, is used as the comparison the experiment needs (open condition 1).
6. Linux behaviour of the build scripts is verified or the experiment is explicitly Windows-only.
