# Project Map

Canonical reference for repository structure and project responsibilities.

## Repository structure

```
Apperture-Core/
├─ src/
│  ├─ Apperture.Shared/          (Kernel, Security, Observability, Serialization)
│  ├─ Apperture.Core/            (Domain, Application, Security.Secrets)
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
| Core | Domain, Application, Security.Secrets | Domain model (Environment, Module, User, Role, etc.); application service contracts; secret store/resolver abstractions |
| Runtime plane | Abstractions, Engine, Persistence.SqlServer, DTFx, DurableFunctions | Portable workflow model, backend-agnostic logic, adapters |
| Runtime host (Windows) | WorkerHost, ModuleHost, InstanceAgent, SchedulerHost | On-prem execution, one module per process, local mgmt, schedule triggers |
| Runtime host (Azure) | Template, Shared | One module per Function App scaffolding |
| Module SDK | Abstractions, SDK, Testing | Module interfaces, helpers, test harness |
| Control plane | Management Api, Host, Client, UI | API, composition, typed client, Blazor WASM |

## Dependency rules

- **Kernel has no Core dependencies** — Shared.Kernel must not depend on Core.Domain or Core.Application.
- **Core.Domain depends only on Kernel** — No Core.Application, RuntimePlane, ControlPlane, or Security.Secrets.
- **Core.Application** — Depends on Core.Domain, Kernel; may reference RuntimePlane.Abstractions and Core.Security.Secrets. Implementations live in infrastructure and hosts.
- **Abstractions have no runtime dependencies** — Only interfaces and models.
- **Engine depends only on Abstractions** — No DTFx, no Durable Functions.
- **Hosts compose everything** — WorkerHost, Management.Host, SchedulerHost wire up adapters.
- **Modules reference ModuleSdk** — Never reference RuntimePlane or ControlPlane directly.
