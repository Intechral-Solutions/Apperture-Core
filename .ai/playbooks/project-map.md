# Project Map

Canonical reference for repository structure and project responsibilities.

## Repository structure

```
Apperture-Core/
├─ src/
│  ├─ Apperture.Shared/          (Kernel, Security, Observability, Serialization)
│  ├─ Apperture.RuntimePlane/    (Abstractions, Engine, Persistence.SqlServer, DTFx, DurableFunctions)
│  ├─ Apperture.RuntimeHost/     (Windows: WorkerHost, ModuleHost, InstanceAgent, SchedulerHost | Azure: Template, Shared)
│  ├─ Apperture.ModuleSdk/       (Abstractions, SDK, Testing)
│  ├─ Apperture.ControlPlane/    (Management Api, Host, Client, UI)
│  ├─ Apperture.Cli/             (PowerShell, Cli.Core)
│  └─ Apperture.Modules/         (DEV: Sample.HelloWorld, etc.)
├─ tests/
├─ deploy/
├─ scripts/
└─ tools/
```

## Project responsibilities

| Area | Projects | Responsibility |
|------|----------|----------------|
| Shared | Kernel, Security, Observability, Serialization | IDs, Result types, errors, time, cross-cutting concerns |
| Runtime plane | Abstractions, Engine, Persistence.SqlServer, DTFx, DurableFunctions | Portable workflow model, backend-agnostic logic, adapters |
| Runtime host (Windows) | WorkerHost, ModuleHost, InstanceAgent, SchedulerHost | On-prem execution, one module per process, local mgmt, schedule triggers |
| Runtime host (Azure) | Template, Shared | One module per Function App scaffolding |
| Module SDK | Abstractions, SDK, Testing | Module interfaces, helpers, test harness |
| Control plane | Management Api, Host, Client, UI | API, composition, typed client, Blazor WASM |

## Dependency rules

- **Abstractions have no runtime dependencies** — Only interfaces and models.
- **Engine depends only on Abstractions** — No DTFx, no Durable Functions.
- **Hosts compose everything** — WorkerHost, Management.Host, SchedulerHost wire up adapters.
- **Modules reference ModuleSdk** — Never reference RuntimePlane or ControlPlane directly.
