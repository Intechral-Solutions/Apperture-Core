# Deploy

Deployment configuration.

| Folder | Purpose |
|--------|---------|
| [config/](config/) | Canonical `appsettings` templates (docker-dev, Windows, Azure) and shared UI config — see [config/README.md](config/README.md) |
| docker/ | docker-compose.dev.yml, env files, SQL init/migrations |
| k8s/ | Kubernetes manifests (future) |
| azure/ | Azure deployment config (future) |

Management platform configuration reference: [docs/03-operations/management-platform-configuration.md](../docs/03-operations/management-platform-configuration.md) (environment/startup layer). Configuration boundaries: [ADR-0039](../docs/02-decisions/ADR-0039-environment-startup-bootstrap-and-platform-settings.md).

**Epic 106 (Aspire)** delivers local orchestration via [`Apperture.AppHost`](../../src/Apperture.AppHost/). Setup automation and installer scripts remain deferred — see [ADR-0039 post-Aspire checklist](../docs/02-decisions/ADR-0039-environment-startup-bootstrap-and-platform-settings.md#post-aspire-revisit-checklist).

## Local development: Aspire (preferred)

From the repo root (requires Docker):

```powershell
dotnet run --project src/Apperture.AppHost/Apperture.AppHost.csproj
```

The AppHost orchestrates:

| Resource | Port / detail |
|----------|----------------|
| SQL Server (Aspire container, `2022-latest`, volume `apperture-aspire-sql`) | `localhost:1433` |
| Management API (`Apperture.Management.Host`) | `http://localhost:9000` |
| Management UI (`Apperture.Management.Ui.Server` + Blazor WASM client) | `http://localhost:9001` |
| Worker runtime (`Apperture.RuntimeHost.Windows.WorkerHost`) | background worker |

**First-time (both Aspire and script paths):** copy the docker-dev appsettings template for Management.Host dev overlays (`DevTestUser`, bootstrap admin, etc.):

```powershell
copy deploy\config\examples\appsettings.docker-dev.json.example src\Apperture.ControlPlane\Apperture.Management.Host\appsettings.Development.json
```

Aspire injects `ConnectionStrings:DefaultConnection` for SQL; the appsettings overlay is still required for auth/bootstrap dev flags.

**SQL password (Aspire path):** default in `src/Apperture.AppHost/appsettings.json` (`Parameters:sql-password`). Override via AppHost User Secrets if needed.

**Mutual exclusion:** Do not run Aspire and `Start-Dev.ps1` at the same time — both bind ports 1433, 9000, and 9001. Aspire uses its own SQL container/volume (`apperture-aspire-sql`); the script path uses docker-compose (`apperture-sqlserver`).

## Root Docker Compose shim (script path)

From the **repo root** you can control all Compose-defined services with a single interface:

```bash
docker compose up -d
docker compose down
docker compose ps
```

The root [docker-compose.yml](../docker-compose.yml) uses Compose `include` to pull in nested compose files under `deploy/docker/`. Add or remove entries in that `include` list as you add more compose files (e.g. `docker-compose.dev.yml`, `docker-compose.e2e.yml`).

## Dev environment: SQL Server (script path)

The script-based dev stack includes SQL Server for EF Core. **User Secrets are the single source of truth** for the SQL password on this path; `.env` is generated from User Secrets for Docker Compose.

### Quick start (script path)

```powershell
.\scripts\Start-Dev.ps1
```

This syncs User Secrets to `.env`, starts SQL Server (Docker), and launches the API (Management.Host), UI, and WorkerHost.

### First-time setup

1. **Set the SQL password** (one-time; stored in User Secrets, never committed):

   ```powershell
   dotnet user-secrets set "SqlServer:Password" "YourStrong!Passw0rd" --project src/Apperture.ControlPlane/Apperture.Management.Host
   ```

2. **Copy the docker-dev appsettings template** (one-time) for Server, Database, User (password comes from User Secrets):

   ```powershell
   copy deploy\config\examples\appsettings.docker-dev.json.example src\Apperture.ControlPlane\Apperture.Management.Host\appsettings.Development.json
   ```

3. Run `.\scripts\Start-Dev.ps1`

### How it works

- **User Secrets** stores `SqlServer:Password`. The .NET app reads it via Configuration.
- **scripts/dev/Sync-EnvFromUserSecrets.ps1** copies the password to `.env` as `MSSQL_SA_PASSWORD` for Docker Compose.
- **Start-Dev.ps1** runs the sync, starts `docker compose up -d`, then starts Management.Host and the UI Server.

### Stopping

- `.\scripts\dev\down.ps1` stops the API and UI.
- `docker compose down` stops SQL Server.

### Dev test user (local login)

In Development, the **DevTestUser** bootstrap ensures a configured test user exists with the platform administrator role. Add this to `appsettings.Development.json`:

```json
{
  "DevTestUser": {
    "Enabled": true,
    "Username": "tenny",
    "DisplayName": "Tenny (Dev)",
    "Password": "dev"
  },
  "LocalAuth": {
    "Enabled": true
  }
}
```

Then log in at `POST /api/auth/local/login` with `tenny` / `dev` (or your chosen password). The user will have full access to modules, workflows, runs, and the rest of the platform.

### Run migrations

When EF Core migrations exist:

```bash
dotnet ef database update --project src/Apperture.ControlPlane/Apperture.Management.Host
```

See [docs/00-overview/roadmap.md](../docs/00-overview/roadmap.md).
