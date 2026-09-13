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

`Manage.ps1` owns local process start/stop for the native stack (and can launch AppHost with `-Mode aspire`). Management API/UI does **not** start WorkerHost — see [Worker-Hosting-Guidance-v1.md](../docs/03-operations/Worker-Hosting-Guidance-v1.md). There is no supported `scripts/dev/start-worker.ps1` / `restart-worker.ps1` path.

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
| `build-ui` | Compile Management UI CSS once (Tailwind standalone CLI). |
| `watch-ui` | Same compile in watch mode; optional, run in a second terminal. |

Examples:

```powershell
.\scripts\Manage.ps1 -Area dev -Command start
.\scripts\Manage.ps1 -Area dev -Command stop
.\scripts\Manage.ps1 -Area dev -Command restart-service -Service api -Build
.\scripts\Manage.ps1 -Area dev -Command status
.\scripts\Manage.ps1 -Area dev -Command rebuild
```

## Management UI CSS

`wwwroot/css/app.css` is **generated**. Never edit it by hand — the next `build-ui` overwrites it.

| File | Role |
|------|------|
| `Apperture.Management.Ui.Client/styles/app.css` | Tailwind entry point and source-scan rules |
| `Apperture.Management.Ui.Client/styles/theme.css` | Authored semantic light/dark tokens, `.btn*`, `.focus-ring`, nav rail |
| `Apperture.Management.Ui.Client/wwwroot/css/management-ui-primitives.css` | Authored `ui-*` primitives (not compiled by Tailwind) |
| `Apperture.Management.Ui.Client/wwwroot/css/app.css` | **Generated output** (committed) |

```powershell
.\scripts\Manage.ps1 -Area dev -Command build-ui     # one-shot compile
.\scripts\Manage.ps1 -Area dev -Command watch-ui     # optional watcher
```

The pinned **Tailwind 4.1.8** standalone CLI is downloaded on first use to
`scripts/.runtime/tailwind/<version>/` (gitignored) and verified against a SHA256 pinned in
[`lib/Apperture.UiAssets.ps1`](lib/Apperture.UiAssets.ps1). There is no Node, npm, or `package.json`.

Typical inner loop — Aspire in one terminal, the watcher in another:

```powershell
.\scripts\Manage.ps1 -Area dev -Command start -Mode aspire
```

```powershell
.\scripts\Manage.ps1 -Area dev -Command watch-ui
```

Edit a `.razor` utility class or `styles/theme.css`, Tailwind rewrites `app.css`, then refresh the
browser. `watch-ui` only rebuilds CSS — it never starts, stops, or restarts Aspire, the API, SQL, or the
worker. Stop it with Ctrl+C.

Because the generated file is committed, `dotnet build`, `publish`, and CI keep working without
downloading Tailwind; the tradeoff is that you must run `build-ui` and commit the result whenever you
change a source stylesheet or add a utility class. CI can adopt a staleness check later:

```powershell
.\scripts\Manage.ps1 -Area dev -Command build-ui
git diff --exit-code -- "*wwwroot/css/app.css"
```

### Deployment (`-Area deployment`, scaffolded)

| Command | Description |
|---------|-------------|
| `build-ui` | Same one-shot Management UI CSS compile as the dev command. |
| `status`, `restore`, `build`, `publish`, `deploy` | Placeholder workflows for future packaging/deploy targets. |

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
