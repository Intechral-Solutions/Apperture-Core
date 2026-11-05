# Independent Review Request Template

Use this when requesting an independent review of a work package. The reviewer must be a different agent or person from the implementer and must work from the **code at an exact commit**. This template preserves the review obligations that exist today ([.ai/playbooks/dev-loop.md](../playbooks/dev-loop.md), section 8). It does not adopt any change to the review procedure: a lighter or different procedure needs the owner's and the security owner's explicit authorization.

**Do not hand the reviewer your conclusion.** Give the commit, the protected properties, the evidence locations and the claims; let the reviewer reach their own verdict. A summary written by the implementer, and the implementer's own test results, are inputs to be checked, never a substitute for the reviewer's own validation. Do not tell the reviewer what you expect them to find.

---

## Request

**Package:** `<ID and title>`  **Record:** `<path to the work-package record>`

### 1. Exact scope under review

| | |
|---|---|
| Repository and remote | |
| **Exact commit to review** (full SHA; the reviewer checks `HEAD` equals it and that the tree is clean) | |
| Base commit and branch | |
| Diff to read | `git diff <base>..<commit>` (`<n>` files) |
| What is **not** under review (and why) | |

If the reviewer finds the commit differs from this SHA, they stop and say so; they do not review whatever is checked out.

### 2. Protected properties and intended regression controls

*One row per property the package claims to protect. The reviewer decides whether the control is adequate; do not argue it here.*

| # | Property (what must stay true) | Where it is enforced | Control that should fail if it breaks (test / mutation) | Where the implementer's evidence is |
|---|---|---|---|---|
| P1 | | | | |

State also what must **not** have changed: contracts, required CI checks, SQL honesty rules and allow-lists, licensing and CLA text, product behaviour, security posture. The reviewer verifies the diff touches none of them unless the package says so.

### 3. Evidence to inspect

- Evidence folder(s) and checksum manifest: `<paths>` (verify the checksums first; a mismatch is a finding)
- `provenance.json` / `summary.md`, TRX files, logs: `<paths>`
- SQL execution and isolation-mode evidence: `<paths>`
- Mutation results: `<paths>`
- Hosted CI run(s): `<ids, head SHA>`

Anything the reviewer needs that is not in this list, they ask for; absence of evidence is reported, not assumed.

### 4. What the reviewer reproduces independently

At minimum, on the reviewer's **own clean build** of the exact commit (own artifacts path, clean shell, required SQL execution where SQL is in scope):

- [ ] Build the commit; 0 errors.
- [ ] **The focused test set and the relevant non-SQL suites for this package, re-run in full on the reviewer's own build** (both isolation modes where SQL is in scope). This is an existing obligation (audit E.1 and the DEV-1.3A record's review rounds). It is not reduced to "verify the hashes and re-run only what changed"; only the owner and the security owner can authorize that, in writing.
- [ ] Every new or changed test class, on **both** isolation modes where SQL is in scope.
- [ ] Classes that exercise the changed production files but that the implementer's filter may have omitted (the reviewer derives these from the diff; do not limit them to the implementer's list).
- [ ] The mutation or negative control for each property in section 2, and at least one **novel** control the implementer did not run.
- [ ] `scripts/dev/Test-SqlExecutionHonesty.ps1` at the commit, where SQL test code, helpers or fixtures changed.
- [ ] Anything the evidence shows as skipped, not executed or undiscovered, judged for materiality.

The checklist above is the minimum and is **not** to be deleted or edited down when this template is filled in: the implementer fills in sections 1 to 3, the reviewer owns section 4. The lighter procedure of audit E.7 is **not** adopted; do not apply it unless the owner and security owner have authorized it in writing.

### 5. Specific questions (optional, bounded)

*Name the exact things you want attacked: a specific property, a boundary you doubt, a place a laundering or false-pass route might exist. Keep the review bounded; a request for a full re-audit of a tool or area belongs in its own package.*

### 6. Required output

1. **Verdict**, one of: `ACCEPT`, `ACCEPT WITH BOUNDED OBLIGATIONS` (name each obligation), `REJECT` (name what blocks acceptance), at the exact commit.
2. **Exact scope of acceptance:** what was reviewed and reproduced, and what was **not** (so nobody reads the verdict as wider than it is).
3. **Findings**, each with: ID, **severity** (`High` / `Medium` / `Low` / `Informational`), where, a **failure scenario** (concrete inputs or state leading to a wrong result), the reviewer's own repro, and the **required remediation**.
4. **What the reviewer ran:** commands, environment, results, and any check they could not make.
5. **Residual risks and anything that needs an owner decision.**

Severity guide: **High**: a wrong result that matters can be reported as correct or authoritative, a protection can be bypassed, or evidence can be laundered. **Medium**: a real gap with a bounded effect, or a weakness a determined path could exploit. **Low**: a defect with no path to a false pass, or hardening. **Informational**: a note, not a defect.

### 7. Handling the result

The implementer fixes findings with a control that fails on the old behaviour, commits the fix separately, and sends the reviewer a **closure verification request** ([review-closure-verification.md](review-closure-verification.md)) naming the new exact commit and the finding IDs. The package is not accepted on a verdict that predates unreviewed changes; any change after the last verdict is recorded as not re-checked.
