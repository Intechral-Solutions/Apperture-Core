# C4 Architecture Diagrams

C4 structural diagrams for Apperture Core. Open `.drawio` files in [draw.io](https://app.diagrams.net/) or VS Code with a draw.io extension.

**Conventions:** [../diagrams/_conventions.md](../diagrams/_conventions.md) and [../diagrams/_conventions.drawio](../diagrams/_conventions.drawio)

**Current-state source of truth:** [../current-architecture-tracking.md](../current-architecture-tracking.md)

## Catalog

| ID | File(s) | Variants | Purpose | Key sources |
|----|---------|----------|---------|-------------|
| C4-01 | `c4-01-system-context` | current, projected | System boundary and external actors/systems | ADR-0001, ADR-0002, ADR-0005..0011 |
| C4-02 | `c4-02-container` | current, projected | Deployable containers across control, instance, foundation, runtime planes | [control-plane.md](../control-plane.md), [instance-plane.md](../instance-plane.md), [runtime-backends.md](../runtime-backends.md) |
| C4-03 | `c4-03-component-controlplane` | current | Control plane components; enqueue-only dispatch boundary | [control-plane.md](../control-plane.md), Epic 73 ADR |
| C4-04 | `c4-04-component-runtime` | current, projected | Worker execution path; inline engine vs durable orchestration adapters | [runtime-step-execution.md](../runtime-step-execution.md), ADR-0003 |

## Layer progression

```
C4-01 System Context  →  C4-02 Container  →  C4-03 / C4-04 Component
```

Cross-cutting purpose diagrams (workflow hot path, scheduling, connectors, etc.) live in [../diagrams/](../diagrams/).
