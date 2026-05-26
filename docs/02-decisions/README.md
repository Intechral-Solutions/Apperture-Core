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

### Foundation sequence (Epics 105–117)

| Doc | Title |
|-----|-------|
| [Epic 105A](Epic105A-Environment-Startup-Inventory.md) | Appsettings and deploy/config canonical inventory |
| [Epic 105B](Epic105B-Configuration-Layers.md) | Four-layer configuration model (see ADR-0039) |
| [Epic 106](Epic106-Aspire-Local-Orchestration.md) | Aspire AppHost and ServiceDefaults for local dev |
| [Epic 107](Epic107-Post-Aspire-Assessment.md) | Post-Aspire reassessment (historical; superseded by Epic 110) |
| [Epic 108](Epic108-File-Backed-Bootstrap-Store.md) | File-backed bootstrap configuration store |
| [Epic 109](Epic109-Platform-Settings-Registry-Phase1.md) | Platform settings registry Phase 1 (nine keys, API/UI) |
| [Epic 110](Epic110-Foundation-Stabilization.md) | Foundation stabilization and roadmap ([ops runbook](../03-operations/epic110-foundation-stabilization.md)) |
| [Epic 111](Epic111-Platform-Settings-Operator-UI-Polish.md) | Platform settings operator UI polish |
| [Epic 112](Epic112-Platform-Settings-MultiSelect-Foundation.md) | StringArray, InputKind, static MultiSelect |
| [Epic 113](Epic113-Platform-Settings-Option-Providers-Design.md) | Option providers and scoped settings (design spec only) |
| [Epic 114](Epic114-Platform-Setting-Option-Provider-Prototype.md) | `platform.connectorDefinitions` option provider prototype |
| [Epic 115](Epic115-Integration-Scoped-Option-Provider-Foundation.md) | Integration-scoped provider context foundation |
| [Epic 116](Epic116-CampusNexus-Campuses-Option-Provider.md) | `campusnexus.campuses` same-integration option provider |
| [Epic 117](Epic117-Integration-Scoped-Settings-Design.md) | Integration-scoped settings and Integration Detail settings UI design |
| [Epic 118](Epic118-Integration-Settings-API-Foundation.md) | Integration-scoped settings persistence and API foundation |
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
