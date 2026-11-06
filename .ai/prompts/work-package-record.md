# Work-Package Record Template

Copy this file next to the package's other documents (pattern: `docs/01-architecture/transitions/<area>/<package>-record.md`) and fill it in as the work goes, not at the end. Delete guidance lines (in italics) once filled. Keep it short: link to the plan, the gate or the audit instead of repeating them, and do **not** reproduce the development-loop audit in a record. The procedure it records is in [.ai/playbooks/dev-loop.md](../playbooks/dev-loop.md).

**Rules for the numbers.** Counts, durations and hashes come from generated files (TRX, `provenance.json`, `summary.md`, checksum files), not from typing console output. If a field does not apply, write `n/a` and say why in a few words; never leave it blank. Do not put credentials, connection strings or local secret values in a record. Do not put planning labels in runtime-visible product content; a record is documentation, where they belong.

---

# `<PACKAGE-ID>`: `<short title>`

| | |
|---|---|
| Status | `planned` / `implementing` / `validated` / `in review` / `accepted` / `merged and closed` |
| Owner / implementer | |
| Scope source | *link to the plan, gate section or finding register entry* |

## 1. Scope and acceptance criteria

- **In scope:**
- **Out of scope / not changed** (call out contracts you deliberately did not touch: required CI, SQL honesty rules, review procedure, licensing, product behaviour):
- **Acceptance criteria** (each one testable; say which test or control shows it):
  1.
  2.

## 2. Identity of what was validated

| | |
|---|---|
| Base commit (`git rev-parse origin/master` when the branch was cut) | |
| Branch / worktree | |
| **Reviewed HEAD** (exact commit the validation and any review below refer to) | |
| Tree hash of the reviewed HEAD (`git rev-parse HEAD^{tree}`) | |
| Working-tree state when validated | clean / dirty (if dirty: diff hash and why) |
| Commits since base (`git log --oneline origin/master..HEAD`) | *list or count* |

## 3. Changes

- **Changed files** (`git diff --stat <base>..<HEAD>`; group by purpose):
- **Implementation summary** (what changed and why, in a few sentences; the decisions that were not obvious):
- **Contracts and protections touched** (security properties, required checks, honesty rules, allow-lists, pins; "none" is a valid and useful answer):

## 4. Validation

**Environment.** SDK version, OS, PowerShell version, container / SQL target description (no credentials), whether Docker was healthy, whether the shell was clean (no `MSBUILD*`/`DOTNET_*`/`NUGET_*` customization), build command and flags, `--artifacts-path`.

**Commands run** (exact, copy-pasteable, no secrets) and what each is for:

| # | Command | Purpose | Exit |
|---|---|---|---|
| 1 | | | |

**Test counts** (one row per test project or run; from TRX):

| Run | Discovered | Executed | Passed | Failed | Skipped / not executed | Evidence file |
|---|---|---|---|---|---|---|
| | | | | | | |

*Skipped and not-executed tests are listed by name here or in the evidence, with the reason. A skip is never counted as a pass. Compare against the last accepted baseline where one exists (what was added, removed, changed outcome).*

**SQL execution and isolation modes** *(required wherever SQL tests are in scope)*:

- SQL target: `<disposable test container description>`; port 14333 not used: yes / no.
- SQL execution honesty: `scripts/dev/Test-SqlExecutionHonesty.ps1` result (exit code, report path), run at the reviewed HEAD: 
- Isolation modes run: read-committed-snapshot **False**: `<n>` passed / `<n>` failed; **True**: `<n>` passed / `<n>` failed; none-parameterised: `<n>`.
- SQL execution proof (write markers moved, catalog start-ups or writes in the run window): 
- Any soft-skip suspects (SQL cases under 100 ms) and how they were resolved: 

**Hosted CI at the PR head** *(fill when the PR exists)*: run id, `headSha`, `build-and-test` and `browser-security` conclusions.

## 5. Mutation and regression-catch evidence

*One row per protected property you claim. A mutation that was not caught is a failed result and is recorded as such. `Invalid` (did not compile, no test ran, infrastructure failure) is neither caught nor escaped.*

| ID | Protected property | Mutation (file, old to new) | Declared | Outcome | Failing tests (unit / SQL) | Evidence |
|---|---|---|---|---|---|---|
| | | | caught | | | |

- Baseline for the mutation set was green on the unedited tree: yes / no. Source restored and verified against the committed blob; final rebuild reproduced the baseline: yes / no.
- For each fix to a test or control, the break that makes it fail on the old behaviour: 

## 6. Evidence and checksums

| Item | Path (archive location once archived) | SHA-256 or manifest |
|---|---|---|
| Evidence folder (`provenance.json`, `summary.md`, TRX, logs) | | |
| Checksum manifest | | |
| Archive verification (`ARCHIVE-VERIFIED.txt` or equivalent) | | |

*Evidence is never inside a build folder. The archive is a copy on another local drive, not an off-machine backup; say so if off-machine safety matters for this evidence.*

## 7. Independent review

| Round | Reviewer | Exact commit reviewed | Verdict | Scope the reviewer accepted | Findings (ID, severity, one line) |
|---|---|---|---|---|---|
| 1 | | | | | |

**Remediation history.** One row per finding: ID, fix commit, the control that fails on the old behaviour, whether the reviewer re-checked it at which commit (`closed by reviewer` / `fixed, not re-checked`).

**Review obligations that apply to this package** (from its gate or plan) and how each was met:

## 8. Remaining risks and owner decisions

- **Residual risks** (each with severity and who owns it):
- **Owner decisions needed** (what is being asked, the recommendation, the consequence of each answer):
- **Known limits of the evidence** (what this record does not show):

## 9. Pull request and closure

| | |
|---|---|
| PR (number, head commit, base) | |
| Required checks at the PR head | `build-and-test`: `browser-security`: |
| Merge commit on `master` | |
| Post-merge CI on the merge commit (run id, conclusion) | |
| Branch tip is an ancestor of `origin/master` | yes / no |
| Worktree clean; ignored files compared with the archive; nothing unique left | yes / no |
| Worktree and branch retired (ordinary `git worktree remove`, `git branch -d`) | yes / no, date |
| Evidence archived (location, file count, verified) | |
| Post-PR supplement (final review logs, PR and CI records) archived | yes / no |
