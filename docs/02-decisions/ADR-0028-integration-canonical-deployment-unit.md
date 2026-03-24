# ADR-0028: Integration as canonical deployment and binding unit

## Status

Accepted

## Context

[ADR-0027](./ADR-0027-connector-definitions-not-platform-clients.md) recenters the platform on connector definitions, persisted connections, module slot requirements, and direct module consumption of connector-provided typed clients. [ADR-0024](./ADR-0024-integration-ownership-boundary.md) and [ADR-0025](./ADR-0025-epic-50-integration-composition.md) introduced Integration as the operator-facing unit and nullable ownership on bindings and schedules.

Without a single canonical deployable unit, operators still mentally stitch module metadata, loose bindings, detached schedules, and runtime pages. Runtime slot resolution that ignores Integration also breaks down when multiple integrations use the same module.

## Decision

1. **Integration is the canonical control-plane unit** for module-based deployment: it composes a module reference, instance settings, integration-scoped slot bindings, integration-scoped schedules, and (when present) lineage to workflow runs. Operators deploy and manage **an Integration**, not a scattered set of resources.

2. **Bindings and schedules are integration-scoped for new configuration.** Legacy module-only binding creation and schedules without `IntegrationId` are rejected at the API (see Epic 54 implementation). Persisted rows may still carry null `IntegrationId` until backfilled; readiness and runtime behavior prefer integration context when available.

3. **Readiness is evidence-based** — a deterministic projection from persisted facts: declared module slots, integration bindings, connection state and connector compatibility, and schedule association. No vague heuristics.

4. **Runtime lineage** — `StartWorkflowRequest` and persisted workflow instances/runs carry optional `IntegrationId`. Slot resolution uses `(integrationId, moduleId, slotName)` when integration id is known; otherwise falls back to legacy `(moduleId, slotName)` for transitional data.

5. **Out of scope** — Gateways, brokered access, generic client operation invocation, message bus orchestration, and connector-definition redesign remain excluded (per ADR-0027).

## Consequences

### Positive

- One coherent operator story: configure and validate an Integration; schedules and runs trace back to it when configured.
- Multi-integration deployments using the same module can resolve slots without ambiguous “first row wins” when integration id is supplied.

### Negative

- Clients must create bindings and schedules through integration-scoped APIs; old module-only binding POST and schedule create without integration id are no longer supported.

### Neutral

- Nullable `IntegrationId` on historical rows may remain until data migration; behavior is explicitly transitional where documented.
