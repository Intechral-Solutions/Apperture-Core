# IAM-10.2b WP4B-C command line: independent review brief

Write for a reviewer who has not seen this work. The record is [iam10-2b-wp4b-c-cli-record.md](iam10-2b-wp4b-c-cli-record.md); read its sections 1 to 6 first.

## 1. What is under review

| | |
|---|---|
| Branch and head | `feat/iam10-2b-wp4bc-cli`, local only; code commits `658d7a6c`, `1e58c2fd`, `a51b59df`; the head is stated in the hand-over message |
| Baseline | `ffe92b72` (accepted services, SR-1 remediated). Review `git diff ffe92b72 <head> -- src tests` |
| Under review | the emergency command line (`src/Apperture.Cli/Apperture.Cli.Host`: `EmergencyCli.cs`, `EmergencyCommandRunner.cs`, `EmergencyCommandLine.cs`, `EmergencyOutcome.cs`, `EmergencyConsole.cs`, `EmergencyHelp.cs`), the PowerShell module's `-BareReset`, `NativeMfaCodeNotValidException` and its two throw sites, and the tests under `tests/Apperture.UnitTests/Cli` and `tests/Apperture.IntegrationTests/Audit/EmergencyCliSqlServerTests.cs` |
| Not under review | Foundation 1A to 1D and the services (accepted; reopen only on a concrete integration defect), WP5, OPS-1, environment tooling |

## 2. Binding requirements

SC-1 to SC-5 (record section 1). The questions below are the places where a mistake would matter most.

## 3. Questions

1. **Status map (SC-1).** Trace every exception a service entry point the commands call can throw, including what the audited unit wraps, to its exit status. Is there a path to 0 or 1 that should be 3, 4, 5 or 6, or a message that says "nothing was changed" when the change may have been applied? Is 5 for "applied, result unreadable" acceptable beside "unknown commit", or should it be separate?
2. **Plain `InvalidOperationException` as 6.** The services state a few conditions as plain exceptions; the command line reads the state back for the one race it can (enrolment) and otherwise reports 6 with the service's literal message. Is that the right line?
3. **Replacement workflow (SC-2).** Is there any input, state or timing in which `reset-mfa` changes the authenticator without a verified code, runs the bare reset, enrols, or leaves a candidate that would later switch? Are abandon-after-cancel, end of input and three wrong codes safe given that `AbandonReplacementAsync` is account-wide?
4. **Display (SC-3).** Can a recovery code or the staged key reach any output stream, log or audit row before a confirmed commit, or after an unknown one? Ctrl+C at each point, piped input, end of input.
5. **Composition (SC-4) and boundaries (SC-5).** Is the composition test strong enough that a dropped registration fails, given the optional constructor parameters? Are the regular-expression source rules bypassable in a way that matters (an alias, a generic resolution, a `var`)? Does anything in the services' own writer-coverage guard now see the command line differently?
6. **Compatibility.** The behaviour changes in record section 3 (strict options, `reset-mfa` meaning, `enroll-mfa` and `regenerate-codes` pre-checks): acceptable? Any caller missed?
7. **Controls.** Are the 28 mutants (record section 10) and the cases real controls or tautologies? What would a mutant of your own show? The strict-double cases make an unexpected service call fail the case: is any assertion vacuous because of it?
8. **Typed exception.** Is `NativeMfaCodeNotValidException` the smallest acceptable service change, and does it alter anything for existing callers?

## 4. Evidence available

The evidence archive and its manifest are named in the hand-over message and record section 12. The clean build, suites, focused SQL results, honesty outcome and the mutation logs are in it. Verify the manifest before relying on it.

## 5. Required output

As in the earlier briefs: `ACCEPT`, `ACCEPT WITH BOUNDED OBLIGATIONS` or `REJECT` at the exact commit; each new finding with a concrete scenario; the exact scope of the acceptance; what was run.
