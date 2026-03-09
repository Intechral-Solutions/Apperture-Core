# Persistence Foundation

The persistence foundation is the shared SQL Server-based substrate for platform data and SQL-backed secret storage.

## Scope

Persistence Foundation owns:

- SQL connection and execution primitives (EF Core DbContext)
- Migrations and schema conventions
- Retry/resilience strategy (EnableRetryOnFailure)
- Transaction and concurrency conventions
- Audit table conventions

Persistence Foundation does **not** own:

- Bootstrap credential selection or connection string resolution
- Secret metadata or enrollment tokens (those are stored in the shared schema but owned by other subsystems)
- Runtime trust or operational auth

## Project Structure

- **Apperture.Persistence.SqlServer** — Shared DbContext, migrations, retry/resilience.
- **Apperture.RuntimePlane.Persistence.SqlServer** — Thin extension for backward compatibility; derives from shared DbContext.

## Schema

The shared DbContext contains:

- `WorkflowInstances`, `WorkflowStepInstances`, `OperationRuns` — Runtime plane workflow state
- `AuditLogs`, `TelemetryEvents` — Cross-cutting audit and telemetry
- Future: `SecretDescriptors`, `SecretVersions`, enrollment tables (owned by other subsystems)

## Usage

Hosts (Management.Host, WorkerHost, SchedulerHost) register persistence via `AddApperturePersistence(IConfiguration)` or `AddApperturePersistence(connectionString)`. Connection string comes from bootstrap configuration; see [bootstrap-and-secrets-boundary.md](bootstrap-and-secrets-boundary.md).

## Migrations

Migrations live in `src/Apperture.Persistence/Apperture.Persistence.SqlServer/Migrations/`. Add migrations with:

```bash
dotnet ef migrations add MigrationName --project src/Apperture.Persistence/Apperture.Persistence.SqlServer --startup-project src/Apperture.ControlPlane/Apperture.Management.Host --context Apperture.Persistence.SqlServer.AppertureDbContext
```

## Related

- [Bootstrap and Secrets Boundary](bootstrap-and-secrets-boundary.md)
- [Secrets and key management](secrets-and-key-management.md)
