# Management Service (Control Plane)

## Responsibilities

- Module registry (what is installed/enabled for this instance)
- Workflow definition discovery (by module)
- Execution requests (start, signal, cancel)
- Run history and details
- Schedule management
- Audit logs (who did what)
- Access control primitives (minimal RBAC)
- Telemetry browsing/query APIs

## Components

- **Management API** — ASP.NET Core; REST endpoints
- **Management Host** — Composition root; wires API, persistence, workflow starter client
- **Management Client** — Typed client for CLI, agents, other callers
- **Management UI** — Blazor WASM; mirrors Integration Center patterns

## Key endpoints (minimal v1)

- `GET /modules`
- `GET /modules/{id}/workflows`
- `POST /workflows/{workflowId}/start`
- `GET /runs`
- `GET /runs/{id}`

## Auth

Management API uses Entra-authenticated users (wiring can be placeholder). Audit management actions by user identity. Modules declare resources/permissions; implement minimal RBAC primitives.
