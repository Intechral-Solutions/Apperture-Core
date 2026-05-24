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

See [deploy/README.md](deploy/README.md) and [management platform configuration](docs/03-operations/management-platform-configuration.md).

### Preferred: .NET Aspire

Requires Docker for the Aspire-managed SQL Server container.

1. Copy `deploy/config/examples/appsettings.docker-dev.json.example` to `src/Apperture.ControlPlane/Apperture.Management.Host/appsettings.Development.json` (one-time; needed for `DevTestUser`, bootstrap admin, and dev feature flags).
2. Run the AppHost:

   ```powershell
   dotnet run --project src/Apperture.AppHost/Apperture.AppHost.csproj
   ```

   Starts SQL Server, Management API (:9000), Management UI (:9001), and WorkerHost. Opens the Aspire dashboard. [`Apperture.ServiceDefaults`](src/Apperture.ServiceDefaults/) adds conventional health, OpenTelemetry, and service discovery on orchestrated hosts — hosts still run without Aspire when started directly.

   Optional: override the SQL password with User Secrets on the AppHost (`dotnet user-secrets set "Parameters:sql-password" "<password>" --project src/Apperture.AppHost`).

**Epic 106 scope:** local dev orchestration only — not platform settings, bootstrap store, setup wizard, or production deployment.

### Alternate: PowerShell scripts

1. `dotnet user-secrets set "SqlServer:Password" "<password>" --project src/Apperture.ControlPlane/Apperture.Management.Host`
2. Copy `deploy/config/examples/appsettings.docker-dev.json.example` to `src/Apperture.ControlPlane/Apperture.Management.Host/appsettings.Development.json`
3. `.\scripts\Start-Dev.ps1`

Do not run Aspire and `Start-Dev.ps1` at the same time — both use ports 1433, 9000, and 9001.

## Architecture and decisions

- Read [docs/01-architecture/](docs/01-architecture/) for control plane, instance plane, workflows, and runtime.
- Record significant decisions in [docs/02-decisions/](docs/02-decisions/) using the [ADR template](docs/02-decisions/adr-template.md).

## AI agents

Follow [AGENTS.md](AGENTS.md) and the playbooks in [.ai/playbooks/](.ai/playbooks/) (principles, anti-patterns, project map).

## Pull requests

CI must pass before merge. The pipeline runs on push and pull requests to `main` (see [.github/workflows/ci.yml](.github/workflows/ci.yml)): restore, build in Release, and run all tests.
