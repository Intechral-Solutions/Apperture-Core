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

## What Belongs to Bootstrap

Bootstrap (or the host) owns:

- Connection string resolution (config, env, Key Vault)
- Auth mode (Integrated vs SQL auth)
- Validation-before-commit (connection test)
- Credential source selection
- Secure post-deploy update and recovery

Persistence receives a resolved connection string via `Func<IServiceProvider, string>`; it never reads configuration or Bootstrap directly.

## Project Structure

- **Apperture.Persistence.SqlServer** — Shared DbContext, migrations, retry/resilience.
- **Apperture.RuntimePlane.Persistence.SqlServer** — Thin extension for backward compatibility; derives from shared DbContext.

## Usage

### Registration Overloads

Hosts register persistence via:

1. **Connection string factory** (primary) — Bootstrap wires the resolved profile:
   ```csharp
   AddApperturePersistence(services, sp => sp.GetRequiredService<IBootstrapConnectionProfile>().ConnectionString);
   ```
2. **Explicit connection string** — For tests and simple hosts:
   ```csharp
   AddApperturePersistence(services, connectionString);
   ```

Bootstrap must be registered before persistence so the factory can resolve `IBootstrapConnectionProfile`. Persistence never parses configuration; connection string resolution belongs in Bootstrap. See [bootstrap-and-secrets-boundary.md](bootstrap-and-secrets-boundary.md).

### DbContextFactory for Background Execution

Background workers and message-driven flows use `IDbContextFactory<AppertureDbContext>` instead of a scoped DbContext:

```csharp
AddAppertureDbContextFactory(services, sp => connectionString, options);
```

HTTP request flows keep the scoped DbContext registered by `AddApperturePersistence`.

### SqlPersistenceOptions

Both `AddApperturePersistence` and `AddAppertureDbContextFactory` accept optional `SqlPersistenceOptions`:

- `MaxRetryCount` (default 3) — EnableRetryOnFailure count
- `CommandTimeout` — Optional command timeout

### Migrations on Startup

Opt-in automatic migrations:
```csharp
AddApperturePersistenceMigrationsOnStartup(services);
```
Registers a `IHostedService` that runs `context.Database.Migrate()` on startup when enabled.

## Schema Conventions

- **Table names**: PascalCase, default `dbo` schema
- **Audit fields**: `CreatedAt`, `UpdatedAt` — applied via conventions where configured
- **Optimistic concurrency**: Entities with concurrent-update risk use rowversion (`byte[] RowVersion` shadow property)

## Schema

The shared DbContext contains:

- `WorkflowInstances`, `WorkflowStepInstances`, `OperationRuns` — Runtime plane workflow state
- `AuditLogs`, `TelemetryEvents` — Cross-cutting audit and telemetry
- `SecretDescriptors`, `SecretVersions`, enrollment tables (owned by other subsystems)

## Migrations

Migrations live in `src/Apperture.Persistence/Apperture.Persistence.SqlServer/Migrations/`. Add migrations with:

```bash
dotnet ef migrations add MigrationName --project src/Apperture.Persistence/Apperture.Persistence.SqlServer --startup-project src/Apperture.ControlPlane/Apperture.Management.Host --context Apperture.Persistence.SqlServer.AppertureDbContext
```

## Related

- [Bootstrap and Secrets Boundary](bootstrap-and-secrets-boundary.md)
- [Secrets and key management](secrets-and-key-management.md)
