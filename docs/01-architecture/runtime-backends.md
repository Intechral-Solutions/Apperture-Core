# Runtime Backends

See [ADR-0003](../02-decisions/ADR-0003-runtime-portability.md) and [.ai/playbooks/runtime-portability.md](../../.ai/playbooks/runtime-portability.md).

**Today vs adapters:** The on-prem worker **hot path** is SQL enqueue → worker claim → **inline** execution (`IWorkflowExecutor`), not DTFx orchestration. The sections below describe the **portability adapters** (DTFx and Durable Functions) that map the portable workflow model when those backends are used. See [Epic 73 ADR](../02-decisions/Epic73-execution-hot-path-and-host-roles.md).

## On-prem: DTFx

- Durable Task Framework
- Worker Service host
- SQL Server persistence
- One module per worker process

## Cloud: Durable Functions

- Azure Durable Functions
- One module per Function App
- Azure Storage backing store
- Consistent telemetry/audit to instance store

## Adapter strategy

`Apperture.RuntimePlane.DTFx` and `Apperture.RuntimePlane.DurableFunctions` are adapters. They map the portable workflow model to each backend. The portable model lives in `Apperture.RuntimePlane.Abstractions` and `Apperture.RuntimePlane.Engine`.
