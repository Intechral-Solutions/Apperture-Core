# Tests

| Project | Purpose |
|---------|---------|
| Apperture.ArchTests | Architecture and dependency rule tests |
| Apperture.UnitTests | Unit tests for domain and application logic |
| Apperture.Management.Ui.Shared.Tests | Static/source contract checks for shared UI |
| Apperture.IntegrationTests | Integration tests (SQL Server, Management Host HTTP, SFTP) |
| Apperture.EndToEndTests | Placeholder until real E2E workflow tests land |

## Integration test infrastructure

SQL Server and SFTP are **required** for the full `Apperture.IntegrationTests` project. See [Apperture.IntegrationTests/README-ManagementHostGovernance.md](Apperture.IntegrationTests/README-ManagementHostGovernance.md) for:

- `MSSQL_SA_PASSWORD`, `MSSQL_TEST_SERVER` (default `localhost,1433`)
- `APPERTURE_TEST_SQL` (governance harness database)
- `APPERTURE_TEST_SFTP_HOST` / `PORT` / `USERNAME` / `PASSWORD` (default `127.0.0.1:2222`)

**Quick local run (PowerShell):**

```powershell
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Pass" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
docker run --name apperture-test-sftp -p 2222:22 -d atmoz/sftp apperture:AppertureSftp59a!:1001:100:upload
$env:MSSQL_SA_PASSWORD = "YourStrong!Pass"
$env:APPERTURE_TEST_SQL = "Server=127.0.0.1,1433;User Id=sa;Password=YourStrong!Pass;TrustServerCertificate=True;Initial Catalog=Apperture_ManagementGovernanceTests;"
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
```

Without SQL/SFTP, governance and SqlServer-backed tests fail fast or skip with explicit messages — not silent passes.
