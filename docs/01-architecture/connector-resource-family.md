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
| connector.definition.read | ConnectorDefinition | Read the connector-definition catalog (management API shape metadata) |
| connector.manage | ConnectorDefinition | Reserved for future mutating/admin semantics on connector definitions (not used for catalog read) |
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

- **Administrative** — connection CRUD, connector.definition.read (catalog browse), connector.manage (reserved for future definition admin), mapping.manage. Configuration and lifecycle.
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
- `GET /api/connector-definitions`, `GET /api/connector-definitions/{id}` — Read-only catalog (`connector.definition.read`). See [ADR-0027](../02-decisions/ADR-0027-connector-definitions-not-platform-clients.md).

### Source of truth for connector definitions (Epic 52A)

The **authoritative catalog** for the control plane and runtime resolution is **`IConnectorDefinitionRegistry`**: in-process definitions registered at host startup (well-known platform definitions, optional module contributors). The management API reads from that registry only.

The **`ConnectorDefinitions`** SQL table and **`ConnectorDefinitionEntity`** exist for historical schema evolution but are **not** the catalog source of truth today; nothing in the control-plane or connector-resolution paths loads definitions from that table for listing or validation. Do not treat the table as authoritative until a persistence-backed catalog is explicitly implemented end-to-end.
- `GET/POST/PATCH/DELETE /connections` — Connection CRUD and retire
- `GET/POST/DELETE /modules/{moduleId}/slots/{slotName}/binding` — Binding get, create, revoke
- `GET /modules/{moduleId}/slots/{slotName}/eligible-connections?contractType=X` — List eligible connections

### Eligibility Rules (Minimal)
- Connection.State == Active
- Connector definition must exist (permissive contract-type matching for now)

## Runtime Resolution

Live client resolution at runtime is **slot-bound only**. The runtime does not resolve by connection id; it resolves by (moduleId, slotName) via `IRuntimeSlotResolver.ResolveSlotAsync<T>(moduleId, slotName)`. Binding is the gate: only an active connection binding for the slot yields a resolved client. The resolution pipeline uses `IConnectionResolver` internally (binding → connection → secret/config material → connector factory → live client). See [Runtime Connection Resolution](runtime-connection-resolution.md) for the full flow, contracts, and secret/config boundary.

## Connection Lifecycle

Connections have **ConnectionState**: Active or Retired. Retired connections are excluded from binding eligibility.

## Scaffolded vs Deferred

**Implemented:**
- EF persistence for Connection, Binding (connector definitions: registry-backed only; see “Source of truth” above)
- IConnectionRepository, IBindingRepository, Sql implementations
- IConnectionManagementService, IBindingService (full implementations with auth)
- IBindingEligibilityService, IModuleSlotRegistry
- Minimal Connections and Bindings API endpoints

**Implemented (runtime resolution):**
- IRuntimeSlotResolver, IConnectionResolver (slot-bound resolution path)
- ResolvedConnectionMaterial, IConnectorClientFactory&lt;T&gt;, IConnectorClientFactoryRegistry
- Platform SQL connector (Apperture.Platform.Sql) and IConnectorClientFactory&lt;IDbConnection&gt;
- Secret/material resolution inside pipeline; connector factories receive material only

**Deferred:**
- Rich connector management UI (full create/edit flows for connections remain incremental)
- Module slot discovery from manifests
- Audit emission for resolution events; capability.consume at resolution time

## Related

- [Authorization and Governance Foundation](authorization-and-governance-foundation.md)
- [ADR-0012](../02-decisions/ADR-0012-authorization-evaluation-model.md)
- [ADR-0013](../02-decisions/ADR-0013-action-and-scope-naming-conventions.md)
- [ADR-0027](../02-decisions/ADR-0027-connector-definitions-not-platform-clients.md) — Connector definitions vs platform-managed clients (Epic 52)
