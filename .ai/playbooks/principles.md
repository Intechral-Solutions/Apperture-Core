# Principles

## 1. Workflow-centric orchestration

Orchestration is explicit and workflow-based. Workflows have definitions, step graphs, and persisted state. Runtime services execute workflows; they do not route messages or dispatch handlers.

## 2. Single-tenant Instance boundary

Each deployment is one Instance (one customer per deployment). Isolation and compliance are simplified. Everything is auditable per instance. Design tables so adding TenantId later would be straightforward.

## 3. One module per compute process

- **On-prem:** One module per worker process (service/container).
- **Cloud:** One module per Azure Function App.

Modules are trusted; no sandboxing. This is a core isolation boundary for reliability and deployment.

## 4. Current execution path vs durable runtime portability

**Current reality (on-prem today):** Workflow and operation work is **durably enqueued in SQL** (`IWorkflowStartDispatchService` / related dispatch), **claimed by the worker**, and **executed inline** via `IWorkflowExecutor` and the runtime engine against shared SQL persistence. This is the hot path; it is not DTFx orchestration.

**Portability direction:** The workflow definition model stays backend-agnostic. **Adapters** for DTFx (on-prem) and Azure Durable Functions (cloud) are **future-facing**; they map the portable model when those backends are wired for orchestration. Management Host may register DTFx-related services for non-hot-path scenarios while the worker remains SQL enqueue + claim + inline execution until that changes.

## 5. Scheduling triggers workflows only

Scheduling is behind an abstraction. When a schedule fires, the host enqueues a durable workflow start (today: `IWorkflowStartDispatchService` and related SQL dispatch; idempotency keys such as `ScheduleId:FireTimeUtc`). The scheduling path never executes workflow steps directly.
