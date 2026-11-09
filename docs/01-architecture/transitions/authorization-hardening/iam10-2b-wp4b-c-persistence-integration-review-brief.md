# Independent review request: IAM-10.2b WP4B-C persistence integration (Foundation 1D into the accepted foundation)

Filled from [.ai/prompts/independent-review-request.md](../../../../.ai/prompts/independent-review-request.md) by the implementer (sections 1 to 3 and 5). Section 4 of that template, the reviewer's reproduction checklist, is the reviewer's and is **not reduced** here; the lighter audit E.7 procedure and build Configuration C are not adopted. This brief states facts and claims, not a conclusion. The verdict, and the acceptance of Foundation 1D, are the reviewer's. The implementer's pre-review (seven read-only lenses and a skeptic pass) is described in the [record](iam10-2b-wp4b-c-persistence-integration-record.md) sections 9 and 10; it is input, not a substitute: re-derive anything you rely on.

**Package:** IAM-10.2b WP4B-C persistence integration. **Record:** [iam10-2b-wp4b-c-persistence-integration-record.md](iam10-2b-wp4b-c-persistence-integration-record.md), with the [1D record](iam10-2b-wp4b-c-foundation-1d-record.md), the [foundation integration record](iam10-2b-wp4b-c-foundation-integration-record.md) and the [decision record](iam10-2b-wp4b-decision-record.md) (section 15).

## 1. Exact scope under review

| | |
|---|---|
| Repository | `Intechral-Solutions/Apperture-Core`, **local only** (nothing pushed, no PR) |
| Branch / worktree | `integration/iam10-2b-wp4bc-persistence`, `F:\Code\Apperture\wt-iam10-2b-persistence-int` |
| **Exact commit** | The head of that branch, whose full SHA the owner's hand-over message gives. Its `src` tree must equal `47e2a54333f4388254f169d13bc0ef7e9fa48b2f`; if it differs, stop and say so. Validated code commit: `d549c550d221c27c80c6553fa7c6dab31e8ca080` (tests `b0f00fe088cd9b5c74add16fcef9e25cc9f5cdeb`); later commits are tests and documentation only |
| Parents of the merge | `cfe3e7020bd194a5e2def726901dc8d1e24a0cb0` (accepted foundation, `src` `e961044d235e4145685f61cebce154ee1beed82f`) and `859de93f21c568694e988aca20c861b3b99bdf5c` (Foundation 1D, validated code `7a74fe0755dbfbc19bc33965614819218c108fc9`, `src` `6aac67e93d5bf0a31dd3ff0023df8e3ecb6c2015`); merge `85d4ff4cddff0798f88c7ac2710231bc20fc92dc`; base `c3e9813a09748234228e3227b22cb906e8aedfad` |
| Diff to read | `git diff cfe3e702..<head>` for what 1D adds to the accepted foundation (29 source/test/doc files at the merge plus this package's tests and records) and `git diff 859de93f..<head>` for what the foundation adds to 1D |
| Not under review | The foundation 1A to 1C behaviour (accepted at `cfe3e702`; do not reopen without concrete contradictory evidence), the service and the other reducers, the command line, WP5, any audit emission (none exists), retry infrastructure, the DEV-1 toolkit |

## 2. Protected properties and the controls that should fail if they break

| # | Property | Where enforced | Control that should fail if it breaks | Evidence |
|---|---|---|---|---|
| Q1 | The merge is exact: no change beyond the two sources, one seed correction and tests/docs | `git` | your own `merge-tree`, `diff`, blob comparison; the five key files are blob-identical to 1D | record section 1 |
| Q2 | One live candidate per user; ownership, version and database-clock expiry are conditions of the same statement that writes | `SqlNativeMfaReplacementCandidateRepository`, `UX_UserTotpReplacementCandidates_Live` | candidate and concurrency classes; mutants M03 to M06, M10, M13 (1D), X05, X06, X07, X12 | 1D record section 5; record section 8 |
| Q3 | The switch replaces the secret, identity, step and recovery codes atomically, in the caller's transaction, or changes nothing; the active authenticator and codes survive every refusal and every rollback | `SwitchAsync`, `InTransactionAsync` | the every-refusal theory; the rollback case with the real coordinator scope; X08, X11 | record sections 4 and 8 |
| Q4 | Conditional enrollment cannot activate an outdated pending credential; `MarkEnrolledAsync` requires the expected identity | `MarkEnrolledAsync`; `NativeMfaService.CompleteEnrollmentAsync` | `ConditionalMfaEnrollmentSqlServerTests` (with a legacy-unconditional control); X09, X10 | record sections 2 and 8 |
| Q5 | Each of the three writers that remove a staged candidate does so on its own (credential delete, code removal, code replacement) | `DeleteAsync`, `DeleteAllAsync`, `ReplaceAllAsync` | the six contract cases; X01 to X04 | record section 8 |
| Q6 | Lock order credential row, candidate rows, code rows; a reset racing a switch is a `DbUpdateConcurrencyException` that changes nothing | repository code; parked concurrency cases | concurrency class; **F-1 and F-2 are open code-reading findings**: confirm or refute them by a run | record sections 4 and 9 |
| Q7 | The migration upgrades an existing installation (identities, no value changed), enforces the filtered index, rolls back without losing a credential or code, and re-applies | `Iam102b_MfaReplacementCandidates`, snapshot | migration class and the round-trip case; the model-consistency unit test | record section 5 |
| Q8 | Secrets: candidate and credential secrets are only ever protected bytes; consumed and superseded rows hold zero bytes; no record, message, log or evidence carries secret material | repository, tests, evidence | review of the new code and the archived evidence (the evidence was scanned for connection-string and secret patterns) | record sections 4 and 11 |
| Q9 | The two new audit actions are catalogued with typed producers and schemas, count pinned, and no production code emits them | catalog, `AuditEvents` | `MfaReplacementAuditContractTests`, `AuditActionCatalogTests` | record section 6 |
| Q10 | The merged halves interoperate: the viability facts do not see a candidate; the real switch inside a real coordinator scope is a posture-only allowed replacement; a reset is refused as a replacement; rollback restores everything | `SqlAdministratorFactsReader`, coordinator, repository | `MfaReplacementViabilityIntegrationSqlServerTests` (both modes) | record sections 4 and 8 |

**What must not have changed:** every file under `src` other than Foundation 1D's 15 and nothing from the foundation's 15 (`git diff --name-only` must show it), the SQL honesty allow-list, must-fail list and script and their pin, `.github` and the required CI checks, `tests/test-manifest.json`, the validation protocol, the protected IAM branch. This package changed no `src` file.

## 3. Evidence to inspect

* Evidence folder on the second drive and its `SHA256SUMS.txt`: the owner's hand-over message names the path. **Verify the checksums first** (the manifest is CRLF with backslash paths: verify with PowerShell `Get-FileHash`, not `sha256sum -c`); a mismatch is a finding. A second folder holds Foundation 1D's original ignored evidence (114 files), copied before any integration work.
* Working copy: `F:\Code\Apperture\wt-iam10-2b-persistence-int\artifacts\int-logs` (`trx-focused`, `trx-new`, `trx-edited`, `mutation` with `mutants.json` holding the exact edit of every mutant, `honesty`).
* Both isolation modes: every real-SQL case runs with `rcsi: False` and `rcsi: True`; the new cases read state back with plain SQL on a separate connection.
* Hosted CI: none.

## 4. What the reviewer reproduces independently

As in the template, unreduced: your own clean build of the exact commit with `--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers` and a fresh artifacts path inside the repository; the non-SQL suites; the focused SQL set on both modes; the honesty script (omit `-NoBuild` for authoritative evidence); the new and 1D classes; classes the changed files touch that the implementer's filter may have omitted (the implementer's set was 1D's filter, which names the foundation's coordinator, attestation, policy and registration classes; the full SQL assembly was not run). Apply the toolkit path caveat: the DEV-1 validation toolkit is not authoritative for source-reading tests.

## 5. Specific questions (bounded)

1. **Ancestry.** Is the merge exact as claimed, with nothing but the two sources, the one seed correction and tests/docs?
2. **Atomicity.** Can any failure path (a refusal, a constraint error mid-way through the recovery-code inserts, a lost connection, a rollback after the switch) leave the credential switched and the codes not, or the candidate consumed and the credential unchanged? In joined mode the switch has no savepoint: is the audited unit the only caller that must roll back, and does it always?
3. **Lock order and deadlocks.** Reproduce or refute F-1 (Stage versus `ReplaceAllAsync`/`DeleteAllAsync`/`DiscardAsync`) with a parked test. Is there a cycle with the administrator-set gate or the audit writes?
4. **Replay protection.** Reproduce or refute F-2 (a stale last-used write after a switch). What exactly does persistence guarantee about the last-used step, and what is left to the service?
5. **Conditional enrollment.** Is there an interleaving in which `MarkEnrolledAsync` succeeds on a secret nobody verified? Are all callers of the old unconditional write gone?
6. **Version and ownership.** Can a stale or foreign version, owner or candidate id ever apply under a coinciding credential identity or a re-stage?
7. **Recovery-code preservation.** Is there a way to lose the active recovery codes without a successful switch, reset or regeneration?
8. **Migration.** Is the filtered index predicate valid under the connection options the application uses? Any data path in which a rollback loses a credential, a code or an enrolment flag? Is a concurrent insert during the upgrade a risk?
9. **Secrets.** Any place where candidate or credential secret material, a code or a hash can reach a log, an audit row, an exception message, a test failure message or the evidence?
10. **Audit contract.** Do the two catalog entries cover what the service must record (including evidence for a replacement and for each refusal), and can the service fill them from what the coordinator returns?
11. **The I-R3-1 specification** in record section 10 (U4): is "accept a replacement only on basis `PostureOnly`" necessary and sufficient, and is anything missing that the service would need beyond it?
12. **Do the controls catch failures?** Are the 12 negative controls (X01 to X12) and the new cases real controls or tautologies? What would a mutant of your own show?
13. **New gaps from the merge.** Anything the merge made worse that neither source showed alone.

## 6. Required output

As in the template: a verdict at the exact commit (`ACCEPT`, `ACCEPT WITH BOUNDED OBLIGATIONS`, `REJECT`); the exact scope of acceptance (state separately whether Foundation 1D is accepted, and whether the accepted foundation is still intact); findings with severity, a concrete failure scenario, your own reproduction and the required remediation; what was run; residual risks and owner decisions. Distinguish *persistence-level* assurance (the candidate lifecycle, the switch, the migration, enrollment, the catalog, as implemented and tested here) from *service-level* assurance (whether the administrator-viability and replacement contracts hold end to end once the service calls these foundations), which is not provided by this package.

## 7. Handling the result

Findings are fixed with a control that fails on the old behaviour, in a separate commit, and sent for closure verification at the new exact commit ([review-closure-verification.md](../../../../.ai/prompts/review-closure-verification.md)). The implementer does not declare the integration or Foundation 1D accepted.
