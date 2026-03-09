# Instance Plane

The instance plane executes workflows. See [ADR-0001](../02-decisions/ADR-0001-runtime-boundaries.md).

Folder layout: `Apperture.RuntimePlane` (shared engine), `Apperture.RuntimeHost` (Windows and Azure hosts).

## On-prem (RuntimeHost.Windows)

- **WorkerHost** — Hosts the DTFx runtime adapter
- **ModuleHost** — One module per process boundary
- **InstanceAgent** — Local instance management endpoint/service
- **SchedulerHost** — Triggers workflow starts only (optional dedicated service)

SQL Server is the persistence store. DTFx coordinates durable orchestration.

## Cloud (RuntimeHost.Azure)

- **Template** — One Function App per module
- Each module provides Durable orchestration functions and activity functions
- Durable state in Azure Storage; telemetry/audit to instance store

## Boundary

The instance plane does not expose management APIs. It receives execution requests from the control plane and reports status/telemetry.
