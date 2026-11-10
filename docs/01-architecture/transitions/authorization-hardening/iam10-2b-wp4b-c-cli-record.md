# IAM-10.2b WP4B-C: the emergency command line

| | |
|---|---|
| Package | IAM-10.2b, work package 4B-C, the command-line integration (the last piece of WP4B-C after the [services](iam10-2b-wp4b-c-services-record.md)) |
| Branch | `feat/iam10-2b-wp4bc-cli`, worktree `F:\Code\Apperture\wt-iam10-2b-wp4bc-cli`, **local only**: not pushed, no pull request, the protected IAM branch is not advanced |
| Baseline | `ffe92b7287b81281da82b66e195cfa70b36e34f0`, the accepted services package (SR-1 remediated). **The protected IAM branch `feat/iam-10.2b-administrator-viability` is still at `1c1030ad`**, ten commits behind this baseline, and `1c1030ad` is an ancestor of `ffe92b72`. The brief assumed the branch had been fast-forwarded; it had not. Nothing was done about it (advancing IAM is out of scope): this branch starts from the exact accepted commit, so a later fast-forward of IAM to `ffe92b72` leaves this branch based on IAM's head |
| Scope | `emergency` commands only: `reset-mfa` as the replacement workflow, the explicit bare reset, the typed-outcome and exit-status map, recovery-code display, the console seam, the composition and source rules. Not WP5, not OPS-1, not environment work. Accepted services and Foundation 1A to 1D are unchanged except one typed exception (section 7) |
| Code commits | `658d7a6c` (production), `1e58c2fd` (tests), `a51b59df` (review fixes, section 9) |
| Documentation | this record and its [review brief](iam10-2b-wp4b-c-cli-review-brief.md) |

## 1. Requirements and their disposition

SC-1 to SC-5 are carried forward from the services record (section 15.7) and the services review. They are binding.

| Id | Requirement | Disposition | Where | Controls |
|---|---|---|---|---|
| SC-1 | Map busy, unavailable, conflict and unknown outcomes to distinct operator outcomes and exit codes | **Done.** `EmergencyOutcomeMapper` reads the type and the stable code, never a message. Statuses 0 to 6 (section 4). Every `GovernanceConflictCodes` value must be decided in a test, so a new code cannot fall through | `EmergencyOutcome.cs` | the mapper tests, the runner status tests, the SQL cases for busy, unavailable, rollback and unknown; mutants M1 to M7, S2, S3, S5 |
| SC-2 | `reset-mfa` is the replacement workflow; the bare reset is explicit and separate | **Done.** `reset-mfa` stages, shows the key once, reads a code, verifies against the new key, switches through `CompleteReplacementAsync`; abandons on cancel, end of input or three wrong codes. The bare reset is `reset-mfa --bare-reset` only, never a fallback | `EmergencyCommandRunner.cs`, `EmergencyCommandLine.cs` | R1, R2, S1; strict doubles make a call to the wrong service fail the case |
| SC-3 | Show codes and secrets only from a committed result | **Done.** Recovery codes are printed by one function from the value a service call returned; an unknown outcome, a refusal, a rollback, a busy gate, an empty set and an already-applied switch show none. The staged secret is shown once, before the switch, as the contract says (it is not an authenticator until the switch) | `ShowCommittedRecoveryCodes` | the display-timing cases (event order), source rule, R4, R6 |
| SC-4 | A composition test of the command line's container | **Done.** The production composition (`EmergencyCli.BuildProvider`) is built with only the connection and the platform protector replaced; the services resolve with none of their optional dependencies unset; and the whole command is run through `EmergencyCli.RunAsync` against a throwaway catalog in both isolation modes | `EmergencyCliCompositionTests`, `EmergencyCliSqlServerTests` | C1, F1 |
| SC-5 | Reach the switch only through `INativeMfaService.CompleteReplacementAsync` | **Done.** The only service the commands change state through are `INativeMfaService` and `IEmergencyAccessService`; the one repository named is `IUserRepository`, called for reads only; no candidate, credential, unit, gate or attestation type is named. Source rules pin this, and the services' own writer guard (unchanged) still passes | source rules in `EmergencyCliCompositionTests` | R3, R4 |

Also carried: **SO-1** (null evidence records `not_required`) stays dormant: the command line's reading is always attested or unavailable. **SI-1** (a busy gate on the administrators' set-password action returns 500) stays deferred; it is a host concern, not the command line's.

## 2. `reset-mfa`

The normal command. Help text (`emergency reset-mfa --help`):

```
emergency reset-mfa [--user-id <id> | --username <name>]
  Replaces the account's authenticator without a gap. This is the normal way and it is safe to abandon:
    1. A new authenticator is staged and its key is shown once. Add it to an authenticator app.
    2. Enter a code from the new authenticator. It is checked against the new key, not the current one.
    3. Only then is the new authenticator made active and the recovery codes replaced, in one audited step;
       the new recovery codes are shown once, after that step is confirmed.
  Until step 3 the current authenticator and its recovery codes keep working. Cancelling, a wrong code, or
  letting the replacement expire (10 minutes) changes nothing. It is refused when no authenticator is enrolled.
```

Behaviour:

* Staging, verification and abandonment hold no transaction and no gate; the SQL case reads the gate and the active authenticator **from inside the prompt** and finds both free and unchanged.
* A wrong code is asked for again at a prompt (three attempts); with piped input it is not (one attempt). Running out of attempts, end of input, or Ctrl+C discards the staged replacement and exits with 2 (1 for Ctrl+C).
* A replacement that expired, was superseded, consumed or changed is a retryable conflict (3). It is **not** discarded by the command, because the staged row may now be another operator's.
* An account with no authenticator is refused (1) with the instruction to run `enroll-mfa`. It never becomes an enrolment or a reset.
* The switch is called with the version the verification returned. Its result `AlreadyApplied` (an earlier request made this very switch) is a success with no codes and the instruction to run `regenerate-codes` if they were lost.
* Cancellation applies only while waiting for input. A change that has started is never given the token.

## 3. The bare reset

`emergency reset-mfa --bare-reset [--user-id <id> | --username <name>]`: removes the authenticator and every recovery code, with nothing in their place, through the viability-protected `ResetAsync`. Help text marks it `DESTRUCTIVE` and states it is never done instead of a replacement that failed.

* It asks for nothing and does not enrol afterwards: it ends with "run `emergency enroll-mfa`".
* A refusal is reported as the service gave it: `entra_independent_fallback_required` or `last_viable_administrator` (1), `administrator_viability_unavailable` (4), a busy gate (3), an unknown outcome (5). None of them leads to a replacement or an enrolment (SQL: no candidate row after a refusal).
* Intentional behaviour change: before, `reset-mfa` was a bare reset followed by a new enrolment. Now `reset-mfa` is the safe replacement and the destructive part needs `--bare-reset`. An account that has no authenticator used to be enrolled by `reset-mfa`; it is now refused and `enroll-mfa` is the command.
* Other intentional changes: options are read strictly (an unknown option, a missing value, a repeated option, or both `--user-id` and `--username` is a usage error, never ignored; before, `--user-id` with no value silently fell back to the default account); a usage error is settled before anything is opened; `enroll-mfa` of an enrolled account and `regenerate-codes` of an unenrolled one are refused before anything is staged. The PowerShell module gained `Register-AppertureEmergencyAuthenticator -Reset -BareReset`; its `-Reset` now means the replacement. No script in the repository other than that module calls `reset-mfa`.

## 4. Exit statuses and the typed-outcome map

The exit statuses are printed in `emergency --help` and pinned by test. The previously proposed mapping was adopted unchanged; nothing in the records had fixed it.

| Status | Meaning | Mapped from |
|---|---|---|
| 0 | done and confirmed | success (including a switch an earlier request already made) |
| 1 | refused, or stopped by the operator; nothing changed | `GovernanceConflictException` with `last_viable_administrator`, `last_viable_sign_in_method`, `last_local_credential`, `entra_independent_fallback_required`, `authenticator_not_enrolled`; `AuthorizationRefusalException`; `EmergencyAccessException` (other codes); Ctrl+C |
| 2 | invalid usage or input; nothing changed | unknown command or option; no user; `NativeMfaCodeNotValidException`; `PasswordPolicyException`; end of input; `EmergencyAccessException` `confirm_fallback_required`, `user_not_found`, `duplicate_username`, `reserved_username`; `GovernanceNotFound`/`Validation` |
| 3 | busy or retryable conflict; nothing changed | `PlatformAdministratorSetBusyException`, `GovernanceBusyException`; `authenticator_changed`, `administrator_access_changed`, `recovery_access_posture_reduced`, `authenticator_replacement_no_longer_valid`, `authenticator_replacement_expired`; `AuditOperationNotCompletedException` (commit failed, rollback confirmed, reruns used up); `EmergencyAccessException` `stale_update` |
| 4 | the configuration evidence is unavailable; nothing changed | `administrator_viability_unavailable` |
| 5 | the result is not confirmed | `AuditOutcomeUnknownException` (may or may not be applied); `AuditCommittedResultUnavailableException` (applied, result unreadable); a switch that returned no usable codes |
| 6 | unexpected internal failure | `AuditInvariantException`, `PlatformAdministratorSetGateException` (not contention), any plain `InvalidOperationException` (wiring defects), a failure of setup or of resolving the services, anything else |

Notes. **5 covers two cases** the proposal did not separate: an unknown commit, and a change known to be applied whose result cannot be shown. Both say "check the state"; neither says "nothing was changed". **6 reports type names and never a message** for an unknown exception (a message can carry what the operator typed); a plain `InvalidOperationException`, whose messages the services write as literals, shows its message. The map orders derived types before `InvalidOperationException`, so a defect (`AuditInvariantException`) and a typed input error never read as a plain condition. Each outcome prints `Error [<stable code>]: <message>` and, where there is one, `Next: <step>` on stderr. No text names a package, epic or review label (pinned by test).

## 5. Recovery codes and secrets

* Recovery codes are printed from exactly one function, `ShowCommittedRecoveryCodes`, from the list a service call returned (`CompleteEnrollmentAsync`, `RegenerateRecoveryCodesAsync`, `CompleteReplacementAsync`). The enrolment challenge's own code list is never read (a source rule).
* After a refusal, a busy gate, a confirmed rollback, an unknown outcome, an unreadable committed result, an empty or blank list, or an already-applied switch, no code is printed. SQL: every one of those cases asserts no code in the output; the unknown-outcome case also shows the commit did reach the database, which is why the operator is told to check rather than that nothing changed.
* The staged authenticator's key is printed once, before the code prompt, and is the only secret printed before a commit; the output says the replacement is not active until verified.
* Secrets come from stdin only; no option takes one (pinned). The commands use no logger. SQL: no key, code or recovery code appears in any audit row's details or target.
* Non-interactive use: with redirected input no prompt text is written, a wrong code is not retried, end of input discards the staged replacement and exits 2, and nothing is switched without a code.

## 6. Composition

`EmergencyCli.RunAsync` parses and settles usage, builds the production container (`BuildProvider`: the bootstrap CLI services, persistence, the DPAPI protector on Windows, `AddAppertureEmergencyOperatorServices`), creates a scope and takes `INativeMfaService`, `IEmergencyAccessService` and `IUserRepository` once (`CreateRunner`). Setup and resolution failures are mapped like any other outcome. A test seam (`console`, `configureServices`) replaces registrations after the production composition; it is null in the process.

Both `NativeMfaService` and `EmergencyAccessService` take the unit, the gate and the audit writer as optional constructor parameters, so a registration that went missing would build a service with nothing there. The composition test reflects every such field and requires it set; the SQL host cases run real commands through the container and would fail otherwise. The container reads the deployment's configuration only through the attestation (`AttestedDeploymentAuthenticationConfiguration`), as designed.

## 7. The one service change

`NativeMfaCodeNotValidException` (a sealed `InvalidOperationException`, same message) replaces the untyped exception the service threw for a code that does not match, at the two places it is thrown (first enrolment, replacement verification). The command line needed it to tell a mistyped code from any other plain condition without reading a message. Nothing else in the services changed. Behaviour and message are identical for existing callers, and the existing service tests that expect `InvalidOperationException` still pass.

## 8. Validation

All steps ran at the validated code commit `a51b59df` (tree `1e692eccc3470988c8c24e33e0da061331a0c96a`, `src` `1802100fdb2e460dcd7b5f91162a96e8ba812638`, `tests` `79f3452bb91fcfd466ef4824aeb22f5565cfd8f1`), SDK 10.0.402, with `src` and `tests` equal to HEAD (the only uncommitted files were these records). Evidence: `artifacts/cli-logs/final/status.json`, the TRX files and the logs.

| Step | Result |
|---|---|
| Clean build | `dotnet build Apperture.slnx -c Release --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers --artifacts-path artifacts/cli-release`: **0 errors**, 73 warnings (the accepted baseline also has 73: the one analyzer warning the command line first gave was removed), 392 s |
| UnitTests | **6364 passed, 0 failed**, 1 skipped by design (the opt-in desktop password-timing diagnostic). The services baseline was 6247 passed: the difference, +117, is exactly the command line's unit cases (the namespace `Apperture.UnitTests.Cli`) |
| ArchTests | 38/38 |
| Management.Ui.Shared.Tests / Management.Ui.Client.Tests / AppHost.Diagnostics.Tests | 1497/1497, 532/532, 7/7 |
| **Focused SQL**, Release, real SQL Server | **1455/1455**: 657 with `rcsi: False`, 657 with `rcsi: True`, 141 without a mode; 2556 s. This is the services package's 36-term filter (byte-identical, kept in the evidence) plus the new `EmergencyCliSqlServerTests`: the baseline's 1411 cases all pass, and the 44 additions are the command line's (22 per mode). The test SQL password appears in no output |
| Of which: the command line over real SQL | `EmergencyCliSqlServerTests` 44 (22 per mode): the replacement (normal, current authenticator untouched at the prompt, default account, abandonment, wrong code, expired, superseded, already applied, busy gate, confirmed rollback, unknown commit, no attestation needed), the bare reset (success, typed refusal, evidence unavailable), no authenticator, regeneration (success, busy), status, and three runs through the production container |
| SQL honesty | TBD-HONESTY |
| Not run | the full `IntegrationTests` assembly against real SQL (only the focused set; the honesty run executes the whole assembly against an unreachable endpoint); `BrowserTests`, `EndToEndTests`; the built `apperture` process on a Windows console with the DPAPI protector and a running Management Host; Linux and hosted CI |

Development runs that are not the final evidence: the command line's SQL cases first ran 38/42; the four failures were the injected commit failures firing on the candidate staging commit (one context serves the whole command), fixed in the test by arming them after verification, then 42/42; after the review fixes 44/44.

## 9. Independent review of this checkpoint (pre-hand-over)

Before the hand-over a four-lens read-only review (outcome mapping; secret display, cancellation and non-interactive use; routing, composition and guards; test quality and compatibility) ran over `1e58c2fd`, with each finding put to a skeptic who tried to refute it. 10 findings: 6 survived, 4 were refuted. The survivors, all fixed in `a51b59df` with controls:

| Finding | Fix | Control |
|---|---|---|
| Resolving the services is outside the outcome map: a connection-profile failure exits 1 with a raw message (two lenses, and the test lens: no test) | the failure goes through the map: status 6 with a next step | F1; a resolution-failure test |
| `Ctrl+C` cannot interrupt a blocked prompt (the console's async read completes synchronously), and a line typed with it would still be used | the read runs on its own thread beside the wait; a line that arrives with the cancellation is dropped | F3; two console tests |
| A lost enrolment race (another operator finished first) is reported as an internal failure | the state is read back and the race is a refusal; a plain failure on an unenrolled account stays 6 | F2; two tests |
| Default-account resolution (no option, the designated account, the reserved name, `--username`) had no test | unit cases and a SQL case through the real repository | F4, F5 |

Refuted: abandon discards another operator's newer candidate (the service's abandon is account-wide; the command only abandons after the operator's own cancel, end of input or exhausted attempts, never after a conflict; documented in section 2); a blank line at a prompt discards at once (by design: end of input); no SQL case for the authenticator changing between verification and switch (the superseded and already-applied cases cover both outcomes); the PowerShell wrapper leaves a stale `$LASTEXITCODE` when the CLI is not installed (not part of this change).

## 10. Negative controls

A mutant is one exact, single-occurrence text replacement applied to the committed tree, built, and run against the stated tests; the file is restored with `git checkout` and the tree was clean afterwards. Unit mutants ran the filter `UnitTests.Cli`: the 117 command-line cases and four unrelated environment-resolver cases (121 in all, which is the "Total" in the logs); SQL mutants ran the 44 SQL cases (both isolation modes). **28/28 caught**, each by at least one named case. The failing counts are the cases that failed.

| Mutant | Routing or mapping mistake | Caught by (failing cases) |
|---|---|---|
| R1 | plain `reset-mfa` routed to the bare reset | 34 unit |
| R2 | `--bare-reset` ignored | 5 unit |
| R3 | the switch presents the staged version, not the verified one | 16 unit |
| R4 | a switch that did not return discards the staged replacement | 12 unit |
| R5 | piped input retries a wrong code | 1 unit |
| R6 | an empty set of codes is shown as a success | 1 unit |
| R7 | enrolment no longer refuses an enrolled account | 1 unit |
| R8 | abandoning never discards the staged replacement | 4 unit |
| M1 | unknown commit becomes an internal failure | 6 unit |
| M2 | evidence unavailable becomes a plain refusal | 5 unit |
| M3 | busy gate becomes an internal failure | 4 unit |
| M4 | a confirmed rollback becomes an internal failure | 3 unit |
| M5 | a wrong code becomes an internal failure | 2 unit |
| M6 | an expired replacement becomes a refusal | 3 unit |
| M7 | a committed result that cannot be read becomes an internal failure | 3 unit |
| P1 | an option the command does not take is ignored | 6 unit |
| C1 | the operator services are not registered in the container | 4 unit |
| S1 | SQL: plain `reset-mfa` routed to the bare reset | 34 SQL |
| S2 | SQL: unknown commit becomes an internal failure | 2 SQL |
| S3 | SQL: busy gate becomes an internal failure | 4 SQL |
| S4 | SQL: abandoning never discards | 4 SQL |
| S5 | SQL: evidence unavailable becomes a plain refusal | 4 SQL |
| S6 | SQL: the switch presents the staged version | 12 SQL |
| F1 | a failure while resolving the services escapes the outcome map | 1 unit |
| F2 | the lost enrolment race stays an internal failure | 1 unit |
| F3 | a line that arrives with the cancellation is used | 1 unit |
| F4 | the designated account is never looked up | 1 unit |
| F5 | SQL: the designated account is never looked up | 2 SQL |

Notes. R4 is the consequential one for an unknown outcome: after a switch that may have applied, discarding the staged replacement would remove the evidence the candidate's applied-earlier proof needs. R1 and S1 are the "safe replacement became the destructive reset" mistake. The mutation harness and the mutant list are in the evidence archive.

## 11. Remaining and deferred

* **WP5**, the full pair and concurrency matrix, is not started and not changed by this package.
* A **running Management Host plus the real process end to end** (the built `apperture` executable, the DPAPI protector, a console) was not run: the tests drive the real runner and the real container over throwaway catalogs with the test protector, and the process console is unit tested. A manual smoke of `emergency reset-mfa` on Windows against a dev installation remains for the owner.
* Linux and hosted CI are unverified, as in the earlier packages. The Windows-only protector is registered only on Windows; elsewhere the command line refuses with a platform message unless a protector is provided.
* Abandoning from an interrupted process (a killed terminal, a power loss) leaves a staged replacement to expire after ten minutes; it never affects the active authenticator.
* `status` still renames the designated account to its canonical name (existing, audited, ungated by design R-6).
* SI-1 (the host's set-password busy mapping) and the test SQL container's exposure on `0.0.0.0:1433` (an owner action recorded on 2026-11-07) are unchanged.

## 12. Evidence

TBD-EVIDENCE
