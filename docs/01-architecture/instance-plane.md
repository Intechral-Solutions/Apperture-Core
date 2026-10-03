# Instance Plane

The instance plane executes workflows. See [ADR-0001](../02-decisions/ADR-0001-runtime-boundaries.md).

Folder layout: `Apperture.RuntimePlane` (shared engine), `Apperture.RuntimeHost` (Windows and Azure hosts).

## On-prem (RuntimeHost.Windows)

- **WorkerHost** — **Current:** SQL-backed durable enqueue (`IWorkflowStartDispatchService` / dispatch stack), claim of start requests, and **inline** step execution via `IWorkflowExecutor` / engine. **Future:** DTFx adapter when wired for orchestration (see [Epic 73 ADR](../02-decisions/Epic73-execution-hot-path-and-host-roles.md)).
- **ModuleHost** — Stub today. Intended home of a one-module-per-process boundary, which is a supported future deployment option and not a current invariant (see [rc1-program-gate.md](rc1-program-gate.md), D-23)
- **InstanceAgent** — Local instance management endpoint/service
- **SchedulerHost** — Stub in current deployment; schedule dispatch runs in WorkerHost via `IScheduleDispatchRunner`

SQL Server is the shared persistence store for platform and runtime state today. DTFx/Durable Functions adapters are portability targets, not the current worker hot path.

## Cloud (RuntimeHost.Azure)

- **Template** — One Function App per module
- Each module provides Durable orchestration functions and activity functions
- Durable state in Azure Storage; telemetry/audit to instance store

## Boundary

The instance plane does not expose management APIs. It receives execution requests from the control plane and reports status/telemetry.
