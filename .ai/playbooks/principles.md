# Principles

## 1. Workflow-centric orchestration

Orchestration is explicit and workflow-based. Workflows have definitions, step graphs, and persisted state. Runtime services execute workflows; they do not route messages or dispatch handlers.

## 2. Single-tenant Instance boundary

Each deployment is one Instance (one customer per deployment). Isolation and compliance are simplified. Everything is auditable per instance. Design tables so adding TenantId later would be straightforward.

## 3. One module per compute process

- **On-prem:** One module per worker process (service/container).
- **Cloud:** One module per Azure Function App.

Modules are trusted; no sandboxing. This is a core isolation boundary for reliability and deployment.

## 4. Durable runtime portability

Apperture Core supports two execution backends:

- **On-prem:** DTFx in a Worker Service; SQL Server persistence.
- **Cloud:** Azure Durable Functions; one Function App per module; Durable Functions backing store + consistent telemetry/audit to instance store.

The workflow model is portable. Adapters (DTFx, DurableFunctions) map the portable model to each backend.

## 5. Scheduling triggers workflows only

Scheduling is behind an abstraction. When a schedule fires, the host enqueues a durable workflow start (today: `IWorkflowStartDispatchService` and related SQL dispatch; idempotency keys such as `ScheduleId:FireTimeUtc`). The scheduling path never executes workflow steps directly.
