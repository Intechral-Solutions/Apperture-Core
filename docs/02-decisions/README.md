# Architecture Decision Records

| ADR | Title |
|-----|-------|
| [ADR-0001](ADR-0001-runtime-boundaries.md) | Runtime boundaries (control vs instance plane, one-module-per-process) |
| [ADR-0002](ADR-0002-single-tenant-instance.md) | Single-tenant instance deployment boundary |
| [ADR-0003](ADR-0003-runtime-portability.md) | Runtime portability (portable workflow model, DTFx + Durable Functions adapters) |
| [ADR-0004](ADR-0004-instance-owned-scheduling.md) | Instance-owned scheduling (control-plane-managed, host-portable execution) |
| [ADR-0037](ADR-0037-platform-signals-events-and-notification-system.md) | Platform signals and notification system (Epic 77) |
| [ADR-0038](ADR-0038-platform-signals-sdk-conventions.md) | Module SDK signal conventions (Epic 103A) |
| [ADR-0039](ADR-0039-environment-startup-bootstrap-and-platform-settings.md) | Environment, startup, bootstrap, and platform settings layers (105B; Phase 1 settings Epic 109) |
| [Epic 110](../03-operations/epic110-foundation-stabilization.md) | Foundation stabilization, smoke baseline, deferred roadmap, Epic 111 recommendation |
| [ADR-0005](ADR-0005-federated-external-instance-relationships.md) | Federated external instance relationships |
| [ADR-0006](ADR-0006-entra-backed-platform-trust-for-instance-authentication.md) | Entra-backed platform trust for instance authentication |
| [ADR-0007](ADR-0007-separation-of-human-auth-service-auth-and-relationship-authorization.md) | Separation of human auth, service auth, and relationship authorization |
| [ADR-0008](ADR-0008-parent-child-first-on-generalized-relationship-model.md) | Parent-child first on generalized relationship model |
| [ADR-0009](ADR-0009-constrained-cross-instance-management-api-boundary.md) | Constrained cross-instance management API boundary |
| [ADR-0010](ADR-0010-signed-reliable-child-to-parent-notifications.md) | Signed reliable child-to-parent notifications |
| [ADR-0011](ADR-0011-delegated-capability-model-for-remote-instance-administration.md) | Delegated capability model for remote instance administration |
| [ADR-0012](ADR-0012-authorization-evaluation-model.md) | Authorization evaluation model (ResourceDescriptor vs IGovernedResourceIdentity) |
| [ADR-0013](ADR-0013-action-and-scope-naming-conventions.md) | Action and scope naming conventions |
| [Epic 73](Epic73-execution-hot-path-and-host-roles.md) | Execution hot path, host roles, runtime boundary (dispatch, module persistence, connectors) |

Use [adr-template.md](adr-template.md) when creating new ADRs.
