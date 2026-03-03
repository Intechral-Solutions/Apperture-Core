# Instance Plane

The instance plane executes workflows. See [ADR-0001](../02-decisions/ADR-0001-runtime-boundaries.md).

## On-prem

- **Worker Host** — Hosts the DTFx runtime adapter
- **Module Host** — One module per process boundary
- **Instance Agent** — Local instance management endpoint/service
- **Scheduler Host** — Triggers workflow starts only (optional dedicated service)

SQL Server is the persistence store. DTFx coordinates durable orchestration.

## Cloud

- **Function App** — One per module
- Each module provides Durable orchestration functions and activity functions
- Durable state in Azure Storage; telemetry/audit to instance store

## Boundary

The instance plane does not expose management APIs. It receives execution requests from the control plane and reports status/telemetry.
