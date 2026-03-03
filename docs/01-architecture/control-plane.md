# Control Plane

The control plane manages the Instance. See [ADR-0001](../02-decisions/ADR-0001-runtime-boundaries.md).

## Components

- **Management API** — ASP.NET Core REST API
- **Management UI** — Blazor WASM (mirrors Integration Center patterns)
- **Management Client** — Typed client for CLI, agents

## Responsibilities

- Module registry (installed/enabled modules)
- Workflow definition discovery
- Execution requests (start, signal, cancel)
- Run history and details
- Schedule management
- Audit logs
- Access control (minimal RBAC)
- Telemetry browsing/query

## Boundary

The control plane does not execute workflow steps. It issues requests to the instance plane (Worker or cloud runtime).
