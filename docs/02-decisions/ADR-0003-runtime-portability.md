# ADR-0003: Runtime portability (portable workflow model, DTFx + Durable Functions adapters)

## Status

Accepted

## Context

Apperture must run on-prem (DTFx + SQL Server) and in the cloud (Azure Durable Functions). We need a single workflow model that maps to both backends without coupling core semantics to either.

## Decision

1. **Portable workflow model** — Workflow definitions, step graphs, and operational attributes are backend-agnostic. They live in `Apperture.RuntimePlane.Abstractions` and `Apperture.RuntimePlane.Engine`.

2. **Adapters** — `Apperture.RuntimePlane.DTFx` and `Apperture.RuntimePlane.DurableFunctions` implement the portable model for each backend. They handle persistence, orchestration mechanics, and execution loops.

3. **Consistent observability** — Both backends emit consistent `WorkflowInstance`, `WorkflowStepInstance`, `OperationRun`, `AuditLog`, `TelemetryEvent` records. Cloud may store durable state in Azure Storage but must emit to instance/telemetry store.

## Consequences

### Positive

- Same workflow semantics on-prem and cloud
- Backend can be swapped without changing workflow definitions
- Consistent telemetry across deployments

### Negative

- Adapter layer adds indirection
- Some backend-specific optimizations may be harder to exploit

### Neutral

- Cloud adapter can be stubbed initially; full implementation follows
