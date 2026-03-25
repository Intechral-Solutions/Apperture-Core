# Management Host — HTTP governance integration harness (Epic 55 / 55A)

This folder exercises the **real** [Apperture.Management.Host](../../src/Apperture.ControlPlane/Apperture.Management.Host/Program.cs) pipeline: bootstrap, SQL persistence, migrations, governance, authentication, and authorization at the HTTP boundary.

## How SQL is discovered

| Variable | Purpose |
|----------|---------|
| `APPERTURE_TEST_SQL` | Full connection string for **Management Host governance** tests (`ManagementHostGovernanceHttpTests`). Must include `Initial Catalog` (fixture creates the database on `master` if missing, then migrates). |
| `MSSQL_SA_PASSWORD` | Required for other **SqlServer** integration tests (`SqlServerTestConfiguration`) using database `Apperture_IntegrationTests` by default. |
| `MSSQL_TEST_SERVER` | Optional; default `localhost,1433`. |
| `MSSQL_TEST_DATABASE` | Optional; default `Apperture_IntegrationTests`. |
| `APPERTURE_TEST_SFTP_HOST` | Hostname/IP for controlled SFTP verification proof (Epic 59A). Default `127.0.0.1`. |
| `APPERTURE_TEST_SFTP_PORT` | SFTP port for Epic 59A tests. Default `2222`. |
| `APPERTURE_TEST_SFTP_USERNAME` | Username for known-good SFTP verification. Default `apperture`. |
| `APPERTURE_TEST_SFTP_PASSWORD` | Password for known-good SFTP verification. Default `AppertureSftp59a!`. |

**CI:** [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) starts SQL Server and a controlled SFTP container, waits for ports (`1433`, `2222`), sets `APPERTURE_TEST_SQL` and `APPERTURE_TEST_SFTP_*` variables, then runs the full integration project. This includes Epic 59A SFTP verification proof tests.

## Running tests locally

**Governance suite only:**

```powershell
$env:APPERTURE_TEST_SQL = "Server=127.0.0.1,1433;User Id=sa;Password=YourStrong!Pass;TrustServerCertificate=True;Initial Catalog=Apperture_ManagementGovernanceTests;"
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~ManagementHostGovernanceHttpTests"
```

**Full `Apperture.IntegrationTests` project** (governance + other SqlServer tests): set both `APPERTURE_TEST_SQL` and `MSSQL_SA_PASSWORD` (same server/password is fine). Example with Docker:

```powershell
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Pass" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
# wait ~20s for SQL to accept connections
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

**Prerequisites:** SQL Server reachable with SA (or equivalent) credentials; `TrustServerCertificate=True` is typical for dev containers. LocalDB works if you point `APPERTURE_TEST_SQL` at `(localdb)\mssqllocaldb` and ensure the instance is running.

If SQL is unreachable, `ManagementHostGovernanceFixture` sets `SqlAvailable = false` and each governance test fails fast with a message explaining the requirement (no silent pass).

If SFTP is unreachable, Epic 59A tests fail fast with a message describing required `APPERTURE_TEST_SFTP_*` settings.

## How it works

1. **`ManagementHostWebApplicationFactory`** hosts `ManagementHostWebApplicationFactoryEntryPoint` (marker type in the Host assembly) via `WebApplicationFactory<T>`.
2. **`FixedBootstrapConnectionProfile`** replaces `IBootstrapConnectionProfile` so `ASPNETCORE_ENVIRONMENT=Testing` can use a normal connection string (the product bootstrap rejects config-based SQL outside Development).
3. **`ManagementTestAuthHandler`** (scheme `ManagementTest`) authenticates when the client sends header **`X-Test-External-Id`**. Missing header → unauthenticated → **401** on `[Authorize]` endpoints.
4. **`ManagementHostGovernanceFixture`** (collection fixture) probes SQL, starts the host once (migrations run), then idempotently seeds users, identity links, roles, and a sample integration. Principal/permission mapping is defined in **`ManagementGovernanceTestConstants`**.
5. **Epic 56A / 56B / 56C (procedures):** The fixture also ensures **`Sample.HelloWorld` is installed and enabled** in `ModuleInstallationStates` for the same environment id as [`StaticEnvironmentContext`](../../src/Apperture.ControlPlane/Apperture.ControlPlane.Security/StaticEnvironmentContext.cs) (default `local`, or `APERTURE_ENVIRONMENT_ID` when set). Without that row, `IProcedureRegistryService` filters out the module and procedure HTTP tests see an empty catalog. **`ExternalProcedureLauncher`** has `workflow.execute` and **`operation.execute`** for `module:Sample.HelloWorld`; the fixture **re-syncs** that role’s `PermissionsJson` when the expected JSON changes (so existing dev DBs pick up new permissions). **`ExternalWorkflowOnlyProcedureLauncher`** has workflow execute only (operation launch **403** tests). A second integration (**`SeededIntegrationForeignModuleId`**, foreign `moduleId`) supports **procedure / integration mismatch** launch tests. Idempotent **`EnsureForeignModuleIntegrationForHarnessAsync`** adds that row even when the main seed block already ran. **Epic 56C:** `Get_run_after_procedure_launch_shows_provenance_and_start_parameters_when_runnable` calls **`GET /api/runs/{id}`** after procedure launches to assert **integrationId**, **procedureId**, and **startParametersJson** (see ADR-0031).

## Adding a new permission scenario

1. Add stable **external id** and **user/role** constants (and JSON permission rows matching [SqlRoleRepository](../../src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlRoleRepository.cs) shape).
2. Extend **`ManagementHostGovernanceFixture.SeedAsync`** (guard with `AnyAsync` on a sentinel user id so re-runs stay idempotent).
3. Add HTTP tests in **`ManagementHostGovernanceHttpTests`** with **one** expected status per fixture (no OR-assertions).

## Related

- Minimal **Management.Api**-only smoke test: `ManagementApiTests` + `ManagementApiStubWebApplicationFactory` (no governance — not a substitute for this harness).
