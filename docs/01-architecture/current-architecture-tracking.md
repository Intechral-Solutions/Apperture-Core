# Current Architecture — Tracking Document

> **Document purpose:** This is a **tracking document for current progress**, not a roadmap or specification of the ideal state. It reflects the architecture as it exists today—what is implemented, what is stubbed, and how subsystems connect. Update this document as the codebase evolves.

---

## High-Level Architecture

```mermaid
flowchart TB
    subgraph External["External Actors"]
        Operator[Operator / Admin]
        Module[Module Process]
    end

    subgraph ControlPlane["Control Plane"]
        direction TB
        UI[Management UI<br/>Blazor WASM]
        API[Management API<br/>ASP.NET Core REST]
        Client[Management Client<br/>Typed HTTP client]
        UI --> API
        Client --> API
    end

    subgraph InstancePlane["Instance Plane (Execution)"]
        direction TB
        subgraph WindowsHost["RuntimeHost.Windows"]
            WorkerHost[WorkerHost]
            ModuleHost[ModuleHost]
            SchedulerHost[SchedulerHost]
            InstanceAgent[InstanceAgent]
        end
        subgraph AzureHost["RuntimeHost.Azure"]
            FuncApp[Function App Template]
        end
    end

    subgraph Foundation["Foundation & Shared"]
        direction TB
        Bootstrap[Bootstrap]
        Persistence[(Persistence Foundation<br/>SQL Server)]
        Secrets[Operational Secrets]
        RuntimeTrust[Runtime Trust]
    end

    subgraph Shared["Shared / Core"]
        Kernel[Shared.Kernel]
        Domain[Core.Domain]
        Application[Core.Application]
    end

    Operator --> UI
    Operator --> CLI
    CLI[CLI / Bootstrap CLI] --> Bootstrap
    Operator --> Client

    API --> IWorkflowRuntime
    API --> Persistence
    API --> Secrets
    API --> RuntimeTrust

    IWorkflowRuntime[IWorkflowRuntime] -.->|Stub runtime execution| InstancePlane

    WorkerHost --> Persistence
    WorkerHost --> Secrets
    WorkerHost --> Bootstrap
    ModuleHost --> WorkerHost
    SchedulerHost --> IWorkflowStarter
    InstanceAgent --> Persistence

    Bootstrap --> Persistence
    Secrets --> Persistence
    RuntimeTrust --> Persistence

    Application --> Domain
    Application --> Kernel
    Domain --> Kernel
```

---

## Subsystem Overview

| Subsystem | Status | Notes |
|-----------|--------|-------|
| **Shared Kernel** | Implemented | IDs, Result, ActorId, environment context, audit sink |
| **Core.Domain** | Implemented | Environment, Module, User, Role, Operation, Schedule, Trust |
| **Core.Application** | Implemented | Service contracts; orchestration boundaries |
| **Bootstrap** | Stubbed | Config store, credential manager, profile manager all stubs |
| **Persistence Foundation** | Implemented | Shared DbContext, migrations, retry, schema |
| **Operational Secrets** | Partially | SqlSecretStore/Resolver done; Azure KV resolver done; KV store placeholder |
| **Runtime Trust** | Partially | EnrollController, SqlRuntimeIdentityService (stub validation) |
| **Runtime Plane** | Abstractions + Stubs | Engine exists; DTFx/DurableFunctions adapters stubbed |
| **Control Plane** | Partially | API, Menus, Modules, Runs, Workflows (start persists handoff; execution deferred), Enroll; UI shell |
| **Instance Plane** | Stubbed | WorkerHost/ModuleHost/SchedulerHost stubs; Azure template has HTTP stub |
| **CLI** | Partially | Bootstrap CLI scaffolding; PowerShell; invokes stubs |
| **Module SDK** | Implemented | Abstractions, SDK, test harness (stub) |

---

## Control Plane (Detail)

```mermaid
flowchart LR
    subgraph ControlPlane["Control Plane"]
        direction TB
        MenusController["MenusController<br/>GET /api/menus"]
        ModulesController["ModulesController<br/>GET /api/modules"]
        RunsController["RunsController<br/>GET /api/runs"]
        WorkflowsController["WorkflowsController<br/>POST /workflows/{id}/start"]
        EnrollController["EnrollController<br/>POST /enroll"]
        
        MenusController --> |200 OK| DomainMenus
        ModulesController --> |200 OK| Empty/stub
        RunsController --> |200/404/403| SQL-backed runs
        WorkflowsController --> |202/404/403| Persisted start handoff
        EnrollController --> IRuntimeEnrollmentService
    end

    WorkflowsController --> OrchestrationSafeValidator
    WorkflowsController -.->|Not wired| IWorkflowRuntime
```

**Implemented:**
- Menus API for Control Panel navigation
- Modules, Runs return empty/stub data
- WorkflowsController: parameter validation (rejects secret-bearing params), returns 501
- EnrollController: enrollment endpoint

**Not implemented:**
- Workflow start not wired to runtime
- Signal endpoint
- Schedule management API
- Audit/telemetry browse API

---

## Bootstrap (Detail)

```mermaid
flowchart TB
    subgraph CLI["CLI Layer"]
        BootstrapCliService
        PowerShell[PowerShell functions]
    end

    subgraph Bootstrap["Bootstrap Subsystem"]
        direction TB
        StubConfig[StubBootstrapConfigurationStore<br/>GetAsync→null, SaveAsync→throws]
        StubCredMgr[StubBootstrapCredentialManager<br/>ValidateAsync→failure, Update/Rotate→throws]
        StubProfile[StubSqlConnectionBootstrapProfileManager<br/>GetProfileAsync→null, Update→throws]
        StubResolver[StubBootstrapCredentialResolver<br/>returns null]
        StubRecovery[StubBootstrapRecoveryAuthorizer<br/>denies all]
    end

    BootstrapCliService --> StubConfig
    BootstrapCliService --> StubCredMgr
    BootstrapCliService --> StubProfile
    PowerShell --> |invokes apperture helper| BootstrapCliService
```

**Implemented:**
- bootstrap show / validate succeed with "no configuration"
- All bootstrap interfaces defined; stubs provide non-throwing read paths where applicable

**Stubbed:**
- Config persistence, credential resolution, profile persistence, recovery authorization

---

## Operational Secrets (Detail)

```mermaid
flowchart TB
    subgraph ControlPlane["Control Plane / Windows Host"]
        ISecretStore
        ISecretResolver
    end

    subgraph Implemented["Implemented"]
        SqlSecretStore[SqlSecretStore<br/>metadata, lifecycle, audit]
        SqlSecretResolver[SqlSecretResolver<br/>GUID refs → DB]
        DpapiProtector[DpapiSecretProtector<br/>Windows only]
        CompositeResolver[CompositeSecretResolver<br/>routes by ref format]
    end

    subgraph Azure["Azure Runtime"]
        AzureKVResolver[AzureKeyVaultSecretResolver<br/>kv: or URI refs]
    end

    subgraph Placeholders["Placeholders"]
        AzureKVStore[AzureKeyVaultSecretStore]
        VaultStore[VaultSecretStore]
        CertProtector[CertSecretProtector]
    end

    ISecretStore --> SqlSecretStore
    ISecretStore -.-> AzureKVStore
    ISecretResolver --> CompositeResolver
    CompositeResolver --> SqlSecretResolver
    CompositeResolver --> AzureKVResolver
    SqlSecretStore --> DpapiProtector
```

---

## Runtime Plane (Detail)

```mermaid
flowchart TB
    subgraph Abstractions["RuntimePlane.Abstractions"]
        IWorkflowRuntime
        IWorkflowStarter
        StartWorkflowRequest
        WorkflowDefinitionId
        OrchestrationSafeValidator
    end

    subgraph Engine["RuntimePlane.Engine"]
        EngineLogic[Backend-agnostic logic]
    end

    subgraph DTFx["RuntimePlane.DTFx"]
        DurableTaskAdapter[DurableTaskRuntimeAdapterStub]
    end

    subgraph DurableFunctions["RuntimePlane.DurableFunctions"]
        DFRuntimeStub[DurableFunctionsRuntimeStub<br/>all methods throw]
    end

    subgraph Persistence["RuntimePlane.Persistence.SqlServer"]
        WorkflowInstances[WorkflowInstances]
        WorkflowStepInstances[WorkflowStepInstances]
        OperationRuns[OperationRuns]
    end

    IWorkflowRuntime --> DFRuntimeStub
    IWorkflowRuntime -.->|future| DTFx
    DurableTaskAdapter -.->|empty stub| DTFx
```

**Implemented:**
- Portable model (StartWorkflowRequest, WorkflowDefinitionId, etc.)
- OrchestrationSafeValidator (parameter/signal validation)
- DbContext entities for workflow/step/operation state

**Stubbed:**
- DTFx adapter
- Durable Functions adapter (all IWorkflowRuntime methods throw)

---

## Instance Plane / Runtime Hosts (Detail)

```mermaid
flowchart TB
    subgraph WindowsHost["RuntimeHost.Windows"]
        WorkerHost[WorkerHost<br/>WorkerStub: ISecretResolver demo]
        ModuleHost[ModuleHostStub]
        SchedulerHost[SchedulerHostStub<br/>BackgroundService]
        InstanceAgent[InstanceAgent]
    end

    subgraph AzureHost["RuntimeHost.Azure"]
        Template[Template project]
        HttpTriggerStub[HttpTriggerStub]
        AzureSecrets[AddAppertureAzureKeyVaultSecrets]
    end

    subgraph Dependencies["Host Dependencies"]
        Bootstrap
        Persistence
        Secrets
    end

    WorkerHost --> Dependencies
    SchedulerHost -.->|would call| IWorkflowStarter
    Template --> HttpTriggerStub
    Template --> AzureSecrets
```

---

## Persistence Foundation (Detail)

```mermaid
erDiagram
    AppertureDbContext {
        WorkflowInstances
        WorkflowStepInstances
        OperationRuns
        AuditLogs
        TelemetryEvents
        SecretDescriptors
        SecretVersions
        SecretAudits
        EnrollmentTokens
        RuntimeIdentities
    }

    WorkflowInstances ||--o{ WorkflowStepInstances : contains
    SecretDescriptors ||--o{ SecretVersions : has
```

**Implemented:**
- Shared DbContext with all tables
- Migrations, retry/resilience, connection string factory
- Bootstrap provides connection string; persistence does not read config

---

## Dependency Flow

```mermaid
flowchart TD
    Kernel[Shared.Kernel]
    Domain[Core.Domain]
    Application[Core.Application]
    SecretsAbstractions[Core.Security.Secrets]
    RuntimeAbstractions[RuntimePlane.Abstractions]

    Domain --> Kernel
    Application --> Domain
    Application --> Kernel
    Application --> SecretsAbstractions
    Application --> RuntimeAbstractions

    Bootstrap[Bootstrap] --> Persistence
    SecretsImpl[Secrets impl] --> Persistence
    RuntimeTrust[Runtime Trust] --> Persistence
    Persistence[Persistence] --> SecretsAbstractions

    ControlPlane[Control Plane] --> Application
    ControlPlane --> RuntimeAbstractions
    ControlPlane --> Persistence

    InstancePlane[Instance Plane] --> RuntimeAbstractions
    InstancePlane --> Persistence
    InstancePlane --> Bootstrap
```

---

## Project Map (Current)

```
src/
├── Apperture.Shared/           Kernel, Security, Observability, Serialization
├── Apperture.Core/             Domain, Application, Security.Secrets, Security.Trust
├── Apperture.Bootstrap/        Stubs only; scaffolding for config/credential/profile
├── Apperture.Persistence/      SqlServer (shared DbContext)
├── Apperture.RuntimePlane/     Abstractions, Engine, Persistence.SqlServer, DTFx, DurableFunctions
├── Apperture.RuntimeHost/      Windows (WorkerHost, ModuleHost, InstanceAgent, SchedulerHost), Azure (Template, Secrets)
├── Apperture.ModuleSdk/        Abstractions, SDK, Testing
├── Apperture.ControlPlane/     Management.Api, Host, Client, UI (Blazor), Security
├── Apperture.Cli/              PowerShell, Cli.Core, Cli.Bootstrap, Cli.Host
└── Apperture.Modules/          Sample.HelloWorld (module, UI, contracts)
```

---

## Related

- [foundational-layers.md](foundational-layers.md) — Layer boundaries and dependency rules
- [bootstrap-and-secrets-boundary.md](bootstrap-and-secrets-boundary.md) — Bootstrap vs operational secrets
- [control-plane.md](control-plane.md) — Management API responsibilities
- [instance-plane.md](instance-plane.md) — Runtime host layout
- [persistence-foundation.md](persistence-foundation.md) — Schema and registration
- [secrets-and-key-management.md](secrets-and-key-management.md) — Secret subsystem
- [bootstrap-cli-invocation.md](bootstrap-cli-invocation.md) — Deferred implementation table
