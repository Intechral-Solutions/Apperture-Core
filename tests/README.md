# Tests

| Project | Purpose |
|---------|---------|
| Apperture.ArchTests | Architecture and dependency rule tests |
| Apperture.UnitTests | Unit tests for domain and application logic |
| Apperture.Management.Ui.Shared.Tests | Static/source contract checks for shared UI |
| Apperture.IntegrationTests | Integration tests (SQL Server, Management Host HTTP, SFTP) |
| Apperture.EndToEndTests | Placeholder until real E2E workflow tests land |

## Integration test SQL (important)

Integration tests **do not** use connections defined in the Management UI. They resolve SQL independently via `SqlServerTestConfiguration` with this precedence:

1. **`APPERTURE_TEST_SQL_CONNECTION_STRING`** — preferred for local dev (copy Aspire SQL endpoint here)
2. **`ConnectionStrings__DefaultConnection`** — Aspire-injected default when exported to the test process
3. **Legacy `MSSQL_SA_PASSWORD` + `MSSQL_TEST_SERVER`** — defaults to **`localhost,1433`** when server is unset

Governance HTTP tests (`ManagementHostGovernanceHttpTests`) use `SqlServerTestConfiguration.GetGovernanceConnectionString()`:

1. **`APPERTURE_TEST_SQL`** — full connection string (CI)
2. Same precedence as above, with database `Apperture_ManagementGovernanceTests`

Tests create their own databases on the selected server (`EnsureTestDatabaseExists`); they do **not** require a container named `apperture-test-sql`.

### Aspire dev SQL (dynamic port)

When AppHost exposes SQL on a dynamic host port (e.g. `127.0.0.1,58361`), set in repo-root `.env`:

```env
APPERTURE_TEST_SQL_CONNECTION_STRING=Server=127.0.0.1,58361;User Id=sa;Password=<your-aspire-sa-password>;TrustServerCertificate=True;Initial Catalog=Apperture;
```

Or export Aspire's `ConnectionStrings__DefaultConnection` before `dotnet test`.

**Do not** rely on `MSSQL_SA_PASSWORD` alone unless SQL is actually listening on `localhost:1433`. A stale `apperture-test-sql` Docker container on 1433 (from ad-hoc test runs) may have a **different** password than Aspire.

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
