# ADR-0027: Connector definitions and connections — correction from platform-managed clients

## Status

Accepted

## Context

[ADR-0026](./ADR-0026-epic-51-client-definition-model.md) introduced **ClientDefinition** as a first-class platform concept with **client operation metadata** and a **Clients** management API/UI surface. That model treated “clients” and operator-visible operation catalogs as part of the control plane.

That boundary was incorrect. The platform should not expose generic **client** definitions, **client operation** metadata, or operator-facing surfaces that imply the platform can execute or configure arbitrary connector methods. Typed access to external systems belongs in **connector and module code**; the platform governs **what can be connected** (shape of connection material), **connections** (instance configuration), **slots and bindings** (secure delegation), and **runtime delivery** of resolved material—not a second invocation plane for connector clients.

## Decision

This ADR **supersedes** ADR-0026. The Epic 51 client-definition model is **removed** from the platform contract.

1. **Platform-visible primitive** — **Connector definition → Connection → slot binding**. Not **Client → Operation**.

2. **Connector definitions** — Canonical catalog artifact: identity, display metadata, **connection field descriptors** (config vs secret hints), and optional **capability flags** that do not imply generic operation exposure. Definitions may be contributed at host startup via **connector definition contributors** merged into `IConnectorDefinitionRegistry`.

3. **Connections** — Remain first-class governed resources: created from a connector definition, holding config and secret references, bound into integrations via slots.

4. **Code-level consumption** — Modules depend on connector packages and use **typed** `IConnectorClientFactory<T>` (or equivalent) with material from **slot-bound resolution** (`IRuntimeSlotResolver`). No platform-managed client invocation layer.

5. **Operator honesty** — If an operator can trigger behavior through the platform, it is via **module-contributed** workflows, operations, or other explicit control surfaces—not via a generic connector “operation” catalog.

6. **Gateways** — **Deferred**. A future **brokered/gateway** pattern may sit in front of some connectors for controlled access; it is not the default model and is **not** implemented here. Optional **capability flags** on connector definitions may label candidates for such a pattern without implementing buses, queues, or gateway brokers.

## Consequences

### Positive

- Aligns the management plane with an honest boundary: catalog and lifecycle of **definitions** and **connections**, not executable client APIs.
- Reduces architectural confusion between “connection material” and “things the platform runs on your behalf.”
- Keeps runtime delivery clean: bindings gate resolution; factories stay in connector code.

### Negative

- **Breaking**: `GET /api/clients` and related DTOs are removed; consumers must use **connector-definition** catalog endpoints.

### Neutral

- ADR-0026 remains in the repo as historical context but is **superseded** by this record.
- **Catalog source of truth (Epic 52A):** Connector definitions are materialized in **`IConnectorDefinitionRegistry`** (host-seeded / contributor-registered). A SQL `ConnectorDefinitions` table may exist for schema continuity but is **not** the authoritative catalog until an explicit persistence-backed path is implemented end-to-end. See [connector-resource-family](../01-architecture/connector-resource-family.md).
