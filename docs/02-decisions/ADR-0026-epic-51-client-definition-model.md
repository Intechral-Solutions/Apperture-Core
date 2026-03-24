# ADR-0026: Epic 51 Client Definition Model

## Status

Accepted

## Context

Connections are low-level resources that reference `ConnectorDefinitionId`. Connectors (e.g., Campus Nexus, SFTP, Platform SQL) expose APIs and capabilities, but there is no higher-level abstraction for "client definitions" that describe what operations a connection type supports and how operators or workflows might use it. The Clients tab in the Connections area was deferred with a placeholder. We need a first-class `ClientDefinition` concept that:

- Sits above Connections: a ClientDefinition declares what *kind* of connection it expects (`RequiredConnectorDefinitionId`) and what operations it exposes
- Supports module/connector contribution via `IClientDefinitionProvider`
- Enables future client usage and execution in Integrations without blocking on that implementation

## Decision

### Domain Model

1. **ClientDefinition** — Record with `ClientDefinitionId`, `DisplayName`, `Description`, `RequiredConnectorDefinitionId`, `ModuleId?`, `UsesGateway`, and `IReadOnlyList<ClientOperationMetadata> Operations`. A Connection whose `ConnectorDefinitionId` matches `RequiredConnectorDefinitionId` can later fulfill a client usage.

2. **ClientOperationMetadata** — `OperationKey`, `DisplayName`, `Description`, `IsOperatorFacing`, `MayRequireUserInputs`, `ParameterHint?`. Metadata-only; execution is deferred.

### Contribution Pattern

- **IClientDefinitionProvider** — `IAsyncEnumerable<ClientDefinition> GetClientDefinitionsAsync()`. Connectors and modules register implementations.
- **IClientDefinitionQueryService** — Aggregates from all providers, dedupes by Id.
- **IClientDefinitionReadService** — Authorized read surface; reuses `connection.read` for gating.

### API and UI

- **GET api/clients** — List `ClientDefinitionDto[]`
- **GET api/clients/{id}** — Get by id; 404 if not found
- **Management Client** — `GetClientDefinitionsAsync()`, `GetClientDefinitionAsync(id)`
- **Clients tab** — Replaces placeholder with `ClientsListContent` listing definitions: Display Name, Connector, Module (or "Platform"), Uses Gateway, Operations count

### Deferred (Explicitly Out of Scope)

- Client usage/binding in Integrations
- Operator-triggered client operation execution
- Operation history
- Client instance/configuration CRUD
- Module-specific ClientDefinition contribution (connector contribution only in first slice)

## Consequences

### Positive

- ClientDefinition is a first-class platform concept with clear relationship to Connections
- Connectors can contribute definitions; Campus Nexus provider demonstrates the pattern
- UI copy clarifies that usage will be configured in Integrations (future)
- Authorization reuses `connection.read`; no new permission surface

### Negative

- Client definitions only appear when a connector that contributes them is registered (e.g., Campus Nexus via PaymentPlans integration)

### Neutral

- Client usage will attach to Integration composition in a future epic
