# IAM-10.2b WP4B-C Foundation 1B + 1C: coordinator and attempt-scoped evidence

| | |
|---|---|
| Status | `validated`; not reviewed; not pushed. **Since combined with Foundation 1A** on `integration/iam10-2b-wp4bc-foundation` ([integration record](iam10-2b-wp4b-c-foundation-integration-record.md)); the combination is locally verified and pending independent acceptance. The owner ruled on review point 1 (§7, §8) on 2026-11-07: `attested` is kept and the decision record §4 now says so. |
| Owner / implementer | Tennyson-Hull / Claude Opus 5.5 |
| Scope source | Owner brief "IAM-10.2b WP4B-C Foundation 1B + 1C"; [decision record](iam10-2b-wp4b-decision-record.md) §3.4, §3.5, §4, §5.2, §5.4, §8, §10 item 6; [DEV-1 integration record](iam10-2b-dev1-integration-record.md) §5.2, §11 |

## 1. Scope and acceptance criteria

- **In scope:** one attestation reading per protected attempt (I-3); the posture-only and evidence-dependent decisions; the mutation classifications and refusals; the scope's verify operations; their tests.
- **Out of scope / not changed:** the attestation publisher and SQL store (Foundation 1A, L-B1, L-B2); `EmergencyAccessService` reducers; `NativeMfaService` enrolment and replacement flows; MFA candidate persistence; emergency CLI behaviour; the WP5 pairwise matrix; required CI; SQL honesty rules and allow-lists; development-environment and container work (deferred by the owner).
- **Acceptance criteria:**
  1. An attempt reads the deployment configuration once, after the gate, and every state of that attempt uses that reading. Each attempt, including a rerun of an audited unit, reads again. Shown by `AdministratorViabilityAttemptReadingTests`, the SQL class, R01 to R06.
  2. Holding a reading cannot extend the authority of the evidence past its window, and the two states of an attempt cannot reach contradictory conclusions about it. Shown by the freshness cases, the SQL freshness edge, F01 to F05.
  3. A bare reset depends on the posture and the Local reading only, never on Microsoft sign-in. Shown by `AdministratorViabilityPostureDecisionTests`, P01 to P15.
  4. A replacement is judged on its net effect on the posture and reads no configuration. Shown by S01 to S04.
  5. Each decision states the evidence class it used and the mutation it judged, and has a typed refusal. Shown by E01 to E05, C01 to C09.
  6. Both invariants hold on real SQL in both isolation modes, including races. Shown by `AdministratorViabilityAttemptSqlServerTests` (20 cases, 40 runs).

## 2. Identity of what was validated

| | |
|---|---|
| Base commit | `c3e9813a09748234228e3227b22cb906e8aedfad` (accepted IAM baseline; `feat/iam-10.2b-administrator-viability`, unchanged) |
| Branch / worktree | `feat/iam10-2b-wp4bc-coordinator` / `F:\Code\Apperture\wt-iam10-2b-coordinator`; local only |
| **Validated commit** | `58bb7b1aa3ebb7b62892a6031e885d5a4e2a6e5b` |
| Tree of the validated commit | `7eb35b6549ac553ac9c2d070823fe3d87448ea62` (`src` `45db56a77d8a05cb5645e0c97d85930aedce3ca4`, `tests` `f027668f81f367ba13af1ac6f819e83374bbe1ab`) |
| Working-tree state when validated | clean |
| Commits since base | `345a2c6` implementation, `73140f2` test re-base, `58bb7b1` stronger decision tests, then this record (docs only) |

## 3. Changes

- **Changed files** (under `Apperture.ControlPlane.Security/AdministratorViability/` unless noted):
  - New: `AttemptConfigurationReading.cs`, `ViabilityDecision.cs`.
  - Changed: `AdministratorViabilityCoordinator.cs`, `AdministratorViabilityGuard.cs`, `AdministratorViabilityModel.cs`, `AdministratorViabilityService.cs`, `EffectiveSignInConfigurationProvider.cs`, `DeploymentAuthenticationConfiguration.cs`, `HostConfigurationAttestationClassifier.cs` (one expression), `ViabilityRefusal.cs`, `../GovernanceRefusals.cs`.
  - Tests new: `AdministratorViabilityAttemptReadingTests.cs`, `AdministratorViabilityPostureDecisionTests.cs`, `AdministratorViabilityAttemptSqlServerTests.cs`.
  - Tests changed: `FakeViability.cs`, `EntraPrimaryAuthenticationPolicyTests.cs`, `AdministratorViabilityWriterCoverageGuardTests.cs`, and three M-2 cases in `AdministratorViabilityWp2SqlServerTests.Remediation.cs` and `.Provisioning.cs`.
- **The reading.** A scope owns one `AttemptConfigurationReading`. The read is lazy: it happens at the first capture, after the gate is held, so a gate wait cannot age it and entering a scope reads nothing. Only the deployment reading is pinned. The stored facts, the registration and the secret overlay are read again for each state, because they are what the comparison is about. A failed read is not retried inside the attempt.
- **Freshness.** The classifier states the time the evidence has left (`window - (database now - refreshed)`). The attempt measures elapsed time with a monotonic clock started before the store call, so it can only understate what is left. When a decision finds the time has run out, trust is removed from **both** states, so a decision that needs the evidence refuses as unavailable and a decision that does not need it proceeds. The check is at decision time. There is no second read and no re-check between the decision and the commit.
- **Decisions.** `VerifyDesignationAsync`, `VerifyRevocationAsync`, `VerifyBareResetAsync`, `VerifyReplacementAsync` return a `ViabilityDecision` (mutation, verdict, evidence, basis). The posture predicates apply only when both states carry their facts, each state's health flag agrees with those facts, no administrator's facts moved, and the posture differs at most in the authenticator. Anything else goes to the full evaluation, which fails closed. A recovery code consumed at a sign-in during a change to another account is tolerated, because that path takes no gate and the writer cannot have caused it.
- **Contracts and protections touched:**
  - `IAdministratorViabilityService` and `IEffectiveSignInConfigurationProvider` gained abstract `ReadForAttemptAsync`.
  - `DeploymentAuthenticationReading` gained optional `EvidenceRemaining`.
  - New verdict kind `RecoveryPostureReduced` and new conflict code `recovery_access_posture_reduced` (additive).
  - No required check, honesty list or pin changed. `allowListSha256` in the honesty report is `8798D5A9...442E` with 227 entries.

### Outcomes by mutation

| Mutation | Evidence needed | Outcome |
|---|---|---|
| Designation | Local and Microsoft, per view, unless inputs did not change or the result is proven safe on Local alone | Existing `Evaluate` verdict, plus its evidence class |
| Strict revocation | Local, per view; never Microsoft | Existing strict rule, plus its evidence class |
| Bare reset: not the recovery account, posture already unhealthy, or still healthy | None | Allowed, `not_required` |
| Bare reset: takes a healthy posture away | Local, per view; never Microsoft | Allowed on an Entra-independent administrator, otherwise `entra_independent_fallback_required`; `administrator_viability_unavailable` without the Local reading |
| Replacement: not the recovery account, healthy afterwards, or unhealthy before and lost nothing | None | Allowed, `not_required` |
| Replacement: otherwise | None | Refused, `recovery_access_posture_reduced`, `not_required` |

## 4. Validation

**Environment.** SDK 10.0.402, Windows 11, PowerShell 7.6.9. SQL target: the disposable container `apperture-test-sql` at `127.0.0.1,1433`, used as it stands under the owner's deferral. Shell cleared of `MSBUILD*`, `DOTNET_*`, `NUGET_*` (cleared: `DOTNET_MULTILEVEL_LOOKUP`). No toolkit `AUTHORITATIVE` label is relied on.

**Commands run:**

| # | Command | Purpose | Exit |
|---|---|---|---|
| 1 | `dotnet build Apperture.slnx -c Debug --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers --artifacts-path artifacts/v3` | Protocol build: 0 errors, 73 warnings, 339 s | 0 |
| 2 | `dotnet test tests/<project>/<project>.csproj -c Debug --no-build --artifacts-path artifacts/v3 --logger trx` for the five non-SQL suites | Non-SQL suites | 0 |
| 3 | `dotnet test tests/Apperture.IntegrationTests/... -c Debug --no-build --artifacts-path artifacts/v3 --logger trx --filter <focused-filter.txt> -- RunConfiguration.TreatNoTestsAsError=true` | Focused SQL set: the 44-clause filter in the evidence, the established WP4B-B set de-duplicated plus the new class | 0 |
| 4 | `pwsh -NoProfile -File scripts/dev/validation/Get-SqlEvidence.ps1 -Mode Markers -ProcessEnvironmentOnly -Json <file>`, before and after | SQL write markers | 0 |
| 5 | `pwsh -NoProfile -File scripts/dev/Test-SqlExecutionHonesty.ps1 -Configuration Release` | SQL execution honesty, connection string removed | 0 |

**Test counts** (from TRX):

| Run | Discovered | Executed | Passed | Failed | Not executed | Evidence file |
|---|---|---|---|---|---|---|
| Apperture.UnitTests | 6,117 | 6,116 | 6,116 | 0 | 1 | `trx/Apperture.UnitTests.trx` |
| Apperture.ArchTests | 38 | 38 | 38 | 0 | 0 | `trx/Apperture.ArchTests.trx` |
| Management.Ui.Shared.Tests | 1,497 | 1,497 | 1,497 | 0 | 0 | `trx/Apperture.Management.Ui.Shared.Tests.trx` |
| Management.Ui.Client.Tests | 532 | 532 | 532 | 0 | 0 | `trx/Apperture.Management.Ui.Client.Tests.trx` |
| AppHost.Diagnostics.Tests | 7 | 7 | 7 | 0 | 0 | `trx/Apperture.AppHost.Diagnostics.Tests.trx` |
| Focused SQL (43 m 15 s) | 1,437 | 1,437 | 1,437 | 0 | 0 | `trx/focused-sql.trx` |

Not executed: `PasswordEvaluationDesktopTimingDiagnostics.Records_desktop_zxcvbn_timings_for_representative_passwords`, an opt-in diagnostic.

**Not run:** BrowserTests, the full IntegrationTests project outside the focused filter, and Linux. No hosted CI, because nothing was pushed.

**SQL execution and isolation modes:**

- SQL target: disposable container as above; port 14333 not used: yes.
- SQL execution honesty at `58bb7b1`: exit 0, verdict PASS; discovered 3,519, executed 3,519, passed 226, failed 3,293 (the expected failures of a run with no SQL), unexpected passes 0, violations 0. Report `honesty-58bb7b1/sql-honesty-report.json`.
- Isolation modes: read-committed-snapshot **False**: 594 passed / 0 failed; **True**: 594 passed / 0 failed; not parameterised: 249 passed / 0 failed. New class: 20 per mode, 40 of 40.
- SQL execution proof: `TotalUserUpdates` went from 28,083 to 32,572 across 20 catalogs during the run.
- Soft-skip suspects: 146 cases passed in under 100 ms, none in the new class. None is on the approved non-SQL list, and the honesty run reports no unexpected pass, so each of them fails without SQL.

**A failure on the way.** The first complete run, at `345a2c6`, had 4 failures: `M2_a_provisioned_administrator_succeeds_when_the_authentication_environment_changes_between_the_reads`, asserting two configuration reads where the behaviour now gives one. Fixed in `73140f2`. That evidence is archived as superseded.

### What the SQL class proves and does not prove

It runs the real gate, facts reader, provider, attested reader and store, with a counting decorator on the store and a real `SqlAuditedUnitOfWork` rerun. The reducers do not exist yet, so the writers in it are **SQL proxies** in real transactions, not `EmergencyAccessService` or `NativeMfaService`. It shows that the coordinator behaves correctly under such writers. It does not show that the services will call it correctly.

## 5. Mutation and regression-catch evidence

44 mutants, one at a time, in the disposable clone `F:\Code\Apperture\mut-iam-coord`. Each ran the unit classes (655 cases) and the SQL class (40). All were declared `caught`.

| ID | Mutation | Outcome | Failing unit / SQL |
|---|---|---|---|
| R01 | Second read: state after the change | Caught | 10 / 14 |
| R02 | Second read: state before the change | Caught | 12 / 14 |
| R03 | Reading not pinned | Caught | 13 / 14 |
| R04 | Scope, so reading, reused across attempts | Caught | 2 / 6 |
| R05 | Evidence read before the gate | Caught | 10 / 25 |
| R06 | Failed read retried inside the attempt | Caught | 1 / 0 |
| F01 | Holding the reading never runs out | Caught | 2 / 2 |
| F02 | Time left ignores the attestation's age | Caught | 3 / 2 |
| F03 | Only the state after loses expired trust | Caught | 1 / 2 |
| F04 | Exact edge counts as run out | Caught | 1 / 0 |
| F05 | Attested reading with no limit never runs out | Caught | 1 / 0 |
| P01 | Bare reset demands the Microsoft reading | Caught | 14 / 12 |
| P02 | Bare reset does not look for an independent administrator | Caught | 9 / 4 |
| P03 | Microsoft administrator counts as the fallback | Caught | 3 / 4 |
| P04 | Bare reset judges Restart only | Caught | 1 / 0 |
| P05 | Reset of an unhealthy posture needs evidence | Caught | 4 / 2 |
| P06 | Reset of another account held responsible | Caught | 1 / 2 |
| P07 | Predicates apply when an administrator's facts moved | Caught | 1 / 0 |
| P08 | Predicates apply when role, link, credential or designation moved | Caught | 4 / 0 |
| P09 | A consumed code takes another account's reset out of the predicate | Caught | 2 / 2 |
| P10 | Enrolment may go during a change to another account | Caught | 1 / 0 |
| P11 | A reset may add to the posture | Caught | 1 / 0 |
| P12 | Health flag that disagrees with the facts is believed | **Escaped**, then caught after the fix | 0 / 0, then 4 / 0 |
| P13 | Snapshot that disagrees with the facts is believed | Caught | 1 / 0 |
| P14 | Health flag on the state before is believed | Caught | 2 / 0 |
| P15 | Health flag on the state after is believed | Caught | 2 / 0 |
| S01 | Replacement always allowed | Caught | 5 / 2 |
| S02 | Replacement may lose what an unhealthy posture held | Caught | 2 / 0 |
| S03 | Replacement demands the Local reading | Caught | 4 / 4 |
| S04 | Replacement falls back to the rule of a reset | Caught | 3 / 2 |
| E01 | Proof on attested Local recorded as `not_required` | Caught | 1 / 2 |
| E02 | Refusal for missing evidence recorded as attested | Caught | 2 / 2 |
| E03 | Revocation recorded as not requiring evidence | Caught | 2 / 2 |
| E04 | Host's own reading claims an attestation | Caught | 1 / 0 |
| E05 | Reset that cannot reduce recorded as attested | Caught | 14 / 6 |
| C01 | Replacement refused under the fallback code | Caught | 7 / 2 |
| C02 | Revocation given the wording of the health rule | Caught | 1 / 0 |
| C03 | A replacement's decision carries the wrong mutation | **Escaped**, then caught after the fix | 0 / 0, then 1 / 0 |
| C04 | A reset says it is a replacement | Caught | 1 / 0 |
| C05 | A designation says it is a revocation | Caught | 3 / 0 |
| C06 | An unavailable revocation says it is a designation | Caught | 1 / 0 |
| C07 | A judged revocation says it is a designation | Caught | 2 / 0 |
| C08 | Full evaluation loses its mutation | Caught | 2 / 0 |
| C09 | Allowed posture decision loses its mutation | Caught | 2 / 0 |

- Baseline green on the unedited tree: yes, at both commits (649 and 40 at `73140f2`; 655 and 40 at `58bb7b1`). Each source file was restored and checked against its committed blob, and the final rebuild reproduced the baseline: yes.
- **Which commit each result belongs to.** P12 and C03 to C09, P14, P15 ran at `58bb7b1`; the other 34 ran at `73140f2`. `src` is identical at both. The only test removed in between is not in any mutant's failed list.
- **Escapes.** P12: the old test's disagreement gave the same answer with or without the check. The new cases use states where believing the flag would allow the change, on each state, for both predicates. C03: no test read the mutation of a replacement decision. Every decision path now asserts its mutation, and the refusals that depend on it assert the wording.
- **Unit-only catches.** F04, F05, P04, P07, P08, P10 to P15, E04, C02 to C09, S02 and R06 fail only unit tests. Those properties are pinned in pure code; the SQL class does not separately exercise them.

## 6. Evidence and checksums

Archive: `G:\Backup\Apperture-Core-iam10-2b-evidence\2026-11-07-wp4bc-foundation-1b1c\`, a copy on another local drive, not an off-machine backup.

| Item | Path | SHA-256 or manifest |
|---|---|---|
| Validation at `58bb7b1` | `validation-v3-58bb7b1/` | In the manifest |
| First validation, superseded | `validation-v2-345a2c6-superseded/` | In the manifest |
| Honesty report | `honesty-58bb7b1/` | In the manifest |
| Mutation results | `mutation-results/` | In the manifest |
| Harness scripts | `tools/` | In the manifest |
| Checksum manifest (282 files, 0.183 GiB) | `SHA256SUMS.txt` | `9E8B1FB9ED2DC95D7C27FAA1458F7A4364707A6F24B97620EE3EB0A760520E0C` |
| Archive verification | `ARCHIVE-VERIFIED.txt` | 0 files differ from their sources |

## 7. Independent review

None yet. By the brief, the independent security review comes after Foundation 1A is combined.

Points for that review:

1. **Evidence class of a proof on attested Local (material ambiguity).** Decision record §4's table lists "a post-state proof that needs no missing input" under `not_required`, and §3.4 says an operation not depending on the missing input is `not_required`. But §4's rule says the value names "the evidence **actually used**", and the end of §3.4 says Local-only proofs "need Local established", distinguishing them from "the not_required cases". This implementation records `attested`, which is truthful and consistent with revocation and reset. Changing it is one line in `AdministratorViabilityGuard.Judge`; test `A_designation_proven_safe_on_the_local_reading_alone_records_the_attestation_it_used` and mutant E01 pin the current choice.
2. **A host that stops during an attempt.** The attempt no longer sees this, where the old second read would have. It is the stale-observation risk the record accepts (§3.5), bounded by the 180 s window. Reviewers should confirm the bound is enough.
3. **New refusal.** The decision record names no code for a replacement that loses the posture. The additive `recovery_access_posture_reduced` was chosen rather than falling back to a reset's rule.
4. **`EntraPrimaryAuthenticationPolicy.Deferred`.** Its `_ =>` arm would map the new verdict kind to unavailable. `Evaluate` never returns that kind, so the arm is unreachable today.
5. **Three M-2 SQL cases re-based.** They asserted two reads and now assert one. With a single reading, the environment can no longer change between reads in those cases, so they no longer exercise raise-only attribution against a change. That attribution stays proven by the unit tests in `AdministratorViabilityRemediationTests`.

## 8. Remaining risks and owner decisions

- **Residual risks:**
  - No re-check between decision and commit (the R-B2 position). Medium; owner.
  - A host's own reader records no evidence class. Low; by design.
  - The services are not integrated, so the first real proof of the calls comes with WP4B-C service work. Medium; next package.
- **Owner decision needed:** review point 1. Recommendation: keep `attested`, and amend the §4 table so it agrees with its rule.
- **Interface notes for Foundation 1A:**
  - `IHostConfigurationAttestationStore` is unchanged. If 1A adds a member, `ScriptedStore` (unit) and `CountingStore` (SQL) must implement it.
  - `EvidenceRemaining` relies on the meaning of `DatabaseNowUtc` and `RefreshedUtc`. If 1A changes either, the freshness bound changes with it.
  - One expression in the classifier's Fresh path changed, which may merge with any 1A edit there. The publisher and store files are untouched.
- **Known limits of the evidence:** see "Not run" in section 4 and the proxy writers in "What the SQL class proves". The shared test SQL container was used as it stands. L-B3 documentation corrections, IR-3, IR-4 and the manifest rules were not done (out of scope or Foundation 1A territory).

## 9. Pull request and closure

| | |
|---|---|
| PR | n/a: the brief stops at the committed checkpoint |
| Required checks at the PR head | n/a |
| Merge commit on `master` | n/a |
| Post-merge CI | n/a |
| Branch tip is an ancestor of `origin/master` | no |
| Worktree clean; ignored files compared with the archive | Clean; `artifacts/` holds build output and interim runs not archived (`it1`, `v1`, `v2`, `v3`, `evidence-v1`) |
| Worktree and branch retired | no: kept for Foundation 1A combination and review |
| Evidence archived | Section 6 |
| Post-PR supplement | n/a |
