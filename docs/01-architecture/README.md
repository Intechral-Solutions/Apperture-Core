# Architecture

| Document | Description |
|----------|-------------|
| [current-architecture-tracking.md](current-architecture-tracking.md) | **Current-state tracking** — High-level and detailed subsystem diagrams; what is implemented vs stubbed (not a roadmap) |
| [foundational-layers.md](foundational-layers.md) | Shared Kernel, Core.Domain, Core.Application boundaries and dependency rules |
| [persistence-foundation.md](persistence-foundation.md) | Shared SQL substrate, migrations, retry/resilience, schema conventions |
| [bootstrap-and-secrets-boundary.md](bootstrap-and-secrets-boundary.md) | Bootstrap vs operational secrets, dependency direction, do-not-do list |
| [platform-settings-registry.md](platform-settings-registry.md) | Platform-managed settings registry — **Phase 1 delivered (Epic 109)**; API/UI at `/config/platform-settings` |
| [platform-settings-option-providers-and-scopes.md](platform-settings-option-providers-and-scopes.md) | **Epic 113 design** — dynamic option providers, scoped settings, module/cross-integration boundaries (spec only) |
| [integration-scoped-settings.md](integration-scoped-settings.md) | Integration-scoped settings — Epic 117 design; Epic 118 API; Epic 119 Integration Detail UI |
| [authorization-and-governance-foundation.md](authorization-and-governance-foundation.md) | Auth/governance scaffolding — principal, role, permission, IGovernedResource; RBAC + scope containment; Phase 4 readiness |
| [connector-resource-family.md](connector-resource-family.md) | First governed resource family — connectors, connections, slots, bindings; auth participation |
| [control-plane-connector-workflows.md](control-plane-connector-workflows.md) | Connection/binding application workflows; auth enforcement; eligibility |
| [runtime-connection-resolution.md](runtime-connection-resolution.md) | Runtime slot-bound resolution; binding → connection → material → factory → client; secret boundary |
| [runtime-step-execution.md](runtime-step-execution.md) | Step execution context; slot-bound dependency resolution in workflow steps; disposal and failure semantics |
| [control-plane.md](control-plane.md) | Management API, UI, responsibilities |
| [control-panel-ui.md](control-panel-ui.md) | Management shell (rail + Control Panel) |
| [instance-plane.md](instance-plane.md) | Worker, SQL enqueue/claim/inline execution today; DTFx as future adapter; module/process isolation as a future deployment option |
| [rc1-program-gate.md](rc1-program-gate.md) | **RC1 program contract:** RC1 definition, decisions, deferrals, epic sequence |
| [tenancy-model.md](tenancy-model.md) | Single-tenant Instance, auditability |
| [federated-instance-relationships.md](federated-instance-relationships.md) | **Withdrawn (ADR-0046).** Historical design for federated cross-instance trust; not planned |
| [durable-workflows.md](durable-workflows.md) | Portable workflow model, persistence |
| [runtime-backends.md](runtime-backends.md) | DTFx vs Durable Functions adapters |
| [instance-owned-scheduling.md](instance-owned-scheduling.md) | Portable scheduling model with instance-owned execution |
| [c4/](c4/) | C4 diagrams |
| [diagrams/](diagrams/) | Architecture diagrams |

In-flight change records (not architecture): [transitions/](transitions/).
