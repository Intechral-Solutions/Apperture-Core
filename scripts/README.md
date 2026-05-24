# Scripts

PowerShell scripts for development, CI, and tooling. Run from the repo root or from within `scripts/`; paths are resolved via `$PSScriptRoot`. You can set `APPERTURE_REPO_ROOT` to override the repo root.

## Quick start (dev)

From repo root:

```powershell
.\scripts\Start-Dev.ps1
```

First time: set the SQL password with `dotnet user-secrets set "SqlServer:Password" "<password>" --project src/Apperture.ControlPlane/Apperture.Management.Host` and copy `deploy/config/examples/appsettings.docker-dev.json.example` to `appsettings.Development.json` in the Host folder. See [deploy/README.md](../deploy/README.md), [management-platform-configuration.md](../docs/03-operations/management-platform-configuration.md), and [ADR-0039](../docs/02-decisions/ADR-0039-environment-startup-bootstrap-and-platform-settings.md).

Scripts under `dev/` are **development orchestration** (start SQL, hosts, workers) — not the long-term platform settings control plane.

This starts **SQL Server** (Docker), the **Management API** (port 9000), and the **Management UI** (port 9001). Stop with `.\scripts\dev\down.ps1`; run `docker compose down` to stop SQL Server.

---

## Dev (`dev/`)

| Script | Description |
|--------|-------------|
| **up.ps1** | Start the platform (syncs User Secrets to .env, starts SQL Server, API, UI). Use `-NoBrowser` to avoid launching the browser. |
| **Sync-EnvFromUserSecrets.ps1** | Copy `SqlServer:Password` from User Secrets to `.env` for Docker Compose. Called automatically by up.ps1. |
| **down.ps1** | Stop API and UI processes on ports 9000 and 9001. |
| **logs.ps1** | Show which processes are listening on dev ports. |
| **seed.ps1** | Placeholder: seed dev database (implement when persistence is added). |
| **reset-db.ps1** | Drop/recreate the `Apperture` database and apply all EF migrations. |
| **Fix-ScheduleTables.ps1** | Create schedule tables if missing (only needed if migrations were out of sync). **Management.Host** on startup also applies migration `EnsureScheduleTablesConditional`, which does the same idempotently. |

**`/api/runs` 500 — invalid `OperationRuns` columns (`CapturedOutputJson`, …):** Your database is missing columns the model expects (often migration history vs. schema drift). **Management.Host** runs `MigrateAsync` on startup; rebuild and restart the API so migration `20260322220000_EnsureOperationRunsExtendedColumns` applies (idempotent `ALTER TABLE`). Or run `dotnet ef database update --project src/Apperture.Persistence/Apperture.Persistence.SqlServer --startup-project src/Apperture.ControlPlane/Apperture.Management.Host`, or **`scripts/dev/reset-db.ps1`** for a clean DB.

---

## CI (`ci/`)

| Script        | Description |
|---------------|-------------|
| **build.ps1** | Restore and build the solution. Use `-Configuration Release` and optional `-NoRestore`. |
| **test.ps1**  | Run tests. Use `-Filter "UnitTests"` to run a subset, `-NoBuild` to skip build. |
| **pack.ps1**  | Pack NuGet packages (`-Configuration Release`, optional `-OutputPath`, `-NoBuild`). |

---

## Tools (`tools/`)

| Script                  | Description |
|-------------------------|-------------|
| **generate-openapi.ps1**| Placeholder: generate OpenAPI spec/client from the Management API. |
| **scaffold-module.ps1** | Placeholder: scaffold a new module (e.g. `-Name MyDomain`). |

---

## Requirements

- **.NET SDK 10** (see `global.json`)
- **Docker** (for SQL Server in dev)
- **PowerShell** 5.1 or PowerShell Core (Windows or cross-platform)
