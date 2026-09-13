# Architecture Diagrams

Purpose-specific and cross-cutting architecture diagrams for Apperture Core. Open `.drawio` files in [draw.io](https://app.diagrams.net/) or VS Code with a draw.io extension.

**Conventions:** [_conventions.md](_conventions.md) and [_conventions.drawio](_conventions.drawio)

**C4 structural diagrams:** [../c4/](../c4/)

**Current-state source of truth:** [../current-architecture-tracking.md](../current-architecture-tracking.md)

## Catalog

| ID | File(s) | Variants | Purpose | Key sources |
|----|---------|----------|---------|-------------|
| — | `_conventions` | legend | Shared visual conventions for all diagrams | This folder |
| 05 | `05-workflow-hot-path` | current, projected | SQL enqueue → worker claim → inline execution; cancel/retry/park-resume | [runtime-step-execution.md](../runtime-step-execution.md), Epic 73 ADR |
| 06 | `06-instance-scheduling` | current, projected | Instance-owned schedule evaluation, lease, dispatch | ADR-0004, [instance-owned-scheduling.md](../instance-owned-scheduling.md) |
| 07 | `07-runtime-portability` | current, projected | Portable workflow model; DTFx vs Durable Functions adapters | ADR-0003, [runtime-backends.md](../runtime-backends.md) |
| 08 | `08-config-secrets-layering` | current, projected | Four-layer configuration model; bootstrap/secrets/trust boundaries | ADR-0039, [bootstrap-and-secrets-boundary.md](../bootstrap-and-secrets-boundary.md) |
| 09 | `09-authz-governance` | current, projected | RBAC, governed resources, evaluation order | ADR-0012..0014, [authorization-and-governance-foundation.md](../authorization-and-governance-foundation.md) |
| 10 | `10-connector-integration` | current, projected | Connector definitions, connections, bindings, slot resolution | ADR-0024..0028, [connector-resource-family.md](../connector-resource-family.md) |
| 11 | `11-signals-notifications` | current, projected | Module signals → platform events → notification delivery | ADR-0037, ADR-0038, [platform-signal-catalog.md](../platform-signal-catalog.md) |
| 12 | `12-persistence-er` | current, projected | AppertureDbContext entity relationships | [persistence-foundation.md](../persistence-foundation.md) |
| 13 | `13-deployment-topology` | current, projected | Aspire dev vs production on-prem/cloud deployment | ADR-0001, [instance-plane.md](../instance-plane.md) |
| 14 | `14-federated-instances` | projected | Cross-instance parent-child federation model | ADR-0005..0011, [federated-instance-relationships.md](../federated-instance-relationships.md) |
| 15 | `15-management-ui-shell` | current | Blazor WASM shell, navigation, design system | [control-panel-ui.md](../control-panel-ui.md), [management-ui-design-system.md](../management-ui-design-system.md) |

## Variant legend

| Suffix | Meaning |
|--------|---------|
| `.current.drawio` | Implemented or partial state today |
| `.projected.drawio` | Future/target state (green = new/changed) |

## File inventory (28 files)

### Conventions
- `_conventions.drawio`

### Cross-cutting (05–15)
- `05-workflow-hot-path.current.drawio`, `05-workflow-hot-path.projected.drawio`
- `06-instance-scheduling.current.drawio`, `06-instance-scheduling.projected.drawio`
- `07-runtime-portability.current.drawio`, `07-runtime-portability.projected.drawio`
- `08-config-secrets-layering.current.drawio`, `08-config-secrets-layering.projected.drawio`
- `09-authz-governance.current.drawio`, `09-authz-governance.projected.drawio`
- `10-connector-integration.current.drawio`, `10-connector-integration.projected.drawio`
- `11-signals-notifications.current.drawio`, `11-signals-notifications.projected.drawio`
- `12-persistence-er.current.drawio`, `12-persistence-er.projected.drawio`
- `13-deployment-topology.current.drawio`, `13-deployment-topology.projected.drawio`
- `14-federated-instances.projected.drawio`
- `15-management-ui-shell.current.drawio`

### C4 (see [../c4/README.md](../c4/README.md))
- 7 files in `../c4/`
