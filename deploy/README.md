# Deploy

Deployment configuration. Planned structure:

| Folder | Purpose |
|--------|---------|
| docker/ | docker-compose.dev.yml, env files, SQL init/migrations |
| k8s/ | Kubernetes manifests (future) |
| azure/ | Azure deployment config (future) |

## Root Docker Compose shim

From the **repo root** you can control all Compose-defined services with a single interface:

```bash
docker compose up -d
docker compose down
docker compose ps
```

The root [docker-compose.yml](../docker-compose.yml) uses Compose `include` to pull in nested compose files under `deploy/docker/`. Add or remove entries in that `include` list as you add more compose files (e.g. `docker-compose.dev.yml`, `docker-compose.e2e.yml`).

## Dev environment: SQL Server

The dev stack includes SQL Server for EF Core. **User Secrets are the single source of truth** for the SQL password; `.env` is generated from User Secrets for Docker Compose.

### Quick start

```powershell
.\scripts\Start-Dev.ps1
```

This syncs User Secrets to `.env`, starts SQL Server (Docker), and launches the API (Management.Host) and UI.

### First-time setup

1. **Set the SQL password** (one-time; stored in User Secrets, never committed):

   ```powershell
   dotnet user-secrets set "SqlServer:Password" "YourStrong!Passw0rd" --project src/Apperture.ControlPlane/Apperture.Management.Host
   ```

2. **Copy appsettings.Development.example.json** (one-time) for Server, Database, User (password comes from User Secrets):

   ```powershell
   copy src\Apperture.ControlPlane\Apperture.Management.Host\appsettings.Development.example.json src\Apperture.ControlPlane\Apperture.Management.Host\appsettings.Development.json
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
