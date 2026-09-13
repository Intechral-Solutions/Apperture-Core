# Contributing to Apperture Core

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (version in [global.json](global.json))
- [Docker](https://www.docker.com/) (local SQL Server for Aspire or script-based dev)
- Git

Aspire projects (`Apperture.AppHost`, `Apperture.ServiceDefaults`) restore via NuGet (`Aspire.AppHost.Sdk`); contributors do **not** need a separate Aspire template or workload install to build or run the repo.

## Build and test

From the repo root:

```powershell
.\scripts\ci\build.ps1
.\scripts\ci\test.ps1
```

Or use `dotnet build Apperture.slnx` and `dotnet test Apperture.slnx`. Use `.\scripts\ci\test.ps1 -Filter "UnitTests"` to run a subset of tests by name.

## Local dev with SQL Server

Dev setup uses the **environment/startup configuration layer** only (appsettings templates, User Secrets, dev scripts). For the full four-layer model and what belongs where, see [ADR-0039](docs/02-decisions/ADR-0039-environment-startup-bootstrap-and-platform-settings.md).

See [deploy/README.md](deploy/README.md) and [management platform configuration](docs/03-operations/management-platform-configuration.md). Foundation smoke checklist: [Epic 110](docs/03-operations/epic110-foundation-stabilization.md).

### Preferred: .NET Aspire

Requires Docker for the Aspire-managed SQL Server container.

**One-time setup:** run `.\scripts\Initialize-AppertureEnvironment.ps1` or manually copy the docker-dev template (step 1 below).

1. Copy `deploy/config/examples/appsettings.docker-dev.json.example` to `src/Apperture.ControlPlane/Apperture.Management.Host/appsettings.Development.json` (one-time; needed for `DevTestUser`, bootstrap admin, and dev feature flags).
2. Run the AppHost:

   ```powershell
   dotnet run --project src/Apperture.AppHost/Apperture.AppHost.csproj
   ```

   Starts SQL Server, Management API (:9000), Management UI (:9001), and WorkerHost. Opens the Aspire dashboard. [`Apperture.ServiceDefaults`](src/Apperture.ServiceDefaults/) adds conventional health, OpenTelemetry, and service discovery on orchestrated hosts — hosts still run without Aspire when started directly.

   **WorkerHost lifecycle:** AppHost owns the worker process. Management UI observes health (Control Center → Worker runtime) and can run scheduler recovery — it does **not** start/stop/restart WorkerHost. Quarantined `runtime-worker/start` returns 410. See [Worker-Hosting-Guidance-v1.md](docs/03-operations/Worker-Hosting-Guidance-v1.md).

   **URLs (important):**
   - Management UI: `http://localhost:9001` (HTTP only — there is no HTTPS listener on 9001)
   - Management API: `http://localhost:9000`
   - Aspire dashboard: use the **login URL printed in the AppHost console** (for example `https://localhost:17173/login?t=...`). The bare dashboard origin may redirect or fail in the browser if the dev certificate is untrusted; prefer the tokenized login link. HTTP fallback is often `http://localhost:15235`.

   Stop any prior AppHost session before restarting (Ctrl+C, or `.\scripts\Manage.ps1 -Area dev -Command stop` plus stop lingering `dcp.exe` / WorkerHost processes) to avoid port conflicts and file-lock build errors.

   **SQL and login (Aspire vs scripts):** AppHost uses a **separate** persistent SQL volume (`apperture-aspire-sql`), not the docker-compose `apperture-sqlserver` volume. Users and passwords from the script path are **not** shared. Under AppHost, `ConnectionStrings:DefaultConnection` from Aspire wins over `SqlServer:` in `appsettings.Development.json` for Management.Host and WorkerHost. Copy `appsettings.Development.json` for `DevTestUser` / `BootstrapPlatformAdmin` only — local login uses the password from that file (template default `dev`) against the **Aspire** database after Management.Host migrations run. To reset, delete the Aspire SQL volume in Docker and restart AppHost.

   Optional: override the SQL password with User Secrets on the AppHost (`dotnet user-secrets set "Parameters:sql-password" "<password>" --project src/Apperture.AppHost`).

**Epic 106 scope:** local dev orchestration. **Epic 108** adds the file-backed bootstrap configuration store for Windows/on-prem production (`%ProgramData%\Apperture\bootstrap\configuration.json`); Aspire local dev does not require that file.

### Alternate: PowerShell scripts

1. `dotnet user-secrets set "SqlServer:Password" "<password>" --project src/Apperture.ControlPlane/Apperture.Management.Host`
2. Copy `deploy/config/examples/appsettings.docker-dev.json.example` to `src/Apperture.ControlPlane/Apperture.Management.Host/appsettings.Development.json`
3. Native dev (default mode):

   ```powershell
   .\scripts\Manage.ps1 -Area dev -Command start
   ```

   Or use the interactive menu: `.\scripts\Interactive.ps1`

Do not run Aspire and native script dev at the same time — both want Management API/UI on 9000/9001. Aspire SQL defaults to host port **14333**; docker-compose/native SQL uses **1433**.

## Architecture and decisions

- Read [docs/01-architecture/](docs/01-architecture/) for control plane, instance plane, workflows, and runtime.
- Record significant decisions in [docs/02-decisions/](docs/02-decisions/) using the [ADR template](docs/02-decisions/adr-template.md).

## AI agents

Follow [AGENTS.md](AGENTS.md) and the playbooks in [.ai/playbooks/](.ai/playbooks/) (principles, anti-patterns, project map).

**Runtime-visible naming:** Do not create integrations, seeds, notifications, settings, or UI labels that embed epic/track IDs (`Epic 121`, `SDK-5A`, `SWM-10`, `BDS-3`, etc.). Keep those in docs/ADRs/tests; use product-like names in operator-facing content. See anti-patterns → “Do not put internal development metadata in runtime-visible content”.

## Pull requests

CI must pass before merge. The pipeline runs on push and pull requests to `main` (see [.github/workflows/ci.yml](.github/workflows/ci.yml)): restore, build in Release, and run all tests.
