# Authorization and Governance Foundation

This document captures the status of the auth/governance scaffolding (Phases 1–3): principles, implementation summary, project placement, core types, and the recommended next phase.

## Platform Governance Cycle Updates

The platform governance cycle added cross-domain hardening beyond operations:

- shared authorization seam: `IAuthorizationEvaluationService` with `AuthorizationEvaluationContext`
- descriptor registration in `InMemoryResourceDescriptorRegistry`: exact duplicate (same descriptor + family) is idempotent; conflicting duplicate (same key, different descriptor) fails with clear diagnostics; identity validation enforced (ResourceType/ScopeKey/Action naming rules)
- module governance contributions in `ModuleManifest` (`GovernedResources`, `GovernedActions`) require explicit `ResourceType`; generic contributions use `ResourceFamily.Core` (no operation-shaped defaults)
- minimum governance management APIs for descriptor listing, effective-permission inspection, and role-permission assignment; role-permission updates validated against canonical descriptor catalog before persistence
- schedule capability projection endpoint (`/api/schedules/capabilities`) aligned to governance checks
- canonical privileged audit action names for role/permission governance operations

## Principles

- **Authorization is central and generic** — Auth knows principals, resource types, actions, scopes; it does not know connector internals.
- **Resource governance is broader than connectors** — Connector/connection/capability concepts are one governed resource family.
- **Governed resources plug in via IGovernedResource** — Auth consumes identity only; no family-specific types in auth core.
- **Administrative vs operational auth** — Distinguished by permission action (e.g. `connection.create` vs `capability.consume`).
- **Binding is delegation** — Does not replace operational authorization.

## Implementation Summary

| Area | Status | Location |
|------|--------|----------|
| Principal/identity | Done | IActor, ActorKind, ActorId, IPrincipalContext, IIdentityResolutionService, **HostPrincipalContext** (resolves ClaimsPrincipal → IActor) |
| Role/permission | Done | Role, Permission, IRoleProvider, PermissionDescriptor, **SqlRoleProvider** (persisted roles and user-role assignments) |
| Governed resource vocabulary | Done | IGovernedResource, IGovernedResourceIdentity, GovernedResourceIdentity, ResourceTypeConstants, ActionConstants, ScopeKeyConstants, GovernedResourceDescriptor |
| Authorization evaluation | Done | IAuthorizationService, IAuthorizationPolicyEvaluator, DelegatingAuthorizationService, RbacAuthorizationPolicyEvaluator, IScopeContainmentEvaluator |
| Principal-to-user bridge | Done | IPrincipalToUserResolver, **SqlPrincipalToUserResolver** (resolves Human actor → UserId via persisted IdentityLinks) |
| Platform identity persistence | Done | Users, Roles, IdentityLinks, UserRoles tables (AppertureDbContext); IUserRepository, IRoleRepository; SqlUserRepository, SqlRoleRepository |
| Audit context | Done | AuthorizationEvaluationContext, AuthorizationEvaluationResult, AuthorizationEvaluationPath |
| Permission catalog | Done | IPlatformPermissionSeeder, PlatformPermissionSeeder, IResourceDescriptorRegistry; module-declared permissions registered at startup via IModuleManifestProvider and ModulePermissionRegistrationHostedService (Epic 5 Prompt 1). Runtime enforcement (Epic 5 Prompt 2): module/workflow operations use IAuthorizationService and consume registered module permissions. |
| Connector domain (Phase 1) | Done | Connection, Binding, ConnectorDefinition, MappingDefinition, ICapabilitySlot, CapabilityVisibility |
| Host auth scaffolding | Done | Management.Host: AddAppertureAuthentication (Cookie + OpenIdConnect when Entra enabled), AddAuthorization, UseAuthentication, UseAuthorization; GET /signin, /signout; real Entra OIDC and local POST /api/auth/local/login |
| Endpoint protection | Done | [Authorize] on Connections, Bindings, Enroll, Workflows, Runs, Modules; service-layer permission checks (IAuthorizationService) on Connections, Bindings, module list/workflow list, workflow start; 403 on deny. [AllowAnonymous] on Bootstrap first-admin, Enroll POST, Auth local/login, local/logout, auth/break-glass |

## Executable required-permissions (Epic 6)

Workflows and operations can declare **definition-level required permissions**. Semantics are shared:

- **If `RequiredPermissions` is non-empty:** ALL listed permissions are required (logical AND).
- **If `RequiredPermissions` is null/empty:** fall back to the executable’s coarse permission model.

### Workflows

- **Model:** `WorkflowDefinition.RequiredPermissions` (optional list of `PermissionDescriptor`).
- **Start authorization:** `WorkflowStartAuthorizationService` resolves the workflow definition; if it declares required permissions, it evaluates ALL of them via `IAuthorizationService`. If it declares none (or the definition cannot be resolved), it falls back to the prior behavior:
  - module-owned workflow → `workflow.execute` at `module:{moduleId}`
  - otherwise → `workflow.start` at instance

### Workflow cancel (cancel control surface)

Cancellation reuses the **same executable permission model** as workflow start/read for the first iteration:

- **If `WorkflowDefinition.RequiredPermissions` is non-empty:** ALL listed permissions are required to request cancel.
- **If `WorkflowDefinition.RequiredPermissions` is null/empty:** fall back to coarse workflow permissions:
  - module-owned workflow → `workflow.execute` at `module:{moduleId}`
  - otherwise → `workflow.start` at instance

Control plane persists durable cancel intent (`POST /api/runs/{runId}/cancel`); the instance-plane worker observes intent and transitions run/instance to the terminal `cancelled` state cooperatively at safe execution boundaries.

### Workflow retry (retry-as-new-run control surface)

Retry reuses the **same executable permission model** as workflow start/cancel for the first iteration:

- **If `WorkflowDefinition.RequiredPermissions` is non-empty:** ALL listed permissions are required to request retry.
- **If `WorkflowDefinition.RequiredPermissions` is null/empty:** fall back to coarse workflow permissions:
  - module-owned workflow → `workflow.execute` at `module:{moduleId}`
  - otherwise → `workflow.start` at instance

Control plane creates a brand-new run and workflow instance via the existing start dispatch pipeline. The original run remains terminal and unchanged; the new run records `RetryOfRunId` so lineage is durable and visible in run queries.

- **Non-idempotent by design:** repeated retry requests against the same original run create additional fresh runs.
- **Not supported:** in-place restart, reopening the original run, or signal-style semantics.

### Workflow lifecycle refinement

Lifecycle refinement does not change the executable-permission model. It makes the existing run/start/cancel/retry transitions explicit and queryable:

- **Run/control history:** start dispatch, cancel request, cancel honor, retry request, completion, and failure are now recorded as durable lifecycle events.
- **Step history:** worker execution persists `WorkflowStepInstances` so the control plane can explain where a run got to, not just the final status.
- **Query visibility:** run detail can now surface both the transition trail and the step trail.

The authorization rule stays the same: lifecycle-related control actions remain governed by the existing executable permissions and module/instance fallback logic. This stage is about making the lifecycle honest, not expanding who can do what.

### Workflow external-control recipient model

The recipient model adds parked/resumed workflow control without changing the executable-permission model:

- **Run/control history:** park request, park application, resume request, and resume application are durably recorded.
- **Recipient state:** a run can be parked at a safe step boundary and later resumed from the saved next step.
- **Query visibility:** run detail now shows the current step pointer plus queued/applied external-control requests.

This is still not signal. It is the runtime-side recipient shape that later signal semantics will need to reuse instead of inventing fake persistence-only behavior.

- **Deny path:** `POST /api/runs/{id}/park` and `POST /api/runs/{id}/resume` surface `UnauthorizedAccessException` from `IWorkflowExternalControlAuthorizationService` as **403** (same pattern as retry/cancel).

### Operations

- **Model:** `OperationDefinition.RequiredPermissions` (already present).
- **Execution:** full operation execution remains deferred; Epic 6 establishes the shared semantics so future operation dispatch evaluates ALL required permissions when present and otherwise uses a coarse operation permission model.

### Catalog coherence

Definition-level permissions must remain coherent with the grantable catalog. If a definition references a permission that is not present in the active `IResourceDescriptorRegistry`, authorization denies (403 path), rather than silently allowing execution.
| Auth audit baseline | Done | IAuditService → SqlAuditService; auth events (auth.entra.login.success, auth.local.login.success/failed, auth.break_glass.login.success/failed, auth.unlinked_principal, auth_admin.blocked.*) persisted to AuditLogs |
| Break-glass path | Done | Human session with break-glass claim; RbacAuthorizationPolicyEvaluator grants all permission checks when actor is Human and IsBreakGlassSession is true. Local-auth-only; dedicated endpoint. |
| Lockout prevention | Done | UserManagementService blocks removal of last platform admin role and disabling last admin’s only local credential; blocked attempts audited. |

## Project/Layer Placement

| Concern | Project | Files |
|---------|---------|-------|
| Primitives (Actor, ResourceDescriptor, PermissionDescriptor) | Shared.Kernel | IActor.cs, ResourceDescriptor.cs, PermissionDescriptor.cs |
| Governed resource contracts | Shared.Kernel | IGovernedResource.cs, IGovernedResourceIdentity.cs |
| Domain model (User, Role, Permission) | Core.Domain | Identity/, Trust/ |
| Governance vocabulary | Core.Domain | Governance/ |
| Connector domain types | Core.Domain | Connector/ |
| Application contracts | Core.Application | Services/, Authorization/ |
| Implementations | ControlPlane.Security | ServiceCollectionExtensions.Governance.cs, RbacAuthorizationPolicyEvaluator, DelegatingAuthorizationService, HostPrincipalContext, SqlRoleProvider, SqlPrincipalToUserResolver |
| Identity persistence | Persistence.SqlServer | UserEntity, IdentityLinkEntity, RoleEntity, UserRoleEntity, LocalCredentialEntity; SqlUserRepository, SqlRoleRepository, SqlLocalCredentialRepository, SqlAuditService; migrations AddPlatformIdentityTables, AddLocalCredentialsTable |

## Core Types and Contracts

### Principal/Identity

- **IActor** — Id, Kind, DisplayName
- **ActorKind** — Human, Machine, System, RemoteInstance
- **ActorId** — Stable identifier
- **IPrincipalContext** — GetCurrentActorAsync
- **IIdentityResolutionService** — ResolveAsync (auth context)
- **IPrincipalToUserResolver** — Resolve Human actor to UserId for role lookup

### Roles/Permissions

- **Role** — Id, Name, Permissions (PermissionDescriptor list)
- **Permission** — Action, Resource, ModuleId
- **PermissionDescriptor** — Action, optional ResourceDescriptor
- **IRoleProvider** — GetRoles, GetRolesForUserAsync, Assign/Remove

### Governed Resource Vocabulary

- **IGovernedResource** — GetIdentity()
- **IGovernedResourceIdentity** — ResourceType, ResourceId, ScopeKey, OwnerId
- **GovernedResourceIdentity** — Value object
- **GovernedResourceDescriptor** — ResourceDescriptor + ResourceFamily; FromIdentity()
- **ResourceTypeConstants** — Connection, Binding, Operation, etc.
- **ActionConstants** — connection.create, operation.execute, etc.
- **ScopeKeyConstants** — Instance, Module(), Connection(), Slot()

### Authorization Evaluation

- **IAuthorizationService** — EvaluateAsync(actor, permission, resource, resourceIdentity?) — passes resourceIdentity to policy evaluator for ownership/instance checks
- **IAuthorizationPolicyEvaluator** — EvaluateAsync(actor, permission, resource, resourceIdentity)
- **IScopeContainmentEvaluator** — IsContained(innerScopeKey, outerScopeKey)
- **DelegatingAuthorizationService** — Forwards to policy evaluator
- **RbacAuthorizationPolicyEvaluator** — RBAC + scope containment; when actor is Human and session is break-glass (IsBreakGlassSession), grants all permission checks

### Audit-Aware Context

- **AuthorizationEvaluationContext** — ActorId, Permission, Resource, Result, EvaluationPath
- **AuthorizationEvaluationResult** — Allow, Deny
- **AuthorizationEvaluationPath** — BreakGlass, Relationship, Rbac, ResourceOwnership, None

## Dependency and Boundary Check

| Check | Result |
|-------|--------|
| Auth remains generic | Yes — no connector types in IAuthorizationService, IAuthorizationPolicyEvaluator, IScopeContainmentEvaluator |
| Governed-resource participation generic | Yes — IGovernedResource, IGovernedResourceIdentity are string-based |
| No connector-specific abstractions in auth core | Yes — Connector types are in Core.Domain.Connector; auth uses ResourceType constants |
| No service-locator patterns | Yes — explicit DI; callers pass actor, permission, resource, resourceIdentity |
| No resource-browsing loopholes | Yes — modules resolve via declared slots and bindings; no global browse |

## Seed Naming Guidance

- **Actions:** `{resourceType}.{action}` — connection.create, binding.assign, operation.execute (ActionConstants)
- **Resource types:** PascalCase — Connection, Binding, ConnectorDefinition (ResourceTypeConstants)
- **Scope keys:** instance, module:{id}, connection:{id}, operation:{id}, module:{id}:slot:{name} (ScopeKeyConstants)
- **Principal categories:** ActorKind — Human, Machine, System, RemoteInstance

## Audit Items Addressed or Deferred

| Item | Status | Notes |
|------|--------|-------|
| resourceIdentity never flows to policy | **Addressed** | IAuthorizationService.EvaluateAsync now accepts optional `IGovernedResourceIdentity? resourceIdentity` and passes it through to the policy evaluator. Callers with `IGovernedResource` pass `resource.GetIdentity()`. |
| AuthorizationEvaluationContext never produced | **Deferred** | Intentionally deferred until broader audit integration. No code change. |
| PermissionDescriptor vs domain Permission | **Documented** | `PermissionDescriptor` (Shared.Kernel) is the auth API shape and what `Role.Permissions` uses. Domain `Permission` (Core.Domain.Identity) is a separate record; Role uses PermissionDescriptor to avoid auth depending on domain persistence shape. |
| Ownership checks | **Reserved** | resourceIdentity path enables them; RbacAuthorizationPolicyEvaluator does not yet implement owner checks. Documented as reserved for later. |

## Remaining Gaps (Optional, Not Blocking)

- **AuthorizationEvaluationContext** is defined but not yet populated during evaluation. Populating it would enable future audit emission. Defer until audit integration.
- **Access-denied reasoning** — Current API returns bool. A richer result type (Allow/Deny + reason) could be added later if needed.
- **IAuthorizationService** does not accept IGovernedResource directly; callers use GovernedResourceDescriptor.FromIdentity for resource and resource.GetIdentity() for resourceIdentity. This is intentional and documented.

## Connector Resource Family Integration

The first concrete governed resource family (connectors, connections, slots, bindings) participates in auth via `IGovernedResource` and `IGovernedResourceIdentity`. See [Connector Resource Family](connector-resource-family.md) for details.

- **Resource types:** ConnectorDefinition, Connection, Binding, MappingDefinition, CapabilitySlot (ResourceTypeConstants)
- **Actions:** connection.create/read/update/delete, connector.manage, binding.create/read/delete/assign/revoke, capability.consume, mapping.manage (ActionConstants)
- **Where checks belong:** connector definitions (connector.manage); connection CRUD (connection.*); binding (binding.assign/revoke); slot resolution (capability.consume)
- **Distinctions:** Administrative (connection CRUD, connector.manage), binding/delegation (binding.assign/revoke), operational (capability.consume)

## Principal resolution (Epic 1)

- **Where persisted user/role/identity-link data lives:** SQL Server tables `Users`, `Roles`, `IdentityLinks`, `UserRoles` (see migration `AddPlatformIdentityTables`). AppertureDbContext in Persistence.SqlServer.
- **How principal resolution works:** For the Management Host, `HostPrincipalContext` reads `HttpContext.User` (ClaimsPrincipal). `ClaimsPrincipalToActorMapper` maps standard claims (NameIdentifier/sub, name/preferred_username) to a Human `IActor`. `SqlPrincipalToUserResolver` then looks up `IdentityLinks` by (IdentitySource, ExternalId) where ExternalId = actor.Id, and returns the linked platform `UserId`. Role lookup uses `SqlRoleProvider` and `UserRoles` + `Roles` tables. Host auth is currently a **placeholder scheme** (no login); replace with Entra or local auth in a later epic.

## Deferred Items

- **Full Entra interactive flow** and **full local username/password auth** (login UI, token handling, password hashing)
- Break-glass policy execution in the evaluator — **Implemented (Epic 4):** evaluator grants when Human actor has break-glass session; lockout-prevention guards in UserManagementService
- Relationship and resource-ownership evaluation
- Machine/System actor role resolution
- Full audit emission of auth decisions
- Connector management UI
- Policy authoring, ABAC

## Next Phase: Connector/Connection Integration

Connector persistence and workflows are in place. Remaining auth-related follow-on:

1. **Replace placeholder auth:** Add Entra or local auth scheme in Management.Host; set as DefaultAuthenticateScheme so HostPrincipalContext resolves a real user.
2. **First platform user:** Insert a row into `Users`, one or more into `IdentityLinks` (Source + ExternalId matching the auth provider’s subject/id), and assign a role via `UserRoles` (e.g. `role-platform-administrator`). No first-run wizard in this epic; document or provide a small admin API/CLI in a later epic.

## Related

- [ADR-0012](../02-decisions/) — ResourceDescriptor vs IGovernedResourceIdentity; evaluation order
- [ADR-0013](../02-decisions/) — Action naming; scope key formats
- [Next Stage: Platform Auth Readiness](next-stage-platform-auth.md)
