# Management Host — HTTP governance integration harness (Epic 55 / 55A)

This folder exercises the **real** [Apperture.Management.Host](../../src/Apperture.ControlPlane/Apperture.Management.Host/Program.cs) pipeline: bootstrap, SQL persistence, migrations, governance, authentication, and authorization at the HTTP boundary.

## How SQL is discovered

Resolution is centralized in [`SqlServerTestConfiguration.cs`](../SqlServerTestConfiguration.cs).

Every candidate is checked by `SqlTestTargetGuard` before anything connects to it: the Aspire dev SQL (host port 14333) and the bare `Apperture` catalog are refused, and a governance catalog must end in `Tests`. A refusal fails the test with a message naming the target; it is not skipped. See [Test SQL target safety](../../README.md#test-sql-target-safety).

**General SqlServer integration tests** (`AuditTimelineSqlServerTests`, `Epic73SqlSeamsIntegrationTests`, etc.):

| Priority | Variable | Purpose |
|----------|----------|---------|
| 1 | `APPERTURE_TEST_SQL_CONNECTION_STRING` | Preferred explicit connection string (Aspire dynamic port, e.g. `127.0.0.1,58361`). Catalog is overridden to `Apperture_IntegrationTests` unless `MSSQL_TEST_DATABASE` is set (legacy path only). |
| 2 | `ConnectionStrings__DefaultConnection` | Aspire-injected SQL when exported to the test process. Catalog overridden to test database. |
| 3 | `MSSQL_SA_PASSWORD` + `MSSQL_TEST_SERVER` | Legacy; **defaults to `localhost,1433`**. Avoid when Aspire SQL uses a dynamic port. |

**Management Host governance HTTP tests** use `GetGovernanceConnectionString()`:

| Priority | Variable | Purpose |
|----------|----------|---------|
| 1 | `APPERTURE_TEST_SQL` | Full connection string (CI). |
| 2–3 | Same as above | Catalog `Apperture_ManagementGovernanceTests` (fixture creates DB if missing). |

Tests create uniquely named databases on the selected server. They do **not** reference a Docker container named `apperture-test-sql` (that name is not in source; ad-hoc `docker run --name apperture-test-sql` from docs is optional and separate from Aspire).

### SFTP (Epic 59A)

| Variable | Purpose |
|----------|---------|
| `APPERTURE_TEST_SFTP_HOST` | Hostname/IP for controlled SFTP verification proof (Epic 59A). Default `127.0.0.1`. |
| `APPERTURE_TEST_SFTP_PORT` | SFTP port for Epic 59A tests. Default `2222`. |
| `APPERTURE_TEST_SFTP_USERNAME` | Username for known-good SFTP verification. Default `apperture`. |
| `APPERTURE_TEST_SFTP_PASSWORD` | Password for known-good SFTP verification. Default `AppertureSftp59a!`. |

**CI:** [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) starts SQL Server and a controlled SFTP container, waits for ports (`1433`, `2222`), sets `APPERTURE_TEST_SQL` and `APPERTURE_TEST_SFTP_*` variables, then runs the full integration project. This includes Epic 59A SFTP verification proof tests.

## Epic 73 / Epic 74 preflight — `Epic73SqlSeamsIntegrationTests`

These tests provide **SQL-backed evidence** for the post–Epic 73 seams: platform EF migrations, UNISA module migrations (separate history table), connector `IConnectorClientFactoryContributor` registration end-to-end, and `IScheduleDispatchRunner` resolution against a migrated database (same worker dispatch stack as `WorkerHostStartupDiTests`, but on real SQL Server).

**Requirements:** Configure SQL via `APPERTURE_TEST_SQL_CONNECTION_STRING`, `ConnectionStrings__DefaultConnection`, or legacy `MSSQL_SA_PASSWORD` (see precedence above).

**Run only this suite (Aspire example):**

```powershell
$env:APPERTURE_TEST_SQL_CONNECTION_STRING = "Server=127.0.0.1,58361;User Id=sa;Password=<aspire-sa-password>;TrustServerCertificate=True;Initial Catalog=Apperture;"
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~Epic73SqlSeamsIntegrationTests"
```

**Legacy docker-compose on 1433:**

```powershell
$env:MSSQL_SA_PASSWORD = "YourStrong!Pass"
$env:MSSQL_TEST_SERVER = "127.0.0.1,1433"
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~Epic73SqlSeamsIntegrationTests"
```

Greenfield migration ownership for hosts is documented in [Epic73 ADR — Greenfield database](../../docs/02-decisions/Epic73-execution-hot-path-and-host-roles.md#greenfield-database-who-runs-which-migration-operational).

## Running tests locally

**Governance suite only (Aspire):**

```powershell
$env:APPERTURE_TEST_SQL_CONNECTION_STRING = "Server=127.0.0.1,58361;User Id=sa;Password=<aspire-sa-password>;TrustServerCertificate=True;Initial Catalog=Apperture;"
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~ManagementHostGovernanceHttpTests"
```

**Governance suite (CI / explicit full string):**

```powershell
$env:APPERTURE_TEST_SQL = "Server=127.0.0.1,1433;User Id=sa;Password=YourStrong!Pass;TrustServerCertificate=True;Initial Catalog=Apperture_ManagementGovernanceTests;"
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~ManagementHostGovernanceHttpTests"
```

**Full `Apperture.IntegrationTests` project** with Aspire SQL:

```powershell
$env:APPERTURE_TEST_SQL_CONNECTION_STRING = "Server=127.0.0.1,58361;User Id=sa;Password=<aspire-sa-password>;TrustServerCertificate=True;Initial Catalog=Apperture;"
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
```

**Full project with dedicated Docker SQL on 1433** (optional; not required for Aspire dev):

```powershell
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Pass" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
# wait ~20s for SQL to accept connections
# Then either:
$env:APPERTURE_TEST_SQL_CONNECTION_STRING = "Server=127.0.0.1,1433;User Id=sa;Password=YourStrong!Pass;TrustServerCertificate=True;Initial Catalog=Apperture;"
# or legacy:
$env:MSSQL_SA_PASSWORD = "YourStrong!Pass"
$env:MSSQL_TEST_SERVER = "127.0.0.1,1433"
$env:APPERTURE_TEST_SQL = "Server=127.0.0.1,1433;User Id=sa;Password=YourStrong!Pass;TrustServerCertificate=True;Initial Catalog=Apperture_ManagementGovernanceTests;"
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
```

For Epic 59A SFTP verification proof locally, start an SFTP container and set SFTP env vars:

```powershell
docker run --name apperture-test-sftp -p 2222:22 -d atmoz/sftp apperture:AppertureSftp59a!:1001:100:upload
$env:APPERTURE_TEST_SFTP_HOST = "127.0.0.1"
$env:APPERTURE_TEST_SFTP_PORT = "2222"
$env:APPERTURE_TEST_SFTP_USERNAME = "apperture"
$env:APPERTURE_TEST_SFTP_PASSWORD = "AppertureSftp59a!"
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~Sftp"
```

**Prerequisites:** SQL Server reachable with SA (or equivalent) credentials; `TrustServerCertificate=True` is typical for dev containers. LocalDB works only if you point `APPERTURE_TEST_SQL` at `(localdb)\mssqllocaldb` yourself and ensure the instance is running; there is no implicit LocalDB fallback.

If no configured SQL target is reachable, `ManagementHostGovernanceFixture` and `BootstrapHttpFixture` throw `SqlTestUnavailableException` from `InitializeAsync`, so every test of the collection fails with a message explaining the requirement (no silent pass, and no availability flag for a test to skip on). There is no optional mode and no switch: see "A SQL test that cannot run against SQL Server fails" in `tests/README.md`.

If SFTP is unreachable, Epic 59A tests fail fast with a message describing required `APPERTURE_TEST_SFTP_*` settings.

## How it works

1. **`ManagementHostWebApplicationFactory`** hosts `ManagementHostWebApplicationFactoryEntryPoint` (marker type in the Host assembly) via `WebApplicationFactory<T>`.
2. **`FixedBootstrapConnectionProfile`** replaces `IBootstrapConnectionProfile` so `ASPNETCORE_ENVIRONMENT=Testing` can use a normal connection string (the product bootstrap rejects config-based SQL outside Development).
3. **`ManagementTestAuthHandler`** (scheme `ManagementTest`) authenticates when the client sends header **`X-Test-External-Id`**. Missing header → unauthenticated → **401** on `[Authorize]` endpoints.
4. **`ManagementHostGovernanceFixture`** (collection fixture) probes SQL, starts the host once (migrations run), then idempotently seeds users, identity links, roles, and a sample integration. Principal/permission mapping is defined in **`ManagementGovernanceTestConstants`**.
5. **Epic 56A / 56B / 56C (procedures):** The governance host enables **`Sample.HelloWorld` before module-permission registration**, for the same environment id as [`StaticEnvironmentContext`](../../src/Apperture.ControlPlane/Apperture.ControlPlane.Security/StaticEnvironmentContext.cs) (default `local`, or `APERTURE_ENVIRONMENT_ID` when set). The permission catalog is a startup snapshot of modules that are already enabled. A row inserted only after the host starts leaves `workflow.execute` unknown, so launch returns `workflow_start_forbidden` even though the launcher role JSON is present. The fixture still keeps that install row for later activation-filter reads. Without an enabled row, `IProcedureRegistryService` filters out the module and procedure HTTP tests see an empty catalog. **`ExternalProcedureLauncher`** has `workflow.execute` and **`operation.execute`** for `module:Sample.HelloWorld`; the fixture **re-syncs** that role’s `PermissionsJson` when the expected JSON changes (so existing dev DBs pick up new permissions). **`ExternalWorkflowOnlyProcedureLauncher`** has workflow execute only (operation launch **403** tests). A second integration (**`SeededIntegrationForeignModuleId`**, foreign `moduleId`) supports **procedure / integration mismatch** launch tests. Idempotent **`EnsureForeignModuleIntegrationForHarnessAsync`** adds that row even when the main seed block already ran. **Epic 56C:** `Get_run_after_procedure_launch_shows_provenance_and_start_parameters_when_runnable` calls **`GET /api/runs/{id}`** after procedure launches to assert **integrationId**, **procedureId**, and **startParametersJson** (see ADR-0031).

## Adding a new permission scenario

1. Add stable **external id** and **user/role** constants (and JSON permission rows matching [SqlRoleRepository](../../src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlRoleRepository.cs) shape).
2. Extend **`ManagementHostGovernanceFixture.SeedAsync`** (guard with `AnyAsync` on a sentinel user id so re-runs stay idempotent).
3. Add HTTP tests in **`ManagementHostGovernanceHttpTests`** with **one** expected status per fixture (no OR-assertions).

## Related

- Management.Api is a class library hosted by Management.Host. There is no standalone API stub host.
