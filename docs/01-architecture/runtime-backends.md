# Runtime Backends

See [ADR-0003](../02-decisions/ADR-0003-runtime-portability.md) and [.ai/playbooks/runtime-portability.md](../../.ai/playbooks/runtime-portability.md).

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

`Apperture.Runtime.DTFx` and `Apperture.Runtime.DurableFunctions` are adapters. They map the portable workflow model to each backend. The portable model lives in `Apperture.Runtime.Abstractions` and `Apperture.Runtime.Engine`.
