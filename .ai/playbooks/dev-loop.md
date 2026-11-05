# Development loop playbook

How to take a change from a fresh branch to a closed, evidenced, merged package in this repository. Written for engineers and agents. It describes the procedure that exists today and marks clearly what is only proposed.

**Adoption labels used on this page**

| Label | Meaning |
|---|---|
| **Current** | Required today. Stated by AGENTS.md, CONTRIBUTING.md, the RC1 program gate, the DEV-1 plan, the required CI, or the established practice recorded in the work-package records and their independent reviews. A package's own gate text may add to it and never subtracts from it. |
| **Toolkit** | Tracked tooling in [scripts/dev/validation/](../../scripts/dev/validation/README.md) that may be used **alongside** the current procedure. It has **not** replaced any current validation step. |
| **Proposed** | Described for a later decision. Not adopted. Do not follow it in place of a Current step. |

Where this page and a package's gate or AGENTS.md differ, AGENTS.md and the gate win. Historical audit numbers (timings, counts) are dated evidence, not instructions: [dev-loop audit, 2026-10-29](../../docs/audits/dev-loop-audit-2026-10-29.md).

## 0. Rules that never bend

1. **The required hosted CI always runs every test.** Checks `build-and-test` and `browser-security` must pass before merge ([ci.yml](../../.github/workflows/ci.yml), [CONTRIBUTING.md](../../CONTRIBUTING.md)). No focused run, tool, tier or selector replaces or narrows it. Nothing in this playbook changes a required check.
2. **A test never passes because SQL Server was unavailable.** SQL-dependent tests fail when their SQL execution cannot happen. There is no optional mode and no skip switch ([tests/README.md](../../tests/README.md), "A SQL test that cannot run against SQL Server fails"). A skipped, undiscovered or never-run test is not a pass. Never loosen the honesty allow-lists or `scripts/dev/Test-SqlExecutionHonesty.ps1` to get a green run.
3. **Independent validation is not replaced by the implementer's own results.** Your self-test and your summary are not an independent review. Review-protocol changes need the owner's and the security owner's authorization (section 9).
4. **No credentials, tokens or machine-local secrets in tracked files, command lines, logs or records.** Pass a SQL password to a child process by environment variable, never as an argument.
5. **Nothing is pushed, opened as a PR, merged, force-deleted or cleaned up without the owner's instruction** for that action. Approval for one step is not approval for the next.
6. **The preserved IAM branch and its checkout are untouched** (section 2).

## 1. Start a package

**Current.**

1. Fetch, then branch from the fresh `origin/master`, never from the IAM branch:

   ```powershell
   git fetch origin
   git worktree add -b dev/<package-name> ..\wt-<short-name> origin/master
   ```

   If a non-interactive `git fetch origin` over HTTPS fails with "could not read Username", fetch once with the GitHub CLI as a credential helper, without changing any configuration: `git -c credential.helper= -c 'credential.helper=!"<path to gh>" auth git-credential' fetch origin`. `gh` may not be on `PATH` (on the maintainer's Windows machine it is under `C:\Program Files\GitHub CLI\`).
2. Work in the sibling worktree (`F:\Code\Apperture\wt-*` is the convention). One package, one branch, one worktree. A branch started this way tracks `origin/master`; that is a convenience, and a bare `git push` still requires the owner's instruction.
3. Write a short plan first: base commit (`git rev-parse origin/master`), scope, acceptance criteria, which counts you re-measured on that base (every package re-counts on its own base before it changes anything: [DEV-1 plan](../../docs/01-architecture/transitions/dev-loop/dev1-development-loop-plan.md), section 6). Start the work-package record (section 5) now, not at the end.
4. Before any PR: `git log --oneline origin/master..HEAD` shows only your commits, and, while the IAM branch is unmerged, `git merge-base --is-ancestor 478ae7a HEAD` is false (the IAM branch's work is not in yours; the check is from the DEV-1 plan, section 7).

## 2. Protect preserved references

**Current.**

- `feat/iam-10.2b-administrator-viability` is **local-only** (no upstream) and carries unmerged work. Its checkout is the primary working folder. Do not base work on it, rebase it, merge into it, delete it, run `git gc --prune`, or run index-writing commands in that checkout for non-IAM work. Merging `master` into it happens only at a package boundary on the owner's instruction.
- Preserved safety nets are named in the [DEV-1 plan](../../docs/01-architecture/transitions/dev-loop/dev1-development-loop-plan.md) section 2: git bundles on two local drives, and a local-only `archive/` tag. Do not delete or move them. A stale zero-byte `.git/index.lock` may exist in the IAM checkout; removing it is the owner's decision after confirming no git process runs.
- Branch and worktree retirement is **explicit and ordinary**: `git worktree remove <path>` (no `--force`) and `git branch -d <branch>` (no `-D`). `git branch -d` warning that a branch is "not yet merged to HEAD" is expected when it was merged on `origin`; check the merge yourself first (section 11).
- **`git worktree remove` deletes the worktree's ignored files, including all of its `artifacts/`.** Archive anything you need before you remove it (section 11).
- Never use `git clean`, `git reset --hard`, forced deletes or forced pushes unless the owner asked for that exact action.

## 3. Establish a clean build and test environment

**Current.**

- **PowerShell.** The validation toolkit, the preflight and `scripts/dev/Test-SqlExecutionHonesty.ps1` need **PowerShell 7.2+** (`pwsh`). `scripts/ci/*.ps1` and `Manage.ps1` also run on Windows PowerShell 5.1. Windows PowerShell 5.1 is unsupported for the toolkit and stops at its `#Requires` line.
- **SDK.** The .NET SDK that [global.json](../../global.json) names (`dotnet` applies it from the repository root).
- **Machine headroom.** Builds and the SQL container stall under memory pressure (the audit saw the same clean build take 10 min on a starved machine and under 5 min on a free one). Close heavy tools and check free memory before an evidence run.
- **Stop the dev stack before validation builds.** Aspire AppHost and native `Manage.ps1` runs lock build outputs in the folder they started from. A package worktree has its own `bin/` and `obj/`, which avoids most of it. Never kill a process you did not start.
- **Test SQL.** The disposable test SQL server is the `apperture-test-sql` container at `127.0.0.1,1433` (start it with `docker start apperture-test-sql`; Docker Desktop must be running). For a throwaway container on a dynamic port use `scripts/dev/maintenance/start-adhoc-test-sql.ps1`. **Port 14333 is the Aspire development database and is never a test target**; the test configuration refuses it ([tests/README.md](../../tests/README.md), "Test SQL target safety").
- **Test connection string.** Set `APPERTURE_TEST_SQL_CONNECTION_STRING` in the process environment of the shell that runs the tests. Tests also read a git-ignored repo-root `.env`, found by walking up (at most 12 levels) from their search roots, and a **new worktree has no `.env`**; `APPERTURE_TEST_SQL_PROCESS_ENV_ONLY=1` makes the tests ignore `.env` entirely. Do not print, commit or record the value.
- **Build output location.** For evidence builds use a fresh `--artifacts-path` directly under the repository's ignored `artifacts/` (not a system temp folder, not a deeply nested path: some tests locate the repository root from the test binaries, and the honesty script requires its output inside the repository).
- **`.git` is a file in a linked worktree**, not a directory. The shared test helper `RepositoryRoot.Find()` handles both; do not write another finder that checks for a `.git` directory only. One allow-listed honesty test (`requires-git-directory`) may fail in a worktree and must pass in an ordinary clone.
- **Clean tree.** Evidence is produced from a clean tracked tree at the commit you name, in a clean shell: no `MSBUILD*`, `DOTNET_*` or `NUGET_*` customization, nothing in your user MSBuild import folders, no untracked or ignored build inputs (`*.user`, `*.rsp`, `*.props`, `*.targets`, stray sources) near `src/` or `tests/`. The primary checkout carries an ignored `.csproj.user` that the toolkit counts, which is one reason evidence is made in a worktree.

## 4. Iterate with focused tests

**Current** (iteration only; never evidence).

- Build and run what you touched. One project: `dotnet test tests/<Project>/<Project>.csproj --filter "FullyQualifiedName~<ClassName>" -- RunConfiguration.TreatNoTestsAsError=true` (the last argument makes a filter that matches nothing fail instead of exiting 0).
- Whole solution or one name fragment: [`scripts/ci/test.ps1`](../../scripts/ci/test.ps1) (`-Filter`, `-Configuration`, `-NoBuild`). It fails when no test executed. It is a local convenience; CI does not use it.
- A class whose production code you changed but whose test you did not run is the usual gap. Include the source-closure guard classes (tests that read the repository's source) and, for SQL work, the classes that exercise the tables you changed.
- SQL-backed tests need the test SQL server (section 3). Without it they fail, which is correct.

**Toolkit (alongside).** `pwsh scripts/dev/validation/Select-Tests.ps1` turns a change set into candidate classes and a focused filter from [tests/test-manifest.json](../../tests/test-manifest.json), and reports what it could not map (`UNMAPPED`, `MISSING`, `AMBIGUOUS`). It plans iteration and evidence; it never narrows CI. A production file the manifest does not claim is a cue to add a rule, not a reason to assume nothing is affected.

## 5. Authoritative validation and the work-package record

**Current (established procedure).** For a package whose gate requires evidence, as practised in the IAM-10.2b records and reviews (audit E.1):

1. A clean, isolated build of the committed tree, with the protocol flags `--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers` and a fresh `--artifacts-path`. **Do not change these flags**; reviewing them is DEV-1.3C's job and they are not adopted for change (section 7).
2. The non-SQL suites (UnitTests, ArchTests, Ui.Shared, Ui.Client and the other test projects) and the focused SQL set, **required SQL execution, in both isolation modes** (the read-committed-snapshot setting, shown as `rcsi` False and True in test names). A single mode is for iteration only.
3. A SQL execution proof: show that SQL tests really wrote to the server, not just that they passed.
4. Mutation checks for each claimed protected property (section 7 of this page).
5. A written record with the commit, tree, environment, counts and results, and a pointer to preserved evidence.
6. `pwsh scripts/dev/Test-SqlExecutionHonesty.ps1 -Configuration Release` (add `-NoBuild` to reuse the Release build, it must be newer than every source file) after changing SQL test code, helpers or fixtures. CI runs it too.

**Toolkit (alongside the established procedure, not instead of it).**

```powershell
pwsh scripts/dev/preflight.ps1 ...                         # may this run claim to be evidence?
pwsh scripts/dev/validation/Invoke-Evidence.ps1 -Record <name> -Plan plan.json -ExpectedCommit <sha> -SqlContainer apperture-test-sql -Build
pwsh scripts/dev/validation/Compare-Trx.ps1 -Baseline <accepted.trx> -Candidate <new.trx> -Strict
```

It writes `provenance.json`, `summary.md`, TRX, redacted logs and `SHA256SUMS.txt` under `artifacts/evidence/<record>/<utc>-<sha8>/` (see [the toolkit README](../../scripts/dev/validation/README.md) for the exact usage and refusals). Fill the record's numbers **from those generated files**, not by typing them from console output. The first real toolkit-backed record must run beside the established procedure; its numbers are compared with the established ones before anyone relies on it.

**The record.** Copy [.ai/prompts/work-package-record.md](../prompts/work-package-record.md) next to the package's other documents (the existing pattern is `docs/01-architecture/transitions/<area>/<package>-record.md`). Keep it short: do not reproduce the audit in it.

**What "authoritative" means.** `provenance.json` says a run is authoritative when no *known* channel was found by which the binary could differ from the named commit. It is **not** proof that the build was hermetic. For a result that must hold regardless, make it from a clean worktree in a clean shell and keep `provenance.json` with the record.

## 6. Evidence and provenance

**Current.** A record states the exact commit and tree it validates, is made from a clean tree, names where the raw evidence is, and gives checksums. Evidence lives in a folder of its own under `artifacts/` and **never inside a build folder** ([retention rules](../../docs/01-architecture/transitions/dev-loop/dev1-2c-artifact-retention.md)). Counts come from TRX files (total, passed, failed, skipped, not executed; discovered versus executed), not from memory. A skipped or undiscovered test is reported, never counted as passed.

**Toolkit.** Evidence folders carry the checksums and provenance. Archive finished evidence with `Export-Evidence.ps1` (section 11). That copy goes to a second **local** drive: it protects against one drive failing and is **not** an off-machine backup.

## 7. Mutation checks

**Current.** For every protected property a package claims (an authorization gate, a lock order, a refusal), show the break that the tests catch: change the production code so the property is violated, run the tests that should notice, and require a failure. A mutation whose tests all pass **escaped** and is a failed result, not a footnote. Record each mutation, the tests that failed, and the SQL-only catches (some are caught only by the SQL layer; the unit run alone would pass). The source is restored byte for byte and proven so (hash against the committed blob), and the restored tree is rebuilt. A failure of the environment (container down, build error) is `Invalid`, neither caught nor escaped. Never mutate the checkout you are validating in place.

**Toolkit.** `Invoke-Mutation.ps1 -Mutations mutations.json -Commit <sha> -Workspace <path>` runs the mutations in a disposable clone outside your checkout, with a green baseline first, proof that the edit reached the compiled assembly, and a restore check. It refuses a workspace inside any protected checkout. `-BuildArgs` is an allow-list (`-nologo`, `-v`, `-m`, `-clp`, `-nr`) for evidence builds; **`Invoke-Mutation -BuildArgs` remains unvetted.** Do not run an expensive full mutation cycle merely to validate documentation or guidance.

## 8. Request independent review

**Current.** A package that a gate marks for review gets an independent reviewer: a different agent or person, working from the **code at an exact commit**, who re-runs what they need on their own clean build. Give the reviewer the request template ([.ai/prompts/independent-review-request.md](../prompts/independent-review-request.md)), not your conclusion: the protected properties, the evidence to inspect, the tests to reproduce, and the commit. Do not hand over only a summary you wrote. The reviewer classifies findings by severity and ends with a verdict and the exact scope it accepts. An implementer's self-test results never stand in for any of it.

## 9. Address findings

**Current.** Fix each finding with a test or control that **fails on the old behaviour** (show it). Commit the remediation as its own commit and record it in the package record's remediation history. Then request closure verification of those findings at the new exact commit with [.ai/prompts/review-closure-verification.md](../prompts/review-closure-verification.md). A finding is closed when the reviewer's own repro shows it closed, not when the implementer says so. Stop on named items: do not widen the review into a new audit. A fix made after the last verdict and not re-checked by the reviewer is recorded as exactly that.

**Proposed (not adopted).** The audit's improved review procedure (verify hashes and TRX first; re-run only new and changed classes plus at least one novel mutation per claimed property; no blanket repeat of the full focused set when the first step passes) is described in [the DEV-1.3B record](../../docs/01-architecture/transitions/dev-loop/dev1-3b-development-guidance.md), section 5. It changes how independent review is done, so it needs the owner's **and** the security owner's authorization. Until then the templates preserve the existing obligations.

## 10. Submit the PR and validate hosted CI

**Current.**

1. Only on the owner's instruction: push the branch and open a PR to `master` (never to the IAM branch). Include the record and any third-party-material statement CONTRIBUTING.md requires. Do not alter licensing, CLA or copyright terms.
2. The PR head must show both required checks green. Read them from GitHub (`gh pr view <n> --json statusCheckRollup`, `gh run list --branch <branch> --json headSha,conclusion`), and confirm the run's `headSha` is the PR head, not an older commit. A run for an earlier commit proves nothing about this one.
3. After the merge, confirm CI **on the merge commit on `master`** (`gh run list --branch master --json headSha,conclusion`), not only the PR run. The merge result is a different tree from the PR head.
4. A red required check is investigated, not retried until it passes. If it exposes a real failure the hosted run found, that is a finding.

## 11. Close a merged package and archive its evidence

**Current.** Close only after: the PR is merged (record the merge commit), the post-merge CI on that commit succeeded, the package branch tip is an ancestor of `origin/master` (`git merge-base --is-ancestor <tip> origin/master`), and the worktree has no uncommitted work.

1. **Archive first.** Copy the evidence to a verified dated folder on another local drive with `Export-Evidence.ps1` or an equivalent that writes a checksum manifest and re-hashes the copy. Add records created after the original archive (final review logs, PR metadata, post-merge CI summaries) as a `post-pr-supplement\` folder with its **own** checksum file; leave the original manifest untouched.
2. **Check nothing unique is lost.** Hash-compare the worktree's ignored folders against the archive. Anything not in the archive is either copied or regenerable (build output is regenerable; evidence is not).
3. **Retire with ordinary commands:** `git worktree remove <path>`, then `git branch -d <branch>`. No force. If either refuses, stop and find out why.
4. Update the package's record with the PR, merge commit, post-merge CI result and the archive location (a short closing note; do not rewrite earlier sections).
5. Ask before any deferred cleanup of `artifacts/`; cleanup is manual, by exact name, and owner-approved ([retention rules](../../docs/01-architecture/transitions/dev-loop/dev1-2c-artifact-retention.md)).

## 12. Platform notes (verified on Windows with Git Bash and PowerShell 7)

- **MSYS path conversion.** In Git Bash a leading-slash argument passed to a native Windows program is rewritten: `pwsh -File x.ps1 /var/opt/mssql` receives `C:/Program Files/Git/var/opt/mssql`. Set `MSYS_NO_PATHCONV=1` for that command, or double the slash (`//var/opt/mssql`). This bites `docker exec` and `docker cp` container paths. PowerShell itself does not rewrite them.
- **`pwsh -File` and a value starting with `-`** is read as the next parameter name. Pass it attached: `-BuildArgs:-nologo,-v:q`. List parameters take one comma-separated value under `-File`.
- **PowerShell variable names are case-insensitive**, and a few command names are aliases (`cls`, `fw`): do not name a function `Cls`.
- **SQL password to a container.** `docker exec -e SQLCMDPASSWORD <container> ...` forwards the variable from your process by name, so the value is on no command line. Never use `-P <password>`.
- **Docker Desktop restarts and Windows reboots** can kill a background run and its container. After one, re-verify your edits, scratch folders and logs; do not trust a half-finished run.
- **Long runs.** Poll a long `dotnet test` from the shell that started it (check its log file or exit code). Do not leave a build and a SQL run competing for memory.
- **Files in a worktree.** Use short sibling paths (`F:\Code\Apperture\wt-<short>`). Deep paths and `--artifacts-path` nesting break fixed-depth lookups and Windows path limits.
- **Line endings.** Mutation scripts must match the file's line endings (CRLF versus LF); a `find` string that spans a line break fails otherwise.

## 13. Validation tiers (Proposed, not adopted)

The four-tier model from audit section H is a proposal. It is documented here so that people use the same words, **not** as a change to the required procedure. Adoption needs an owner decision at a package boundary.

| Tier | Intent | What would run | Evidence? |
|---|---|---|---|
| 1. Fast developer feedback | every edit cycle | incremental build of touched projects; non-SQL tests filtered to the changed area plus the source-closure guards; no SQL | **Never evidence** |
| 2. Work-package validation | before a record; after each mutation | the SQL classes for the changed area on **both** isolation modes; the mutation round; SQL write-marker proof; `Compare-Trx` against the last accepted baseline | Evidence, **only after the preflight passes and with required SQL execution** |
| 3. Broad integration validation | before independent review or a PR | the whole IntegrationTests project; the whole non-SQL suites; the browser suite when authentication, session, shell or request trust changed | Entry condition for independent review |
| 4. Full CI and release verification | every push and PR to master; the release candidate | `build-and-test` and `browser-security` exactly as they are | **Never narrowed**: no path filters, no selective runs, every shard asserting its executed count if sharding ever lands |

Restrictions that hold with or without the model: focused test selection never permits bypassing required CI; the toolkit's provenance classification covers known environmental influences only and is not a proof of a hermetic build; the current required procedure is sections 5 to 11.

**Open conditions before the toolkit can be relied on in place of hand-assembled evidence** (none is met yet):

1. The first real toolkit-backed work-package record must run alongside the established procedure and be compared with it.
2. The toolkit self-test (`scripts/dev/validation/tests/Test-Validation.ps1`) is **not** in the hosted CI, and putting it there is a separate, owner-approved decision.
3. **Linux compatibility of the scripts is not established.** They were exercised on Windows only.
4. `Invoke-Mutation -BuildArgs` is unvetted.
5. DEV-1.3C owns the measured build-flag experiment and any proposed build-argument allow-list. Until it reports and is adopted, the build protocol in section 5 stands.

## 14. Where to look

| Need | Go to |
|---|---|
| Non-negotiables | [AGENTS.md](../../AGENTS.md), [anti-patterns](anti-patterns.md), [principles](principles.md) |
| SQL test rules, target safety, opt-in diagnostics | [tests/README.md](../../tests/README.md) |
| Scripts | [scripts/README.md](../../scripts/README.md), [validation toolkit](../../scripts/dev/validation/README.md) |
| The DEV-1 plan and its records | [dev-loop folder](../../docs/01-architecture/transitions/dev-loop/) |
| Record and review templates | [.ai/prompts/](../prompts/) |
| Completed historical plans (not instructions) | [docs/superpowers/README.md](../../docs/superpowers/README.md) |
