# Runtime Portability (DTFx vs Durable Functions)

## Sequencing: on-prem first, then Azure

Azure hosting is built **after** the Windows/on-prem runtime is established and working. The DTFx path must be solid end-to-end before implementing the Durable Functions adapter. Implement clean interfaces and encapsulation from the start so the cloud adapter can plug in correctly without refactoring.

## Portable workflow model

Apperture defines a backend-agnostic workflow model:

- Typed parameter schema
- Step graph (linear sequence first)
- Operational attributes (retry, idempotency, permissions)

This model must be implementable by both on-prem and cloud adapters.

## On-prem: DTFx adapter

- **Apperture.Runtime.DTFx** — Uses DTFx for durable orchestration
- Hosted in Worker Service
- SQL Server is the persistence store (first-class)
- Worker runs polling/execution loops as needed
- One module per worker process

## Cloud: Durable Functions adapter

- **Apperture.Runtime.DurableFunctions** — Maps workflow definitions to Durable Functions orchestrators + activity functions
- Each module maps to one Azure Function App
- Persistence is Durable Functions backing store (Azure Storage by default)
- Apperture still emits consistent telemetry/audit events to the instance database or a telemetry sink

## Consistent observability

Regardless of backend:

- `WorkflowInstance`, `WorkflowStepInstance`, `OperationRun`, `AuditLog`, `TelemetryEvent` — design for consistent emission
- Cloud may keep durable state in Azure Storage but must emit to instance/telemetry store
