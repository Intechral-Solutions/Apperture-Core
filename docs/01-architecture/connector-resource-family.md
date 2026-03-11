# Connector Resource Family

This document describes the first concrete governed resource family integrated into the auth/governance foundation: connectors, connections, capability slots, and bindings.

## Overview

The connector resource family is one governed resource family among many. It participates in authorization via `IGovernedResource` and `IGovernedResourceIdentity`. Auth remains generic; it does not know connector internals.

## Resource Types

| Resource | ResourceType | Scope pattern | Purpose |
|----------|--------------|---------------|---------|
| ConnectorDefinition | ConnectorDefinition | instance or module:{id} | Declares a connector kind; platform or module-provided |
| Connection | Connection | instance or module:{id} | Governed config + secret refs; resolves to live client via IConnectionResolver |
| Binding | Binding | module:{id}:slot:{name} | Slot-to-connection delegation; does not replace operational auth |
| MappingDefinition | MappingDefinition | module:{id} or instance | Gateway result persistence mapping; governed |
| CapabilitySlot | CapabilitySlot | module:{id}:slot:{name} | Declared consumer slot; capability.consume for resolution |

## Actions and Authorization Participation

| Action | Resource type | Use |
|--------|---------------|-----|
| connector.manage | ConnectorDefinition | View/manage connector definitions |
| connection.create, read, update, delete | Connection | Administrative connection lifecycle |
| binding.create, read, delete, assign, revoke | Binding | Binding/delegation management |
| capability.consume | CapabilitySlot | Operational use when resolving connection for slot |
| mapping.manage | MappingDefinition | Gateway mapping registration |

## Auth Integration Pattern

When evaluating authorization for a governed resource:

1. Obtain identity: `var identity = resource.GetIdentity()`
2. Build resource descriptor: `GovernedResourceDescriptor.FromIdentity(identity)` for type/scope
3. Call: `IAuthorizationService.EvaluateAsync(actor, permission, resourceDescriptor, identity)`

The optional `resourceIdentity` enables instance-level and future ownership checks.

## Distinctions

- **Administrative** — connection CRUD, connector.manage, mapping.manage. Configuration and lifecycle.
- **Binding/delegation** — binding.assign, binding.revoke. Explicit delegation of a resource to a slot.
- **Operational** — capability.consume. Using a bound connection during module execution.

## Control-Plane Application Workflows (Implemented)

The control plane can govern connector-related resources through these application services:

### Connection Management
- **IConnectionManagementService** — List, get, create, update, retire connections
- Auth: connection.read, connection.create, connection.update, connection.delete
- Persistence: IConnectionRepository, SqlConnectionRepository

### Slot Inspection
- **IModuleSlotRegistry** — Register and list capability slots per module
- **IBindingService.GetBindingAsync** — Get binding for a slot (auth: binding.read)

### Binding Workflows
- **IBindingService** — Create binding (connection or capability to slot), delete binding, get eligible connections
- Auth: binding.assign, binding.revoke, binding.read
- **IBindingEligibilityService** — Filters connections by active state and connector definition
- Persistence: IBindingRepository, SqlBindingRepository

### Where Auth Checks Occur
- Every workflow method resolves actor via IPrincipalContext, calls IAuthorizationService.EvaluateAsync before proceeding
- UnauthorizedAccessException thrown when actor is null or permission denied

### API Endpoints
- `GET/POST/PATCH/DELETE /connections` — Connection CRUD and retire
- `GET/POST/DELETE /modules/{moduleId}/slots/{slotName}/binding` — Binding get, create, revoke
- `GET /modules/{moduleId}/slots/{slotName}/eligible-connections?contractType=X` — List eligible connections

### Eligibility Rules (Minimal)
- Connection.State == Active
- Connector definition must exist (permissive contract-type matching for now)

## Connection Lifecycle

Connections have **ConnectionState**: Active or Retired. Retired connections are excluded from binding eligibility.

## Scaffolded vs Deferred

**Implemented:**
- EF persistence for Connection, Binding, ConnectorDefinition
- IConnectionRepository, IBindingRepository, Sql implementations
- IConnectionManagementService, IBindingService (full implementations with auth)
- IBindingEligibilityService, IModuleSlotRegistry
- Minimal Connections and Bindings API endpoints

**Deferred:**
- IConnectionResolver (live client resolution)
- Secret resolution, live client creation
- Connector management UI (beyond minimal API)
- Module slot discovery from manifests

## Related

- [Authorization and Governance Foundation](authorization-and-governance-foundation.md)
- [ADR-0012](../02-decisions/ADR-0012-authorization-evaluation-model.md)
- [ADR-0013](../02-decisions/ADR-0013-action-and-scope-naming-conventions.md)
