# Scripts

PowerShell scripts for development, CI, and tooling. Run from the repo root or from within `scripts/`; paths are resolved via `$PSScriptRoot`. You can set `APPERTURE_REPO_ROOT` to override the repo root.

## Quick start (dev)

From repo root:

```powershell
.\scripts\Start-Dev.ps1
```

Or from `scripts/`:

```powershell
.\Start-Dev.ps1
```

This starts the **Management API** at http://localhost:9000 and the **Management UI** at http://localhost:9001 in separate console windows. Stop them by closing those windows or running `.\scripts\dev\down.ps1`.

---

## Dev (`dev/`)

| Script        | Description |
|---------------|-------------|
| **up.ps1**    | Start the platform in dev mode (API + UI Server). Use `-NoBrowser` to avoid launching the browser. |
| **down.ps1**  | Stop processes on ports 9000 and 9001 (dev servers). |
| **logs.ps1**  | Show which processes are listening on dev ports. Log output appears in the windows started by `up.ps1`. |
| **seed.ps1**  | Placeholder: seed dev database (implement when persistence is added). |
| **reset-db.ps1** | Placeholder: reset dev database (implement when EF Core is configured). |

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
- **PowerShell** 5.1 or PowerShell Core (Windows or cross-platform)
