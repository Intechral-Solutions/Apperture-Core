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

## Architecture and decisions

- Read [docs/01-architecture/](docs/01-architecture/) for control plane, instance plane, workflows, and runtime.
- Record significant decisions in [docs/02-decisions/](docs/02-decisions/) using the [ADR template](docs/02-decisions/adr-template.md).

## AI agents

Follow [agent.md](agent.md) and the playbooks in [.ai/playbooks/](.ai/playbooks/) (principles, anti-patterns, project map).

## Pull requests

CI must pass before merge. The pipeline runs on push and pull requests to `main` (see [.github/workflows/ci.yml](.github/workflows/ci.yml)): restore, build in Release, and run all tests.
