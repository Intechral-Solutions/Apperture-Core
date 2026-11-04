# DEV-1.2C: artifact retention rule and tiered cleanup (F16)

Status: **executed 2026-11-03 except tier 1B (deferred) and tier 3 (excluded); see section 7 for the actual results and section 5 for the owner's rulings.** Sections 1 to 4 are the
proposal as reviewed and authorised; their figures were measured on 2026-11-03 against `origin/master` `9d20ca0`. Citations of removed folders resolve through the
[reference-resolution index](dev1-2c-artifact-reference-index.md). Scope: [dev1-development-loop-plan.md](dev1-development-loop-plan.md) (DEV-1.2, F16) and
[dev-loop-audit-2026-10-29.md](../../../audits/dev-loop-audit-2026-10-29.md) (F.1, register row 16). Sizes are GiB (1,024³ bytes) as `robocopy`/Explorer report them.

## 1. What is on the disk now

The audit's figures were taken on 2026-10-29. These were re-measured; where they differ, the new figure is the one to use.

| Place | Audit (10-29) | Measured (11-03) | Note |
|---|---|---|---|
| Root `artifacts/` | 178 GB, 2.77 M files | **173.9 GiB, 2.79 M files**, 118 folders and 120 loose files | 162.4 GiB of it is isolated-build output (`bin`/`obj`/`publish` trees); 11.5 GiB is everything else |
| Of that, `artifacts/v94` | 3.7 GB | 3.6 GiB | a registered, clean, detached Git worktree whose only content is ignored build output |
| Stray `artifacts/` trees under `src/`, `tests/` | 56 trees, 2026-09-26 | **64 trees, 17.9 GiB**, all 2026-09-26 | `Management.Host` alone holds 6.2 GiB; `BrowserTests` 4.3 GiB |
| In-place `bin`/`obj` under `src/`, `tests/`, `poc/` | 1,483 folders, 156 k files | **133 project folders, 19.7 GiB `bin` + 0.5 GiB `obj`, 404 k files** | the test projects' `bin` was rebuilt on 2026-11-02 |
| `TestResults/` in test projects | 92 MB | 5 folders, 31 TRX, 92 MB | real run evidence (WP2 to WP4 runs, WP3 mutation proofs) |
| Drive F: | 92 % full | **84 % full: 779.5 GiB used, 152 GiB free** | |

No directory junction, symlink or other reparse point exists in any of it, and the only nested Git repositories are `artifacts/v94` (the worktree) and the standalone clone
`artifacts/dev1-0-restore-check/clone`. Nothing in these folders is tracked by Git (`git ls-files` finds no tracked path under any `artifacts`, `bin`, `obj` or `TestResults`
folder on `master` or the IAM branch) and all of it is ignored by `.gitignore`.

## 2. The retention convention

Three classes, told apart by what a folder is for, not by its age.

| Class | What | Where | Lifetime |
|---|---|---|---|
| **Evidence** (keep) | Accepted test results (TRX), mutation proof (the `.bak` backups and TRX), independent-review records, logs and screenshots that back a claim, reproduction scripts, commit messages and provenance (commit SHA, command) | `artifacts/<package>-evidence/` (or an existing `*-logs`, `*-results` folder) | Copied to the restricted off-machine archive when the package closes, with a SHA-256 manifest verified from the copy; the working copy stays until the owner agrees to remove it |
| **Regenerable** (delete at close) | Build intermediates, disposable test binaries, publish output, isolated `--artifacts-path` builds, stray content copies | `artifacts/build/<package>-<purpose>/` | Open while the package is open; removed at package close once the evidence beside it is archived |
| **Active** (leave alone) | The current package's builds, registered worktrees, folders a script or test writes (`artifacts/devlogs`, `artifacts/iam95-visual`, `artifacts/sql-execution-honesty`) | as above, worktrees as siblings of the checkout | Until the package closes |

Five rules keep the classes apart. They are the cause of what the inventory found, so they matter more than the thresholds.

1. **Evidence never lives inside a build folder.** Several `aud*` and `sec1-*` folders hold a few MB of TRX beside 4 to 7 GiB of build output, so they cannot be deleted without a copy step.
2. **Give `--artifacts-path` an absolute path or one relative to the repository root, never a bare relative path run from a project folder.** A relative path resolves per project and produced the 64 stray trees under `src/` and `tests/` (17.9 GiB), which the Web SDK content glob also copies into every `Management.Host` build.
3. **Never create a worktree or a clone inside `artifacts/`.** `artifacts/v94` and `artifacts/dev1-0-restore-check/clone` need worktree-aware handling and hide a Git repository inside an "ignored" folder. Put worktrees next to the checkout (`F:\Code\Apperture\wt-*`).
4. **Records cite evidence by its archive location and mark build paths as regenerable.** A record that says "builds used `--artifacts-path artifacts/aud14`" documents a recipe, not a file that must exist.
5. **Cleanup is manual, by exact folder name, from a checklist.** No wildcard, no script that decides by itself what is old enough; a person (or a reviewed list) names each folder. No database, no background service.

### Package-close checklist (manual, about ten minutes)

1. Every claim in the package record points to evidence that exists: a TRX, a log, a mutation `.bak`, a screenshot, with the commit SHA.
2. Copy `artifacts/<package>-evidence/` (and the package's `*-logs`/`*-results` folders) to a new dated, access-restricted folder on the off-machine drive; write a SHA-256 manifest with LF line endings; re-hash the copy and the source and compare (`sha256sum -c`).
3. List the build folders you did not copy in the archive README (they are regenerable), with the commit they were built from.
4. Only now, and only if the package is closed, delete each of the package's `artifacts/build/<package>-<purpose>` folders, one by one, by exact name. Close VS Code and run `dotnet build-server shutdown` first if you also clear in-place `bin`/`obj`.
5. `git worktree list`: remove the package's worktree with `git worktree remove` (no `--force`), then `git branch -d` from another worktree.
6. If the free space on F: is under 100 GiB, run this checklist for every closed package before starting the next.

## 3. Proposed cleanup tiers

The exact folder lists, sizes, file counts, newest/oldest write times, tracked references and the dry-run-by-default scripts are in the local evidence folder
(`artifacts/dev12c/manifest/*.csv`, `artifacts/dev12c/cleanup/*.ps1`; not committed). Every script re-verifies each target before it deletes anything: the exact path, a
file count equal to the inventory, no write after the inventory, no reparse point, no `.git` marker (except the one exact-path marker of the standalone clone in tier 2b), not a registered worktree, and (for tiers A and B) the quiesce check.

| Tier | Content | Folders | Recoverable | Prerequisites | Quiesce the stack? |
|---|---|---|---|---|---|
| **1A** | Root `artifacts/` isolated-build output that holds nothing else, newest write on or before 2026-10-23 | 47 | **89.8 GiB** | none beyond the verification already done | no build or test may run; VS Code may stay open |
| **1B** | In-place `bin`/`obj` and the 64 stray `artifacts/` trees under `src/`, `tests/`, `poc/` of the IAM checkout (no trace-type file in any of them but generated `playwright.ps1` copies) | 197 | **38.1 GiB** | none; the next build restores and compiles from scratch | **yes**: close VS Code (its C# Dev Kit build host writes to `obj`), `dotnet build-server shutdown`, stop the Aspire stack and any `Apperture.*` process |
| **2** | Build folders that also hold a little evidence (TRX, logs, scripts) and build folders whose evidence lives in a sibling `*-logs` | 23 | **61.3 GiB** | evidence children copied to a new restricted archive and independently re-hashed, and every non-build source file of every target proven present in it (`tier2-archive-evidence.ps1`, then `tier2-delete.ps1`) | no |
| **2b** | The DEV-1.0 audit scratch and the standalone restore-check clone, both already held on G: | 2 | 0.2 GiB | `audit-2026-10-29` re-verified identical to the G: zip (466 of 466); the clone re-verified clean and inside the G: bundle (`tier2b-disposable-copies.ps1`; the clone's one `.git` marker is allowed by exact path) | no |
| **3** | The newest IAM validation builds and tree copies (WP4B-B/D reviews, probes, negative-control, master sync): deferred | 8 | 17.9 GiB | WP4B-C closed, then re-classify | no |
| separate | `artifacts/v94` | 1 | 3.6 GiB | see section 4 | no |
| retained | 35 evidence folders, 120 loose files, 5 `TestResults` folders, the two active folders | 37 + | 1.2 GiB, not proposed for deletion | archive copy recommended | n/a |

Tier 1 as a whole is 127.9 GiB (89.8 + 38.1). If the owner decides that folders cited by tracked records must stay (section 5, decision 2), then from tier 1A 33.9 GiB (11 folders), from tier 2 and 2b 31.6 GiB (9 folders), and from tier 1B the six SEC-1 B3 builds the SEC-1 gate cites (`sec1-b3-browser`, `-cookie`, `-int-full`, `-iso`, `-slnx`, `-unit3`) stay.
A solution build with a relative `--artifacts-path` wrote one copy per project, so all 64 stray trees contain at least one of them: the six subfolders total 10.5 GiB across the 64 trees (the trees together are 17.9 GiB), and keeping only those subfolders means tier 1B would have to be run as a per-subfolder list rather than a per-tree list.
The dry runs (every script runs without `-Execute` by default) show: tier 1A passes its preflight on the live disk (47 folders, 89.83 GiB); tier 1B refuses, as designed, while VS Code and its build host run; tier 2 passes its target preflight (23 folders, 61.27 GiB)
and reports that it would block `-Execute` until a verified archive exists; tier 2b passes (2 folders, 0.22 GiB). The archive and verification functions were exercised on synthetic folders, including three negative controls (evidence added after the archive, a changed source, a missing archived file).

Outside the repository, `F:\Code\Apperture` holds 13.4 GiB of sibling folders that are not part of F16's tiers and are proposed for retention until the owner decides:
`_artifacts_task2` and `_task6_build_artifacts` (3.3 GiB each, 2026-09-22, similar but not identical builds, provenance not recorded), `Apperture-Core.zip` (2.9 GiB, 2026-09-14),
`dev11d-review` (3.0 GiB, contains a standalone clone), `archive` (0.3 GiB) and `poc` (0.1 GiB), and the DEV-1.1 review and harness folders (0.6 GiB), which the F22 archive does not cover.

## 4. `artifacts/v94` removal plan (executed 2026-11-03, section 7)

`v94` is a registered worktree of the IAM checkout (`.git/worktrees/v94`, detached at `f319a35`, not locked), clean, with only the ignored folders `out/` and `out2/` (build output,
3.6 GiB). `f319a35` is an ancestor of `master`, `origin/master` and the IAM branch, so no commit exists only there, and the F22 archive recorded its content as build output only.

Steps, run from the new worktree or any worktree, never from inside `v94`:

1. `git -C F:\Code\Apperture\Apperture-Core\artifacts\v94 status --porcelain` is empty; `git worktree list --porcelain` shows no `locked` line for `v94`; no process has its working directory in `v94`; no editor window is open on it.
2. `git worktree remove F:\Code\Apperture\Apperture-Core\artifacts\v94` (no `--force`). If it refuses, stop and report.
3. `git worktree list` no longer shows it; `git worktree prune --dry-run` prints nothing; `F:\Code\Apperture\Apperture-Core\.git\worktrees\v94` is gone.

This changes the shared Git directory of the IAM checkout (it removes `.git/worktrees/v94`) and so needs the owner's authorisation even though it is the ordinary command. The stale
`.git/index.lock` in the IAM checkout is unrelated and untouched.

## 5. Decisions and owner rulings

Rulings (owner, 2026-11-03): tiers 1A, 2b and 2 authorised after their preconditions; tier 1B conditionally (not while development processes write to the targets); `v94` removal authorised; a restricted archive on G: authorised;
historical citations may be kept provided each resolves through the permanent index (option (b) of decision 2, which replaces the plan's earlier acceptance wording); tier 3, the sibling folders outside the repository and the two active writer folders stay excluded.
The decisions as originally put:

1. Which tiers to authorise (1A, 1B, 2, 2b), and the order. Suggested: 1A, then 2b, then 2 (after the archive step), then 1B when the stack is down.
2. **Records that cite deleted folders.** The plan's acceptance says "no record references a deleted folder". 11 of the 47 tier 1A folders, 9 of the 25 tier 2 and 2b folders, and six builds inside every one of the 64 tier 1B stray trees are cited by name in tracked records (AUD-1.2/1.3/1.4, SEC-1 gate, IAM-10.2b WP3/WP4A/WP4B-A, the DEV-1.0 audit). Either (a) keep them (about 76 GiB stays: 33.9 + 31.6 + 10.5; tier 1B then needs a per-subfolder list, which would be generated on request), or (b) accept dangling build paths and add the sentence in rule 4 of section 2 to the records' index without editing the accepted records; for the tier 2 evidence folders the index says where the evidence now lives (the archive path). Recommended: (b); 20 of 42 cited folders were already gone at the audit. Note that (b) departs from the plan's acceptance wording and needs the owner's explicit ruling.
3. Whether to copy the retained evidence (1.2 GiB, plus a snapshot of the two active folders) to the restricted archive too (recommended: it has no off-machine copy), and then keep it on F:. The archive step copies it together with the tier 2 evidence.
4. Whether to remove the standalone restore-check clone and `audit-2026-10-29` (tier 2b; both fully covered by verified G: copies, each on a single drive with its checksum file beside it).
5. `v94`: authorise the worktree removal.
6. The out-of-repository sibling folders listed in section 3.
7. Whether adopting rules 1 to 3 should be followed by an `.editorconfig`/script change (a default isolated-build path under `artifacts/build/`); not part of this package.

## 6. What this package did and did not do

Proposal phase. Did: closed DEV-1.2B (worktree and branch retired, archive supplemented and re-verified), measured everything above read-only, classified every folder, verified the G: copies by
SHA-256, generated the dry-run tier scripts, and had the proposal reviewed independently by a read-only Opus 5.5 review, which rejected the first Tier 2 scripting (the clone's `.git` marker made the tier fail its own preflight, a root-level file crashed the archive manifest, the archive check proved matches but not completeness) and undercounted citations (the reference list had been truncated). All of those, and the quiesce check for SDK builds, were fixed and re-tested. Did not: delete or move any artifact, change the IAM checkout or its Git state, change CI, product code
or the SQL honesty rules, or touch any container or database.

## 7. Execution record (2026-11-03)

Preconditions checked first: branch, worktrees and protected references as inventoried (IAM branch `6e3ab57`, one stash, the archive tag, the IAM checkout's stale `.git/index.lock` untouched); the 11 reviewed scripts and manifests
unchanged by SHA-256; **F: is disk 1 (Samsung SSD 870 QVO, SATA) and G: is disk 2 (WDS100T3XHC, NVMe): physically distinct**; the reference index complete before any cited folder was removed. Every script re-ran its own preflight (exact path, file count equal to the inventory, no later write, no reparse point, no `.git` marker, not a worktree, quiesce) immediately before deleting.

| Step | Result | Free space on F: |
|---|---|---|
| Tier 1A | 47 of 47 folders removed, 0 refused; the folders gone from `artifacts/` are exactly the manifest (118 to 71) | 152.0 to 244.1 GiB (+92.1; manifest 89.83) |
| Tier 2b | audit zip and DEV-1.0 bundle re-verified (checksums; 466 of 466 files identical; clone clean and inside the bundle), 2 of 2 removed | +0.2 (manifest 0.22) |
| Tier 2 archive | `G:\Backup\Apperture-Core-artifacts-evidence\2026-11-03`, restricted ACL: 1,412 files, 1.30 GiB (evidence children of the 23 targets, 35 retained evidence folders, the 120 loose files, the 31 TRX in `TestResults`, a snapshot of `devlogs` and `iam95-visual` taken while both had been idle for months and verified unchanged before and after). `sha256sum -c` 1,412 of 1,412; an independent comparison of every source file against the archive found 1,411 expected, 1,411 present, 0 missing, 0 extra; the build folders that were not copied are listed in `excluded-build-summary.csv`. **Count reconciliation:** 1,411 copied source files (1,192 under `root-artifacts`, 31 under `test-results`, 188 under `active-snapshot`) plus the generated `excluded-build-summary.csv` are the 1,412 checksummed entries; with `SHA256SUMS.txt` and `VERIFIED.txt` (which cannot list themselves) the folder holds 1,414 files | n/a |
| Tier 2 | 23 of 23 removed, 0 refused (71 to 46 folders) | 244.3 to 306.9 GiB (+62.5; manifest 61.27) |
| `v94` | clean, `f319a35` reachable from `master`, no process using it, 10,935 files as inventoried; `git worktree remove` without `--force`; `git worktree list` and `.git/worktrees` no longer show it | +3.6 (manifest 3.6) |
| Tier 1B | **deferred, nothing deleted** | n/a |
| Total | | **152.0 to 310.5 GiB (+158.5); drive F: 66.7 % used (was 83.7 %)** |

The "Free space" column is the **measured change in free space on F:**, not the manifest estimate (the estimates are the sums of file sizes in the pre-execution inventory and are quoted in parentheses where they exist). The measured deltas run about 2 % above the manifests (allocation slack on the removed trees) and the per-step figures are rounded separately, so they add to 158.4 against the measured total of 158.5; no deviation from the manifests occurred. The estimate for the whole of tiers 1A, 2 and 2b was 151.3 GiB (89.83 + 61.27 + 0.22) plus 3.6 for `v94`. Root `artifacts/` went from 173.9 GiB (118 folders) to 18.9 GiB (45 folders, no reparse point, no `.git` marker).

**Tier 1B deferred, two independent reasons found by the preflight.** (1) VS Code, its C# Dev Kit build host and a `dotnet.exe` host are running (19 processes), and the owner's session runs inside that editor; closing them was not done on the owner's behalf. (2) Nine of the 197 targets were written after the inventory (the `bin` of the five test projects and of `Apperture.ModuleSdk.Testing`, `Apperture.Management.Ui.Shared.Tests` and `Apperture.AppHost.Diagnostics.Tests`, and `Apperture.AppHost\obj`, all at 13:03 on 2026-11-03: a build ran); the other 188 pass (26.2 GiB). The earlier authorisation does not carry over: tier 1B needs a **new inventory and a new owner approval** at a time when the development environment is quiescent. To finish it: close VS Code and every build/test/Aspire process, run `dotnet build-server shutdown`, take a fresh in-place inventory, regenerate the tier 1B script from it (the reviewed list is stale for those nine), re-run the dry run, and ask the owner to approve the regenerated list. The `sec1-b3-*` builds the SEC-1 gate cites are in these trees and stay until then.

**Left in place by the rulings:** tier 3 (8 folders, 17.9 GiB, until WP4B-C closes), the two active writers, 35 retained evidence folders and the 120 loose files (copies archived), and the 13.4 GiB of sibling folders outside the repository.

**Still true after the cleanup:** no active instruction (script, workflow, test or README) names a deleted folder (the paths they use are created on demand or still present); the IAM checkout is clean at `6e3ab57` and builds; `dotnet build` of a project in the new worktree succeeds; every G: bundle and archive re-verifies (four bundles and the audit zip against their sums; the DEV-1.2B archive 766 of 766 plus its supplement 3 of 3; the new archive 1,412 of 1,412).

**Evidence of this package:** `wt-dev1-2c\artifacts\dev12c` (ignored by Git; inventory, manifests, generated scripts, dry-run and execution logs, execution records) is archived, verified 258 of 258, at `G:\Backup\Apperture-Core-worktree-evidence\2026-11-03\wt-dev1-2c-dev12c`. Keep that worktree until the owner has decided that the originals are no longer needed.
