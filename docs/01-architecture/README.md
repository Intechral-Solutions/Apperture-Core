# Architecture

| Document | Description |
|----------|-------------|
| [current-architecture-tracking.md](current-architecture-tracking.md) | **Current-state tracking** — High-level and detailed subsystem diagrams; what is implemented vs stubbed (not a roadmap) |
| [foundational-layers.md](foundational-layers.md) | Shared Kernel, Core.Domain, Core.Application boundaries and dependency rules |
| [persistence-foundation.md](persistence-foundation.md) | Shared SQL substrate, migrations, retry/resilience, schema conventions |
| [bootstrap-and-secrets-boundary.md](bootstrap-and-secrets-boundary.md) | Bootstrap vs operational secrets, dependency direction, do-not-do list |
| [authorization-and-governance-foundation.md](authorization-and-governance-foundation.md) | Auth/governance scaffolding — principal, role, permission, IGovernedResource; RBAC + scope containment; Phase 4 readiness |
| [connector-resource-family.md](connector-resource-family.md) | First governed resource family — connectors, connections, slots, bindings; auth participation |
| [control-plane.md](control-plane.md) | Management API, UI, responsibilities |
| [instance-plane.md](instance-plane.md) | Worker, DTFx, one module per process |
| [tenancy-model.md](tenancy-model.md) | Single-tenant Instance, auditability |
| [federated-instance-relationships.md](federated-instance-relationships.md) | Federated cross-instance trust, scoped delegation, and auditable management boundary |
| [durable-workflows.md](durable-workflows.md) | Portable workflow model, persistence |
| [runtime-backends.md](runtime-backends.md) | DTFx vs Durable Functions adapters |
| [instance-owned-scheduling.md](instance-owned-scheduling.md) | Portable scheduling model with instance-owned execution |
| [c4/](c4/) | C4 diagrams |
| [diagrams/](diagrams/) | Architecture diagrams |
