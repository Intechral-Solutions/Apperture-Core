# Runtime Portability (DTFx vs Durable Functions)

**Current worker reality:** On-prem execution today uses **SQL-backed dispatch and inline step execution** (`IWorkflowExecutor`), not DTFx orchestration. DTFx and Durable Functions remain the **target adapters** for portable orchestration. See [Epic 73 ADR](../../docs/02-decisions/Epic73-execution-hot-path-and-host-roles.md).

## Sequencing: on-prem first, then Azure

Azure hosting is built **after** the Windows/on-prem runtime is established and working. The DTFx path must be solid end-to-end before implementing the Durable Functions adapter. Implement clean interfaces and encapsulation from the start so the cloud adapter can plug in correctly without refactoring.

## Portable workflow model

Apperture defines a backend-agnostic workflow model:

- Typed parameter schema
- Step graph (linear sequence first)
- Operational attributes (retry, idempotency, permissions)

This model must be implementable by both on-prem and cloud adapters.

## On-prem: DTFx adapter (design intent, not implemented)

Status today: `Apperture.RuntimePlane.DTFx` registers a stub (`IDurableTaskRuntimeAdapter`) and nothing executes workflows through it. The execution path is SQL-backed dispatch and inline execution in the worker (see the top of this page). What follows is the intended shape, not a description of the current system.

- **Apperture.RuntimePlane.DTFx** — intended to use DTFx for durable orchestration
- Intended to be hosted in the Worker Service
- SQL Server is the persistence store (first-class; this is already true for the current path)
- Worker runs polling/execution loops as needed (true today for the SQL claim loop)
- One worker process may host several modules today; one module per worker process is a supported future deployment option, not a current invariant

## Cloud: Durable Functions adapter (design intent, not implemented; outside RC1 support)

Status today: `Apperture.RuntimePlane.DurableFunctions` is a stub whose members throw `NotImplementedException`. The Azure host projects are scaffold only. What follows is the intended shape.

- **Apperture.RuntimePlane.DurableFunctions** — intended to map workflow definitions to Durable Functions orchestrators + activity functions
- Intended model: each module maps to one Azure Function App (a supported future deployment option, like one module per worker process on-prem)
- Persistence would be the Durable Functions backing store (Azure Storage by default)
- Apperture would still emit consistent telemetry/audit events to the instance database or a telemetry sink

## Consistent observability

Regardless of backend:

- `WorkflowInstance`, `WorkflowStepInstance`, `OperationRun`, `AuditLog`, `TelemetryEvent` — design for consistent emission
- Cloud may keep durable state in Azure Storage but must emit to instance/telemetry store
