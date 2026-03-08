# Contributing to Apperture Core

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (version in [global.json](global.json))
- Git

## Build and test

From the repo root:

```powershell
.\scripts\ci\build.ps1
.\scripts\ci\test.ps1
```

Or use `dotnet build Apperture.slnx` and `dotnet test Apperture.slnx`. Use `.\scripts\ci\test.ps1 -Filter "UnitTests"` to run a subset of tests by name.

## Local dev with SQL Server

See [deploy/README.md](deploy/README.md). Quick setup:

1. `dotnet user-secrets set "SqlServer:Password" "<password>" --project src/Apperture.ControlPlane/Apperture.Management.Host`
2. Copy `appsettings.Development.example.json` to `appsettings.Development.json` in the Host folder.
3. `.\scripts\Start-Dev.ps1`

## Architecture and decisions

- Read [docs/01-architecture/](docs/01-architecture/) for control plane, instance plane, workflows, and runtime.
- Record significant decisions in [docs/02-decisions/](docs/02-decisions/) using the [ADR template](docs/02-decisions/adr-template.md).

## AI agents

Follow [AGENTS.md](AGENTS.md) and the playbooks in [.ai/playbooks/](.ai/playbooks/) (principles, anti-patterns, project map).

## Pull requests

CI must pass before merge. The pipeline runs on push and pull requests to `main` (see [.github/workflows/ci.yml](.github/workflows/ci.yml)): restore, build in Release, and run all tests.
