# Tests

| Project | Purpose |
|---------|---------|
| Apperture.ArchTests | Architecture and dependency rule tests |
| Apperture.UnitTests | Unit tests for domain and application logic |
| Apperture.Management.Ui.Shared.Tests | Static/source contract checks for shared UI |
| Apperture.IntegrationTests | Integration tests (SQL Server, Management Host HTTP, SFTP) |
| Apperture.EndToEndTests | Placeholder until real E2E workflow tests land |
| Apperture.BrowserTests | Opt-in Playwright browser proof harness (SEC-1.0). Skipped unless `APPERTURE_BROWSER_TESTS=1`. CI runs the suite as the blocking `browser-security` job; see its [README](Apperture.BrowserTests/README.md) |

## Integration test SQL (important)

Integration tests **do not** use connections defined in the Management UI. They resolve SQL independently via `SqlServerTestConfiguration` with this precedence:

1. **`APPERTURE_TEST_SQL_CONNECTION_STRING`** — preferred for local dev: the disposable test SQL server (normally the `apperture-test-sql` container at `127.0.0.1,1433`), **not** the Aspire dev SQL (see [Test SQL target safety](#test-sql-target-safety))
2. **`ConnectionStrings__DefaultConnection`** — Aspire-injected default when exported to the test process (refused when it points at the Aspire dev SQL on host port 14333)
3. **Legacy `MSSQL_SA_PASSWORD` + `MSSQL_TEST_SERVER`** — defaults to **`localhost,1433`** when server is unset

Governance HTTP tests (`ManagementHostGovernanceHttpTests`) use `SqlServerTestConfiguration.GetGovernanceConnectionString()`:

1. **`APPERTURE_TEST_SQL`** — full connection string (CI)
2. Same precedence as above, with database `Apperture_ManagementGovernanceTests`

Tests create their own databases on the selected server (`EnsureTestDatabaseExists`); they do **not** require a container named `apperture-test-sql`.

### Test SQL target safety

Test infrastructure creates, migrates, seeds and drops catalogs. Before it does, `SqlTestTargetGuard` checks the target. It is one source file, shared by the integration and browser test projects. A target that fails is **refused with an error that names the data source and the catalog and never a credential**. A refusal is a test failure; it is never reported as a skipped or passing test, and resolution never falls through from an unreachable test server to a development server.

**Approved disposable targets:** the `apperture-test-sql` container at `127.0.0.1,1433`; CI's SQL service; an ad-hoc container from `scripts/dev/maintenance/start-adhoc-test-sql.ps1` (dynamic port); LocalDB, only when you point a connection string at it yourself. No test falls back to LocalDB on its own any more: an unconfigured or unreachable target is a failure (or, in optional mode, an announced skip), never a run against whatever LocalDB happens to be installed.

**Never a test target:** the Aspire dev SQL container (host port **14333**, database `Apperture`).

| Rule | Detail |
|------|--------|
| Disposable catalogs only | A catalog being created or dropped must be named `Apperture_` plus a suffix of ASCII letters, digits and underscore (`Apperture_IntegrationTests`, or a random `Apperture_Aud14Vfy…`). The bare development database `Apperture` is never a test catalog, in any spelling or quoting. A connection string may still *default* to `Initial Catalog=Apperture` (CI's does); the helpers replace it with the test catalog, and only the catalog actually created or dropped is judged. |
| Governance catalogs | The catalog behind `APPERTURE_TEST_SQL`, and the governance and bootstrap fixtures, must also end in `Tests`. |
| Development SQL port | Port 14333 is refused whatever the host name, `tcp:` prefix, keyword alias (`Server`, `Data Source`, `Addr`, `Address`, `Network Address`) or zero padding. A failover partner is checked too. |
| Unplaceable targets | Refused: an unparseable string, an empty or malformed address, a port that is not a number from 1 to 65535, a named instance without a port (SQL Browser chooses the port, so it cannot be checked) and the `np:`, `lpc:` and `via:` protocols. |
| Exceptional use | Set `APPERTURE_ALLOW_PROTECTED_SQL_TEST_TARGET=1` in the **process environment** (deliberately not read from `.env`, so it cannot become a permanent setting). It lets the port-14333, named-instance and non-TCP refusals through for that run. It never relaxes the catalog rules. |

The guard runs in `SqlServerTestConfiguration` (every candidate before anything connects, then the resulting catalog), `EnsureTestDatabaseExists`, both scratch-catalog helpers' drop, the governance fixture and the browser harness's `BrowserSqlConfiguration`. `SqlTestTargetGuardCoverageTests` pins that each is in front of its `CREATE`/`DROP DATABASE`.

It does not see through a client-side SQL alias or a tunnel or port mapping that publishes the dev server on another port: point tests at the test SQL server, never at the dev one.

A refusal reads like this (the connection string's user and password are never printed):

```text
Refusing to create a test catalog. SQL target [data source=127.0.0.1,14333; database=Apperture_IntegrationTests] is not allowed:
the server port 14333 is reserved for the development SQL server. …
```

**`.env`:** `scripts/dev/lib/DevDockerSql.ps1` (`Sync-DevEnvFromUserSecrets`, used by native `Manage.ps1` dev start and restart) changes only the `MSSQL_SA_PASSWORD` line of the repo-root `.env` and leaves every other setting, comment, blank line, line ending and byte order mark as it was, so your `APPERTURE_TEST_SQL_CONNECTION_STRING` survives. An unchanged password does not touch the file. It refuses, and leaves the file untouched, if the file is not UTF-8 or contains a NUL byte. It never prints a value.

**Do not** rely on `MSSQL_SA_PASSWORD` alone unless SQL is actually listening on `localhost:1433`. A stale `apperture-test-sql` Docker container on 1433 (from ad-hoc test runs) may have a **different** password than the dev SQL.

### A SQL test that cannot run against SQL Server fails (no optional mode)

**Policy (DEV-1.1B-R): SQL-dependent tests fail whenever their intended SQL execution cannot occur. A test never reports Passed solely because SQL was unavailable.** There is no optional mode, no skip and no switch. Running the integration tests on a machine with no test SQL Server produces failures for every SQL test, and that is the correct, honest result.

Every helper that cannot reach or prepare SQL Server throws `SqlTestUnavailableException` (built by `SqlTestExecution.Unavailable`): `SqlServerTestConfiguration.RequireConnectionString()`, `EnsureTestDatabaseExists`, `SqlServerScratchDatabase.CreateAsync` (which never returns null), `AuditSqlScratch.CreateAsync` and the Management Host fixtures. The type is deliberately not an `InvalidOperationException`, which older helpers catch to mean "unreachable", so no helper can turn the failure back into a return. A target refused by `SqlTestTargetGuard`, a failed migration and a failed connection fail as themselves.

**`APPERTURE_REQUIRE_SQL_TESTS` is retired.** It is not read anywhere and setting it to any value changes nothing (`The_retired_require_switch_cannot_turn_the_failure_off`). It was introduced as the way to make a skip fail; there is no skip left. It can be deleted from any script or workflow that still sets it, and `ci.yml` must not set it (`SqlHonestyCiGuardTests` refuses it).

Two independent defences keep the policy true:

| Defence | What it does | What it cannot do |
|---------|--------------|-------------------|
| `SqlSoftSkipGuardTests` (runs with the ordinary suite, about a second) | Reads test source and refuses the shapes of a silent exit: a call to the null-returning `GetConnectionString()`/`GetGovernanceConnectionString()` (use `RequireConnectionString()`), an availability condition (`CanConnect*`, `SqlAvailable`, ...) with no `throw`, a broad catch (`Exception`, `SystemException`, `ExternalException`, `SqlException`, `DbException`, ...) that swallows SQL setup or ends in a bare `return`, skip vocabulary (`NotExecuted`, `Try*Async` acquisition helpers), a LocalDB literal, a read of the retired switch. A catch that asserts on the exception it expects, or stores it for an assertion later in the same block, is a test of a failure and is accepted unless it also returns. | It is lexical and not exhaustive. A shape it does not recognise passes it, and it cannot see a failure swallowed after a connection succeeded. |
| `scripts/dev/Test-SqlExecutionHonesty.ps1` (about a minute) | Runs the whole assembly against an unreachable server, with your own SQL configuration removed from the environment (and `.env` not read). Every test that **passes** must be on `SqlExecutionHonesty/approved-non-sql-passes.tsv`; every test on `required-unavailable-failures.tsv` must **fail** with a message that says SQL Server was unavailable; discovery, execution and a TRX written by this run must agree. Evidence: `sql-honesty-report.json`. | It proves nothing about a test that passes for a reason the list explains wrongly; the list is reviewed by people. Unreachable-SQL testing cannot detect an error swallowed only after a successful connection: the live SQL tests prove real execution and assertions. |

The division of labour: the source guard catches known suspicious shapes quickly; the honesty check catches an unexpected pass against unavailable SQL; the live SQL tests (CI's `Test` step against the SQL service) prove that SQL really ran and the assertions held.

Run the honesty check (from the repository root; keep `-ArtifactsPath` inside the repository) after changing SQL test code, helpers or fixtures:

```powershell
pwsh scripts/dev/Test-SqlExecutionHonesty.ps1                 # builds, runs, writes artifacts/sql-execution-honesty/<utc>/
pwsh scripts/dev/Test-SqlExecutionHonesty.ps1 -NoBuild        # reuse an existing build: it must be newer than every source file
pwsh scripts/dev/Test-SqlExecutionHonesty.ps1 -Record         # list what passed and failed, to review a list change (not proof)
```

**Exit codes.** `0` PASS (authoritative), `1` FAIL (evidence rejected, or the run or infrastructure failed), `2` RECORDED (`-Record`: diagnostic lists, never an authoritative pass). `-Record` and `-DotnetPath` (a test seam) are refused with exit 1 when `GITHUB_ACTIONS` or `CI` is `true`.

**Provenance.** The report records the commit and tree, the test assembly's path and SHA-256, the configuration, the SDK version, and whether the script built the assembly or reused one. A hash identifies the binary; it does not prove the binary was built from the current source. A PASS therefore also needs one of: the script built the assembly itself (MSBuild's up-to-date check is the claim), or, with `-NoBuild`, a tracked tree equal to HEAD, no untracked file the build would compile, and a test assembly newer than every tracked build input. A dirty tree, an edit to a source file after the build, or a checkout that rewrites a source file fails the run; rebuild by omitting `-NoBuild`. Known limits: timestamps are not cryptographic, and a modification time cannot see a deletion, so a commit made after the build that only deletes tracked files leaves the binary newer than every remaining input and a stale binary can PASS with a report naming the new HEAD. In CI the checkout is fresh and the Build step is the only writer of the binary, so neither limit is reachable there; for authoritative local evidence omit `-NoBuild`.

**Fresh results.** The script clears only its own files in the output directory (`build.log`, `discovery.log`, `run.log`, `sql-honesty.trx`, `sql-honesty-report.json`, `observed-*.txt`) before it runs, never anything else and never recursively, and rejects a TRX older than the run. A missing or malformed TRX, a wrong `dotnet test` exit code, failed discovery or a changed test count is a FAIL.

Changing either list, or the script itself, is deliberate: `AllowListFingerprintTests` pins each file's SHA-256, so an edit needs a second edit in test source. The script pin is a safeguard against an accidental regression, not a substitute for reviewing a change to both the script and its pin. The script cannot certify its own integrity (a weakened copy can return PASS for its own run), so the layers are: the script refuses the ordinary false-pass conditions (exercised by its scenarios in `SqlExecutionHonestyScriptTests`); the fingerprint and decisive-line tests in `AllowListFingerprintTests` are complementary protection; the ordinary required `Test` step executes those tests and rejects a script that no longer matches; and a coordinated change to the script and its pin still needs independent code review. A new test belongs on the allow-list only if it needs no SQL Server to pass, and any new passing test in the IntegrationTests assembly (including a new non-SQL unit test) must be added or the check fails. In a git worktree one allow-listed source pin (`IntegrationTestSecretProtectorTests.Factory_replaces...`) may fail because `.git` is a file there; the check tolerates exactly that one, declared with `requires-git-directory`, and requires it to pass in a normal clone.

The script's own contract (exit codes, provenance, fresh outputs, credential isolation) is tested in `Apperture.UnitTests/DevScripts/SqlExecutionHonestyScriptTests` against a throw-away repository and a stand-in for `dotnet`.

**In CI.** The required `build-and-test` job runs `./scripts/dev/Test-SqlExecutionHonesty.ps1 -Configuration Release -NoBuild -OutputDirectory artifacts/sql-execution-honesty` after the Release build and before the `Test` step. The step has no environment of its own and the script removes every `APPERTURE_*`, `MSSQL_*` and `ConnectionStrings__*` variable from the test process, so it never sees the CI SQL credentials. Only `sql-honesty-report.json` is uploaded, and only when that step fails. `SqlHonestyCiGuardTests` pins the invocation, its position, the absence of `-Record`, and the unchanged required check names (`build-and-test`, `browser-security`). It reads the workflow as text and fails closed: the honesty and upload steps must be exactly their approved lines (so a YAML continuation line, a block scalar or an extra key fails), the Build and Test steps must keep their single-line `run:` commands, no step of `build-and-test` may set `continue-on-error` and only the upload may be conditional, and the workflow and both required jobs may carry only the listed keys (a job-level `if:`, `continue-on-error`, `strategy` or workflow-level `defaults:` fails until the guard is reviewed). Inline `# comments` on those lines and re-indentation also fail; that is intentional.

### Opt-in diagnostics

Two tests are measurements, not regression tests. They are off in an ordinary run (`dotnet test Apperture.slnx`, CI, the honesty check) and run only when you ask for them. Neither one ever reports Passed without having done its work.

| Diagnostic | Enable with | In an ordinary report | Run it |
|------------|-------------|-----------------------|--------|
| Password evaluation timings (`PasswordEvaluationDesktopTimingDiagnostics`, UnitTests) | `APPERTURE_PASSWORD_TIMING_DIAGNOSTICS=1` | **Skipped**, with the variable named in the reason | see below |
| Audit classification benchmark (`AuditClassificationBenchmark`, IntegrationTests): one million unclassified audit rows classified by the real startup step while the `migrations` readiness check is polled | `APPERTURE_AUDIT_BENCHMARK=1` (optionally `APPERTURE_AUDIT_BENCHMARK_ROWS`, default 1,000,000) | **Absent**: it is not discovered, so it is neither Passed nor Skipped | see below |

Only the exact value `1` enables either one. `0`, `true`, an empty value or anything else leaves it off.

```powershell
# Password evaluation timings: writes 15 timings to the test output and asserts nothing
$env:APPERTURE_PASSWORD_TIMING_DIAGNOSTICS = "1"
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~PasswordEvaluationDesktopTimingDiagnostics" --logger "console;verbosity=detailed"
Remove-Item Env:APPERTURE_PASSWORD_TIMING_DIAGNOSTICS

# Audit classification benchmark: needs the approved test SQL target (see "Test SQL target safety")
$env:APPERTURE_TEST_SQL_CONNECTION_STRING = "Server=127.0.0.1,1433;User Id=sa;Password=...;TrustServerCertificate=True;Initial Catalog=Apperture;"
$env:APPERTURE_AUDIT_BENCHMARK = "1"
# $env:APPERTURE_AUDIT_BENCHMARK_ROWS = "1000000"   # optional
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~AuditClassificationBenchmark" --logger "console;verbosity=detailed" -- RunConfiguration.TreatNoTestsAsError=true
Remove-Item Env:APPERTURE_AUDIT_BENCHMARK
```

- **Keep `-- RunConfiguration.TreatNoTestsAsError=true` on the benchmark command.** The benchmark exists only when the variable is exactly `1` in the test process. If the variable is missing, misspelled (`APPERTURE_AUDIT_BENCHMARKS`), set in a different shell, or not propagated to the test process, the name filter matches zero tests. Without this argument that is reported as `No test matches the given testcase filter` and **exit code 0**, which looks like a successful run that did nothing; with it, zero matching tests is a nonzero exit code. (The password diagnostic does not need it: when not enabled it is discovered and reported Skipped.)
- **Set the variable in the same shell as the command.**
- **The timings are not assertions.** There is no threshold: wall-clock limits would fail on slow hosts and prove nothing about correctness. Read the printed numbers; the evaluator's behaviour is covered by the ordinary password tests.
- **The benchmark is a real SQL test once requested.** It seeds the rows, runs `PersistenceMigrationHostedService`, polls readiness, and asserts that no row is left unclassified, that the check ends Healthy and began Unhealthy, and that a second start finds nothing to do. With the variable set and no reachable test SQL it fails like every other SQL test, and it uses the same protected scratch catalogs (`Apperture_Aud11Bench_…`) and the same target guard, so the Aspire dev SQL (port 14333) is refused. With a very small `APPERTURE_AUDIT_BENCHMARK_ROWS` the work can finish before the first readiness poll and the "began Unhealthy" assertion fails; that assertion is the measurement, so use a large enough count rather than expecting the test to bend.
- **The SQL execution marker is not proof.** The `EXECUTED against ...` line a SQL test writes (`AuditSqlScratch.ExecutionMarker`) is a diagnostic for the person reading the log. What shows a test really ran against SQL Server is its assertions on database state, and across the whole assembly `scripts/dev/Test-SqlExecutionHonesty.ps1`.
- **Why the benchmark is not skipped.** The honesty check treats any Skipped result in the IntegrationTests assembly as a violation, and an allow-listed test that is no longer discovered as one too, so neither a Skip nor a "passes when off" entry can describe it. `OptInBenchmarkFactAttribute` keeps it undiscovered unless requested, and the honesty check (which removes every `APPERTURE_*` variable from its test process) never discovers it.

### CI / docker-compose path

```powershell
$env:APPERTURE_TEST_SQL_CONNECTION_STRING = "Server=127.0.0.1,1433;User Id=sa;Password=...;TrustServerCertificate=True;Initial Catalog=Apperture;"
# or legacy:
$env:MSSQL_SA_PASSWORD = "..."
$env:MSSQL_TEST_SERVER = "127.0.0.1,1433"
$env:APPERTURE_TEST_SQL = "Server=127.0.0.1,1433;User Id=sa;Password=...;TrustServerCertificate=True;Initial Catalog=Apperture_ManagementGovernanceTests;"

dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
```

See [Apperture.IntegrationTests/README-ManagementHostGovernance.md](Apperture.IntegrationTests/README-ManagementHostGovernance.md) for SFTP variables.

The Management Host factory replaces `ISecretProtector` with a test-only AES-GCM protector in this assembly so the host can start where Windows DPAPI is unavailable. Production registration is unchanged: `Program.cs` still calls `AddAppertureSqlSecrets()` with no protector, and a non-Windows host that does not replace it fails startup validation.

## Source-reading tests: the shared corpus and the repository root

Many tests read the repository's source to pin a rule (a coverage guard: "only these files may call X", "no page uses a raw colour"). They share two small helpers in [TestSupport/](TestSupport/), compiled into `Apperture.UnitTests`, `Apperture.Management.Ui.Shared.Tests` and `Apperture.Management.Ui.Client.Tests` (a linked source file and a global `using Apperture.TestSupport;`, not a new project):

- **`RepositoryRoot.Find()`** returns the repository root from the test binaries. A folder is the root when it holds a `.git` directory (an ordinary clone), a `.git` **file** (a linked worktree keeps a one-line pointer file there, so a check for a `.git` directory alone fails the whole source-reading suite in a worktree) or the repository's `AGENTS.md`. Do not write another root finder.
- **`SourceTree.Files(subtree, extensions, includeMigrations, minimum)`** returns the production files under a folder of `src/` (for example `src` or `src/Apperture.ControlPlane/Apperture.Management.Ui.Client`):
  - It does **not descend** into folders named exactly `bin`, `obj` or `artifacts` (lower case, as the tooling creates them; a source folder named `Artifacts` stays in). A walk that filtered those paths after enumerating them paid for every file of every old build (on a machine with old builds in place that was the whole cost of a guard, tens of seconds per test). The repository is walked once per test process; every view is then an in-memory filter.
  - It reads the **working tree, not the git index**, so a new file that is not yet committed is scanned like any other and a guard still fails for an offender added locally.
  - The caller states its own boundary: the subtree, the extensions, and `includeMigrations` (a guard about writers excludes `Migrations`; a guard about entities and tables includes them). Sharing one walk does not widen or narrow any guard.
  - The caller states a **floor** (`minimum`; the `SourceTree.Floor` constants are about two thirds of the tracked counts). A scan that finds fewer files throws instead of returning an empty list, so a wrong root, a missing folder or an over-eager prune turns the guard red; a guard over nothing would pass without checking anything.
- A new coverage guard asks `SourceTree` for its files and picks a floor; it does not call `Directory.EnumerateFiles(..., SearchOption.AllDirectories)` over `src` and filter afterwards. `Conventions/SourceTreeTests` and `Conventions/RepositoryRootTests` pin the behaviour above.
- `artifacts/**` is excluded from the default compile items of these three projects (`DefaultItemExcludes`), so a build run with a relative `--artifacts-path` from a project folder cannot compile its own generated files into the next build.
- Not covered: `Apperture.IntegrationTests` and `Apperture.BrowserTests` keep their own root finders and source walks. The first carries SQL-honesty guards that this change deliberately leaves alone; the second is kept self-contained for CI job isolation.

## Evidence, test selection and mutation controls

A record's test claims are produced by the tracked toolkit in [scripts/dev/validation/](../scripts/dev/validation/README.md), not by copied scripts: `preflight.ps1` and `Invoke-Evidence.ps1` (provenance, TRX, checksums; skipped or undiscovered tests are never a pass), `Compare-Trx.ps1`, and `Invoke-Mutation.ps1` (mutations run only in a disposable clone; one that passes its tests is a failed result). [test-manifest.json](test-manifest.json) maps production paths to test classes for `Select-Tests.ps1`; it plans development iteration and never narrows the required CI, which runs every test. A production file the manifest does not claim is reported `UNMAPPED`, which is the cue to add a rule.

## Deterministic tests: concurrency, bUnit, test order and time

A test that passes because of how fast something ran, or in which order the runner happened to start it, is not evidence. The rules below come from the cases that were found and fixed.

- **A test double that more than one thread can reach is thread-safe.** A recording writer behind a concurrency test takes one lock for its writes and hands out snapshots (`ConnectionToolInvocationTests.AuditSink`); an unsynchronised `List<T>` there lost an update in about a quarter of the class runs. A request the test expects to be refused at once also carries an upper bound, so a regression fails instead of hanging the run.
- **bUnit: the synchronous `Click()`, `Input()` and `Submit()` return at the handler's first pending `await`**, not when it has finished. Assert straight after one only when the handler cannot reach a pending await first. Otherwise use `ClickAsync()`, `InputAsync()` or `SubmitAsync()` (they return when the handler has finished and rendered), or `WaitForAssertion` on an effect that renders. Two cautions: a call to a mock renders nothing, so a bUnit wait cannot see it (let the mock complete a `TaskCompletionSource` and wait on that); and Blazor does not render again after a handler that faults, so the DOM cannot show state released after a defect. A check that something did **not** happen (a second request, a second submit) is made after the work has finished, never straight after the trigger.
- **xunit does not promise an order.** A test starts from the state it needs (it resets or creates it) instead of from what an earlier test left. A test that has to empty a shared catalog, such as the first-administrator bootstrap tests, has a catalog of its own (`BootstrapFirstAdminFixture`), so the reset cannot reach another class's data.
- **No wall-clock assertion for a property that is not about time.** "The writer is waited for" is observed from the server (`LockedRowHolder.WaitUntilBlockedAsync`, `OwnershipGateRace.WaitForWaitersAsync`) and fails if the other side finishes first; "a pass with nothing to classify is cheap" is the statements it issues and the lock it did not take. A delay used as the assertion passes on a slow server whether or not the property holds, and fails on a loaded one when it does. Upper bounds that are never waited in a passing run (hang guards) are fine.
