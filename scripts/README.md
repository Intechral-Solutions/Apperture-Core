# Scripts

PowerShell scripts for development, deployment scaffolding, CI, and tooling. Run from the repo root. Set `APPERTURE_REPO_ROOT` to override repo root detection.

## Quick start

**First-time setup (optional):**

```powershell
.\scripts\Initialize-AppertureEnvironment.ps1
```

Copies the docker-dev appsettings template when missing, optionally prompts for User Secrets SQL password (native/docker-compose path), and prints startup commands.

**Primary entry points:**

```powershell
# Direct commands
.\scripts\Manage.ps1 -Area dev -Command start
.\scripts\Manage.ps1 -Area dev -Command start -Mode aspire
.\scripts\Manage.ps1 -Area dev -Command start -Build

# Interactive menu (delegates to Manage.ps1)
.\scripts\Interactive.ps1
```

Saved defaults (`Area`, `Mode`, `Target`, `Service`, `Configuration`) persist in `scripts/.runtime/script-settings.ini` after successful commands. Resolution order: explicit CLI → saved INI → built-in default (`Mode` = **native** when unset).

```powershell
.\scripts\Manage.ps1 -Command defaults
.\scripts\Manage.ps1 -Command reset-defaults
```

## Manage.ps1 command surface

### Dev (`-Area dev`)

| Command | Description |
|---------|-------------|
| `start` | Start native (default) or Aspire dev environment. Use `-Build` to build before start. |
| `stop` | Stop dev environment (native leaves SQL container running). |
| `restart` | Stop then start. |
| `restart-service` | Restart `api`, `ui`, `worker`, `db`, or `all` (native only for per-service). |
| `status` | Ports, PIDs, runtime mode, SQL container, worker state. |
| `restore`, `build`, `clean`, `rebuild` | Solution-wide .NET commands. |
| `build-ui`, `watch-ui` | UI asset scaffold (npm pipeline TODO). |

Examples:

```powershell
.\scripts\Manage.ps1 -Area dev -Command start
.\scripts\Manage.ps1 -Area dev -Command stop
.\scripts\Manage.ps1 -Area dev -Command restart-service -Service api -Build
.\scripts\Manage.ps1 -Area dev -Command status
.\scripts\Manage.ps1 -Area dev -Command rebuild
```

### Deployment (`-Area deployment`, scaffolded)

| Command | Description |
|---------|-------------|
| `status`, `restore`, `build`, `publish`, `deploy`, `build-ui` | Placeholder workflows for future packaging/deploy targets. |

```powershell
.\scripts\Manage.ps1 -Area deployment -Command status
.\scripts\Manage.ps1 -Area deployment -Command deploy -Target windows-service
```

## Dev modes

| Mode | Behavior |
|------|----------|
| **native** (default) | docker-compose SQL + local dotnet Management.Host (:9000), Ui.Server (:9001), WorkerHost |
| **aspire** | `Apperture.AppHost` orchestration (requires Docker) |

Do not run native and Aspire simultaneously (port conflicts on 1433, 9000, 9001). SQL volumes differ (`apperture-sqlserver` vs `apperture-aspire-sql`).

## Layout

```
scripts/
  Manage.ps1              # CLI entry point
  Interactive.ps1         # Menu entry point
  lib/                    # Shared helpers (paths, SDK, INI settings, dotnet, UI assets)
  dev/
    Invoke-Dev.ps1
    commands/             # Dev command implementations
    lib/                  # Dev runtime helpers
    maintenance/          # reset-db, seed, backup, smoke scripts (not routed via Manage.ps1)
  deployment/
    Invoke-Deployment.ps1
    commands/             # Deployment scaffolds
  ci/                     # CI build/test/pack (unchanged)
  tools/                  # OpenAPI/module scaffolds (unchanged)
  .runtime/               # Local state (gitignored): script-settings.ini, dev PIDs
```

## Maintenance scripts (`dev/maintenance/`)

| Script | Description |
|--------|-------------|
| **reset-db.ps1** | Drop/recreate `Apperture` database and apply EF migrations. |
| **seed.ps1** | Placeholder — no sample-data seed script yet. |
| **backup-databases.ps1** | Database backup utility. |
| **Fix-ScheduleTables.ps1** | Create schedule tables if migrations were out of sync. |
| **epic102-api-smoke.ps1** | API smoke test helper. |

**`/api/runs` 500 — invalid `OperationRuns` columns:** Rebuild/restart the API so migrations apply, run `dotnet ef database update`, or **`scripts/dev/maintenance/reset-db.ps1`** for a clean DB.

## CI (`ci/`)

| Script | Description |
|--------|-------------|
| **build.ps1** | Restore and build the solution. |
| **test.ps1** | Run tests (`-Filter`, `-NoBuild`). |
| **pack.ps1** | Pack NuGet packages. |

## Requirements

- **.NET SDK 10** (see `global.json`)
- **Docker** (SQL Server in dev — native or Aspire)
- **PowerShell** 5.1 or PowerShell Core
