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

## Connectors and connection verification

Connector packages register definitions via `IConnectorDefinitionContributor` (metadata only). Executable behavior (e.g. test-connect) is registered separately:

1. Set `ConnectorCapabilities.SupportsConnectionVerification` on the `ConnectorDefinition`.
2. Implement `IConnectionVerifier` for that `ConnectorDefinitionId`.
3. Implement `IConnectionVerifierContributor` with `Register(IServiceCollection)` calling `AddScoped<IConnectionVerifier, TVerifier>()` and any verifier-only dependencies (e.g. `SftpConnectorClientFactory`).
4. Invoke `new YourConnectionVerifierContributor().Register(services)` from the package’s `AddApperture…Connector` extension.

Do **not** register verifiers inside `AddAppertureConnectorWorkflows` in ControlPlane.Security; that would couple the security layer to specific connector assemblies.

Hosts should call `AddApperture…Connector` **before** `AddAppertureConnectorWorkflows` so registration order is obvious (DI still resolves all `IConnectionVerifier` registrations regardless of order).

At startup, `ConnectionVerifierRegistrationSanityHostedService` logs a warning if a definition declares `SupportsConnectionVerification` but no `IConnectionVerifier` is registered.
