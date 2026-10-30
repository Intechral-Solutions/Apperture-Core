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

### Required and optional SQL mode (`APPERTURE_REQUIRE_SQL_TESTS`)

Most SQL Server tests already fail when SQL Server is not reachable (the audit suites through `AuditSqlScratch`, `SqlServerScratchDatabase.CreateRequiredAsync`, `RequireConnectionString`, the governance and bootstrap fixtures). A group of older ones, and the three IAM classes that read the switch directly, returned early instead. `SqlTestExecution` is now the one place that decides what such a test does:

| Mode | Set by | A SQL test that cannot run against SQL Server |
|------|--------|-----------------------------------------------|
| **Required** | `APPERTURE_REQUIRE_SQL_TESTS=1` in the **process environment** (exactly `1`; not read from `.env`) | **Fails** with `SqlTestExecutionRequiredException: SQL Server proof is required (APPERTURE_REQUIRE_SQL_TESTS=1) but this test could not run against SQL Server: …`. That type is deliberately not an `InvalidOperationException`, which older helpers catch to mean "unreachable", so no helper can turn the failure back into a skip. `SqlServerScratchDatabase.CreateAsync` and `EnsureTestDatabaseExists` follow the same rule. |
| **Optional** (variable unset) | the default | The test returns early after announcing `NOT EXECUTED (soft skip): …` on standard error, and `SqlTestExecution.NotExecutedCount` counts it. **xunit 2 has no dynamic skip, so the run reports that test as Passed.** It is not evidence of anything. |

Use required mode for any run that is offered as proof (CI, a closure run, a review). Optional mode exists only so a developer without a test SQL Server can run a subset of the suite; do not quote its pass counts for the SQL classes. A test may skip only by returning through `SqlTestExecution.NotExecuted`; `SqlSoftSkipGuardTests` fails on any other silent early return from an unavailable SQL Server, on a LocalDB connection literal and on a private read of the switch.

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
