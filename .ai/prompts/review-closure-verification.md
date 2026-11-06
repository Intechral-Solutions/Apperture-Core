# Review Closure Verification Template

Use this after an independent review returned findings and the implementer has remediated them. It asks the **same independent reviewer** (or another independent one) to confirm, from the code, that the named findings are closed at an exact commit and that nothing else changed in a way that matters. It does not shrink the review obligations in [independent-review-request.md](independent-review-request.md) and does not adopt a different review procedure.

The implementer's statement that a finding is fixed is not closure. A finding is **closed** only when the reviewer's own repro, run on the reviewer's own clean build of the exact commit, shows it closed.

---

## Request

**Package:** `<ID and title>`  **Record:** `<path>`  **Original review:** `<round, verdict, commit it reviewed>`

### 1. Exact commits

| | |
|---|---|
| Commit last reviewed (the verdict being closed) | |
| **Exact commit to verify now** (full SHA) | |
| Diff since the last reviewed commit | `git diff <reviewed>..<now>` (`<n>` files) |
| Commits in between, one line each | |

The reviewer checks `HEAD` equals the SHA above and the tree is clean, and says so if not.

### 2. Findings to close

*One row per finding from the review, in its original wording and ID.*

| ID | Severity | Finding (as the reviewer reported it) | Remediation (commit and one line) | Control that fails on the old behaviour | Evidence path |
|---|---|---|---|---|---|
| | | | | | |

### 3. Changes that are not remediation

*Every change since the reviewed commit that is not on a finding above (record edits, refactors, extra hardening). If there are none, write "none". Do not leave unlisted changes: the reviewer diffs the range and reports anything not named here.*

### 4. What the reviewer does

For each finding:

- [ ] Reproduce the original failure on the reviewed commit (own scratch clone, own build), or confirm the original repro from the review.
- [ ] Show the same repro now fails to reproduce at the exact commit: finding **closed**.
- [ ] Confirm the new control **fails on the old behaviour** (revert only the fix in a scratch clone, or run the pre-fix reproduction) and passes now. A control that cannot fail does not close anything.
- [ ] Look for the same defect in a neighbouring path (one named step, not an open-ended audit).

For the range as a whole:

- [ ] Read the diff `<reviewed>..<now>`; report every behavioural change not listed in section 3.
- [ ] Re-run the test classes that changed or that exercise changed production files, on the required SQL modes where SQL is in scope, and the SQL honesty check if SQL test code or fixtures changed.
- [ ] Confirm nothing outside the stated scope changed: required CI, honesty rules and allow-lists, licensing, product behaviour, security posture.
- [ ] Confirm the evidence manifest for the accepted material is untouched or re-issued with new checksums, and say which.

### 5. Required output

1. **For each finding:** `CLOSED` (with the reviewer's own repro), `NOT CLOSED` (with the repro that still fails), or `CLOSED, NEW FINDING NEAR IT`.
2. **New findings**, each with ID, severity, failure scenario, repro and required remediation (same format as the original review).
3. **Verdict at the exact commit:** `ACCEPT`, `ACCEPT WITH BOUNDED OBLIGATIONS`, or `REJECT`.
4. **Exact scope of acceptance:** the commit, the findings it closes, what was re-run, and what was **not** re-checked. Any fix made after this verdict is not covered by it.
5. **What the reviewer ran**, and any check they could not make.

### 6. After the verdict

Record the verdict, the commit it applies to and each finding's closure in the work-package record's review section. If any change lands after the verdict, state in the record that it was not independently re-checked, with its commit. The package may be accepted only at a commit the verdict covers, or with the later change disclosed as unreviewed and accepted by the owner.
