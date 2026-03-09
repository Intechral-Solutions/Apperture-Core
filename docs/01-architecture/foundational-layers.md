# Foundational Layers

This document summarizes the Shared Kernel, Core.Domain, and Core.Application layers and their dependency rules.

## Layer boundaries

| Layer | Purpose | Dependencies |
|-------|---------|--------------|
| **Shared Kernel** | Minimal, stable, dependency-free primitives: identifiers, result/error, actor/principal, environment context, resource/permission descriptors, execution context, clock, correlation, audit sink, module capability descriptor. | None. |
| **Core.Domain** | Rich platform business model: Environment, Runtime Plane, Module, Module Installation, Module Instance, User, Role, Identity, ServiceAccount, Operation, Execution, Schedule, Trust, Audit. | Shared Kernel only. |
| **Core.Application** | Service contracts and orchestration boundaries: identity resolution, authorization, role provider, module registry, operation registry, schedule management, environment trust, audit, execution dispatch, resource descriptor registry. | Core.Domain, Shared Kernel; optionally RuntimePlane.Abstractions, Core.Security.Secrets. |

## Dependency rules

- `Apperture.Core.Domain` depends on `Apperture.Shared.Kernel` only.
- `Apperture.Core.Application` depends on `Apperture.Core.Domain` and `Apperture.Shared.Kernel`; it may also reference `Apperture.RuntimePlane.Abstractions` and `Apperture.Core.Security.Secrets`.
- `Apperture.Shared.Kernel` must not depend on Core.Domain or Core.Application.
- Infrastructure and hosts (Management.Host, Worker hosts, Scheduler Host) reference Core.Application and wire implementations.

## Bounded contexts (domain partitions)

- **Identity and Access** — User, IdentityLink, Role, Permission, Resource, ServiceAccount; break-glass as explicit flag and audit.
- **Environment Management** — Environment, RuntimePlane, Module, ModuleInstallation, ModuleInstance.
- **Module Registry / Lifecycle** — Module (catalog), ModuleInstallation, ModuleInstance; capability descriptors.
- **Operations / Execution** — OperationDefinition, ExecutionRecord; ties to RuntimePlane.Abstractions (WorkflowDefinition ↔ OperationDefinition mapping).
- **Scheduling** — ScheduleDefinition, ScheduleAssignment, ScheduledTarget, execution policy value objects.
- **Secrets / Protected Config** — Secret lifecycle in Core.Security.Secrets; domain objects carry SecretRef; resolution behind interfaces.
- **Auditing** — AuditRecord (Core.Domain); IAuditEventSink (Kernel); IAuditService (Core.Application).
- **Trust / Remote** — TrustRelationship, GrantedCapability, RemoteEnvironment.

## Project structure

```
src/Apperture.Core/
├── Apperture.Core.Domain/       # Environment, Identity, Operations, Scheduling, Trust, Audit
├── Apperture.Core.Application/  # Service interfaces (Services/*)
└── Apperture.Core.Security.Secrets/  # ISecretStore, ISecretResolver, secret models
```

## Terminology

- **Environment** — Full deployed platform boundary for a tenant or organization. (Replaces the overloaded use of "platform instance" for this concept.)
- **Runtime Plane** — Execution/data-plane subsystem within an Environment.
- **Module** — Installable capability type (catalog entry).
- **Module Installation** — A module enabled within an Environment.
- **Module Instance** — A configured executable occurrence of a Module Installation (e.g. one worker process or one Function App).

Do not use the bare word "instance" ambiguously; always qualify (e.g. Module Instance, workflow instance).

## Related

- [Secrets and key management](secrets-and-key-management.md)
- [Instance-owned scheduling](instance-owned-scheduling.md)
- [Federated instance relationships](federated-instance-relationships.md)
- [.ai/playbooks/project-map.md](../../.ai/playbooks/project-map.md)
