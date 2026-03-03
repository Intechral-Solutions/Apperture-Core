# Project Map

Canonical reference for repository structure and project responsibilities.

## Repository structure

```
Apperture-Core/
├─ src/
│  ├─ Apperture.Shared/          (Kernel, Security, Observability, Serialization)
│  ├─ Apperture.Runtime/         (Abstractions, Engine, Persistence.SqlServer, DTFx, DurableFunctions)
│  ├─ Apperture.ModuleSdk/       (Abstractions, SDK, Testing)
│  ├─ Apperture.ControlPlane/    (Management Api, Host, Client, UI)
│  ├─ Apperture.InstancePlane/   (Worker.Host, Worker.ModuleHost, Instance.Agent, Scheduler.Host)
│  ├─ Apperture.Cloud/           (FunctionApp.Template, Cloud.Shared)
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
| Runtime | Abstractions | Portable workflow model, IWorkflowRuntime, IWorkflowStarter, etc. |
| Runtime | Engine | Backend-agnostic runtime logic |
| Runtime | Persistence.SqlServer | Workflow/run/telemetry tables |
| Runtime | DTFx | On-prem DTFx adapter |
| Runtime | DurableFunctions | Cloud Durable Functions adapter |
| Module SDK | Abstractions, SDK, Testing | Module interfaces, helpers, test harness |
| Control plane | Management Api, Host, Client, UI | API, composition, typed client, Blazor WASM |
| Instance plane | Worker.Host, ModuleHost, Instance.Agent, Scheduler | On-prem execution, one module per process, local mgmt, schedule triggers |
| Cloud | FunctionApp.Template, Cloud.Shared | One module per Function App scaffolding |

## Dependency rules

- **Abstractions have no runtime dependencies** — Only interfaces and models.
- **Engine depends only on Abstractions** — No DTFx, no Durable Functions.
- **Hosts compose everything** — Worker.Host, Management.Host, Scheduler.Host wire up adapters.
- **Modules reference ModuleSdk** — Never reference Runtime or ControlPlane directly.
