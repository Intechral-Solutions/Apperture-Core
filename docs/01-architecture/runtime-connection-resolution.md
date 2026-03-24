# Runtime Connection Resolution

This document describes the runtime-side resolution boundary: the controlled path from a governed connection resource and explicit binding to a live, runtime-usable connector/client/session.

## Overview

Runtime dependency resolution is **slot-bound only**. The single entry point for a module or runtime to obtain a live connection-backed client is `IRuntimeSlotResolver.ResolveSlotAsync<T>(moduleId, slotName)`. There is no connection-by-id or arbitrary global resource browsing.

## Resolution Flow

1. **Slot request** — Caller requests resolution by `(moduleId, slotName)`.
2. **Binding lookup** — `IBindingRepository.GetBySlotAsync(moduleId, slotName)`. No binding or inactive binding → no resolution (return null).
3. **Connection lookup** — If binding has `ConnectionId`, `IConnectionRepository.GetByIdAsync(connectionId)`. No connection or inactive connection → no resolution.
4. **Material resolution** — Inside the resolution pipeline, `Connection` plus `ISecretResolver` produce `ResolvedConnectionMaterial` (merged config + resolved secret values). Secret refs from `Connection.SecretRefs` are resolved via `ISecretResolver`; results are merged with `Connection.Config`.
5. **Connector definition** — `IConnectorDefinitionRegistry.Get(connection.ConnectorDefinitionId)` validates the connector exists.
6. **Factory** — `IConnectorClientFactoryRegistry.GetFactory<T>(connection.ConnectorDefinitionId)` returns the connector-specific factory.
7. **Live client** — Factory `Create(connection, material)` returns the live client (e.g. `IDbConnection`). Caller is responsible for disposal where applicable.

## Contracts

| Contract | Layer | Purpose |
|----------|--------|---------|
| **IRuntimeSlotResolver** | Core.Application | Single runtime entry point: `ResolveSlotAsync<T>(moduleId, slotName)`. Returns T or null. No connection id parameter. |
| **ResolvedConnectionMaterial** | Core.Application | Immutable value: merged config + resolved secret values. Passed only to connector factories. ToString redacted. |
| **IConnectorClientFactory<T>** | Core.Application | `Create(Connection, ResolvedConnectionMaterial) → T`. Connector-specific; one per (ConnectorDefinitionId, T). |
| **IConnectorClientFactoryRegistry** | Core.Application | Register/GetFactory by ConnectorDefinitionId and type T. |
| **IConnectionResolver** | Core.Application | Internal to pipeline. Resolves a Connection to T using material builder, registry, and factory. |

## Rules

- **Slot-only entry point** — Modules/runtime must use `IRuntimeSlotResolver.ResolveSlotAsync<T>(moduleId, slotName)` only. No API accepts `ConnectionId` or `Connection` from callers.
- **Binding is the gate** — No binding for the slot, or binding without `ConnectionId`, or inactive binding → no resolution.
- **Connection state** — Only connections in `ConnectionState.Active` are resolved.
- **No auth at resolution (this phase)** — The gate is slot-bound resolution; the runtime is a trusted execution context. Optional future: capability.consume check with runtime principal.

## Secret/Config Boundary

- **Where** — Secret and config material are resolved inside the resolution pipeline (e.g. `ResolvedConnectionMaterialBuilder` used by `DefaultConnectionResolver`).
- **What** — For each key in `Connection.SecretRefs`, a `SecretRef` is built and `ISecretResolver.ResolveAsync` is called; results are merged with `Connection.Config` into `ResolvedConnectionMaterial`.
- **Abstraction** — Callers and connector factories never see `ISecretResolver`. They receive only `Connection` (metadata) and `ResolvedConnectionMaterial` (opaque key-value for building the client). Step handlers receive only the resolved client, not ISecretResolver or secret material.
- **Platform SQL and module connectors** — Same model. Platform SQL factory reads Server, Database, UserId, Password from material; other connectors use the same pipeline.

## Platform SQL Alignment

The platform-managed SQL connector participates in the same resolution model:

- **ConnectorDefinitionId** — `Apperture.Platform.Sql` (see `WellKnownConnectorDefinitionIds.PlatformSql`; `PlatformSqlConstants` aliases the same id).
- **IConnectorClientFactory<IDbConnection>** — `PlatformSqlConnectorClientFactory` builds `SqlConnection` from `ResolvedConnectionMaterial` (Server, Database, UserId, Password).
- **Registration** — Runtime host calls `AddConnectorRepositories()`, `AddConnectorResolution()`, `AddPlatformSqlConnector()` so the worker can resolve slots bound to platform SQL connections. The same extension also registers **Campus Nexus** (`ICampusNexusApiClient`, id `Apperture.Platform.CampusNexus`) and **SFTP** (`ISftpFileClient`, id `Apperture.Platform.Sftp`); see `ADR-UNISA-CampusNexus-Payment-Plans-Connectors-and-Module.md`.

SQL is not a special-case bypass; it uses the same slot → binding → connection → material → factory → client flow.

## Worker Host Requirements

Worker hosts that perform slot-bound resolution must register:

- **ISecretResolver** (e.g. via `AddAppertureSqlSecrets()` or another resolver implementation) so the resolution pipeline can resolve `Connection.SecretRefs`.
- Persistence and connector repositories (`AddApperturePersistence`, `AddConnectorRepositories()`).
- Connector resolution and platform SQL as needed (`AddConnectorResolution()`, `AddPlatformSqlConnector()`).

Without `ISecretResolver`, resolution will fail when a connection has secret refs. See the worker host `Program.cs` for an example registration.

## No Auth at Resolution (Current Phase)

Resolution performs **no authorization check** in this phase. **Binding is the sole gate**: only an active connection binding for the slot yields a resolved client. The **capability.consume** permission at resolution time is deferred; future phases may evaluate it with a runtime principal.

## Disposal

When the resolved client type `T` is **IDisposable** or **IAsyncDisposable**, the **caller** owns the instance and must dispose it (e.g. `using` or `await using`). The runtime does not take ownership or dispose on the caller’s behalf.

## Who May Request Resolution

- Runtime worker/service in the context of module execution.
- Control-plane initiated execution paths where the host has registered the resolution stack.
- Workflow/step execution context requesting a slot by name via `IStepExecutionContext` (see [Runtime Step Execution](runtime-step-execution.md)).

**Forbidden:** Resolving by connection id from module code; browsing arbitrary global connections; bypassing the binding gate.

## Implementation Summary

- **Apperture.ConnectorResolution** — Implements `SlotBoundResolver`, `DefaultConnectionResolver`, `ConnectorClientFactoryRegistry`, `ResolvedConnectionMaterialBuilder`, `PlatformSqlConnectorClientFactory`, `CampusNexusApiConnectorClientFactory`, `SftpConnectorClientFactory`, `InMemoryConnectorDefinitionRegistry`. DI: `AddConnectorResolution()`, `AddPlatformSqlConnector()` (SQL + Campus Nexus + SFTP definitions and factories).
- **Persistence** — `AddConnectorRepositories()` registers `IConnectionRepository`, `IBindingRepository` for runtime hosts that need resolution without the full control-plane workflows.
- **Worker host** — Registers persistence, connector repositories, connector resolution, and platform SQL so `IRuntimeSlotResolver` is available.

## Deferred

- Audit emission of “slot X resolved to connection Y” / “resolution denied”
- Runtime-created connections and their binding flow
- capability.consume check at resolution time (runtime principal)
- Full workflow/step parameter integration (passing slot names into steps)
- All connector types other than platform SQL
- Caching, connection pooling, or session reuse
- Machine/system/remote-instance resolution policy
- Sensitive-resource step-up auth

## Related

- [Runtime Step Execution](runtime-step-execution.md) — How steps request slot-bound dependencies via execution context.
- [Connector Resource Family](connector-resource-family.md)
- [Control-Plane Connector Workflows](control-plane-connector-workflows.md)
- [Authorization and Governance Foundation](authorization-and-governance-foundation.md)
- [ADR-0027](../02-decisions/ADR-0027-connector-definitions-not-platform-clients.md) — Platform catalogs connector definitions and connections; typed clients are not a management-plane invocation layer.
