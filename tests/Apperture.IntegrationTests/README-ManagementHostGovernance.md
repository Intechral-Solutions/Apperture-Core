# Management Host — HTTP governance integration harness (Epic 55)

This folder exercises the **real** [Apperture.Management.Host](../../src/Apperture.ControlPlane/Apperture.Management.Host/Program.cs) pipeline: bootstrap, SQL persistence, migrations, governance, authentication, and authorization at the HTTP boundary.

## Running tests

- **SQL required:** LocalDB or SQL Server must accept the connection string. Default database name: `Apperture_ManagementGovernanceTests` on `(localdb)\mssqllocaldb`.
- **Override connection string:** set environment variable `APPERTURE_TEST_SQL` to a full SQL Server connection string (include `TrustServerCertificate=True` for local dev if needed).

```powershell
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~ManagementHostGovernanceHttpTests"
```

If SQL is unreachable, `ManagementHostGovernanceFixture` sets `SqlAvailable = false` and each test fails fast with a message explaining the requirement (no silent pass).

## How it works

1. **`ManagementHostWebApplicationFactory`** hosts `ManagementHostWebApplicationFactoryEntryPoint` (marker type in the Host assembly) via `WebApplicationFactory<T>`.
2. **`FixedBootstrapConnectionProfile`** replaces `IBootstrapConnectionProfile` so `ASPNETCORE_ENVIRONMENT=Testing` can use a normal connection string (the product bootstrap rejects config-based SQL outside Development).
3. **`ManagementTestAuthHandler`** (scheme `ManagementTest`) authenticates when the client sends header **`X-Test-External-Id`**. Missing header → unauthenticated → **401** on `[Authorize]` endpoints.
4. **`ManagementHostGovernanceFixture`** (collection fixture) probes SQL, starts the host once (migrations run), then idempotently seeds users, identity links, roles, and a sample integration. Principal/permission mapping is defined in **`ManagementGovernanceTestConstants`**.

## Adding a new permission scenario

1. Add stable **external id** and **user/role** constants (and JSON permission rows matching [SqlRoleRepository](../../src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlRoleRepository.cs) shape).
2. Extend **`ManagementHostGovernanceFixture.SeedAsync`** (guard with `AnyAsync` on a sentinel user id so re-runs stay idempotent).
3. Add HTTP tests in **`ManagementHostGovernanceHttpTests`** with **one** expected status per fixture (no OR-assertions).

## Related

- Minimal **Management.Api**-only smoke test: `ManagementApiTests` + `ManagementApiStubWebApplicationFactory` (no governance — not a substitute for this harness).
