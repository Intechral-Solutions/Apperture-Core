# Epic 5 Prompt 2: Module and Workflow Authorization Enforcement — Implementation Summary

## Status

Implemented. Registered module permissions are enforced at runtime on module- and workflow-related API operations. At least one path consumes a real module-declared permission (Sample.HelloWorld’s `workflow.execute`).

## Endpoints and permissions enforced

| Endpoint | Permission | Scope | Source |
|----------|------------|--------|--------|
| GET /modules | module.configure | instance | Platform |
| GET /modules/{id}/workflows | workflow.execute | module:{id} | Module-declared |
| POST /workflows/{workflowId}/start | workflow.execute or workflow.start | module:{moduleId} or instance | Module-declared or platform |

- **GET /modules** — Requires `module.configure` at instance. Unauthorized → 403.
- **GET /modules/{id}/workflows** — Requires `workflow.execute` (module-declared) at scope `module:{id}` with resource type WorkflowInstance. Unauthorized → 403. This path consumes the permission registered from Sample.HelloWorld’s manifest.
- **POST /workflows/{workflowId}/start** — Resolves owning module via `IWorkflowModuleResolver`. If the workflow is owned by a known module, requires `workflow.execute` at `module:{moduleId}`; otherwise requires platform `workflow.start` at instance. Unauthorized → 403. For `Sample.HelloWorld.HelloWorld`, the check uses the module-declared `workflow.execute` at `module:Sample.HelloWorld`.

## Service-layer seams added

- **IWorkflowModuleResolver** / **WorkflowModuleResolver** — Resolves `WorkflowDefinitionId` to owning module id by enumerating known modules and their workflow definitions. Registered as singleton; used by workflow start authorization and could be reused for other module-scoped workflow operations.
- **IModuleWorkflowListingService** / **ModuleWorkflowListingService** — Lists modules (with `module.configure` check) and workflows per module (with `workflow.execute` at `module:{id}` check). Registered as scoped; used by ModulesController.
- **IWorkflowStartAuthorizationService** / **WorkflowStartAuthorizationService** — `EnsureCanStartWorkflowAsync(workflowDefinitionId)` resolves actor and module, then evaluates `workflow.execute` at module scope or `workflow.start` at instance. Registered as scoped; used by WorkflowsController.

All follow the existing pattern: resolve actor via `IPrincipalContext`, call `IAuthorizationService.EvaluateAsync`, throw `UnauthorizedAccessException` on deny; controllers catch and return 403.

## Platform admin role repair

**PlatformAdminRoleRepairService** was extended to add all registry permissions whose scope key starts with `module:` to the platform administrator role (after auth-admin permissions and deduplication). So platform admins automatically receive every module-declared permission (e.g. `workflow.execute` at `module:Sample.HelloWorld`) and can pass the new checks. Module permission registration runs before platform admin repair (hosted service order is unchanged).

## How the module-declared permission is consumed at runtime

1. **Declaration** — Sample.HelloWorld’s manifest declares `Permissions = [ new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance") ]`.
2. **Registration** — At host startup, `ModulePermissionRegistrationHostedService` registers this into `IResourceDescriptorRegistry` with scope `module:Sample.HelloWorld` (Prompt 1).
3. **Role** — `PlatformAdminRoleRepairService` adds that permission (and any other module-scoped permissions from the registry) to the platform admin role so users with that role have it.
4. **Enforcement** — When a caller hits GET /modules/Sample.HelloWorld/workflows or POST /workflows/Sample.HelloWorld.HelloWorld/start, the service resolves the current actor and calls `IAuthorizationService.EvaluateAsync(actor, new PermissionDescriptor("workflow.execute", new ResourceDescriptor("WorkflowInstance", "module:Sample.HelloWorld")), ...)`. The evaluator matches the role’s permission (same action, resource type, scope containment); allowed → proceed, denied → 403.

So the end-to-end path (module declares → host registers → role receives → runtime evaluates) is real.

## Docs updated

- **docs/05-modules/module-manifest-spec.md** — Added “Runtime enforcement (Epic 5 Prompt 2)” and link to this summary; noted deferred items.
- **.ai/playbooks/module-sdk.md** — Updated “Module permission declaration” to state that registered permissions are enforced at runtime and linked to manifest spec and this summary.
- **docs/01-architecture/authorization-and-governance-foundation.md** — Updated Permission catalog and Endpoint protection rows to reflect module/workflow enforcement and use of registered module permissions.

## Deferred beyond Epic 5

- Broader per-workflow-definition required-permission modeling (e.g. per-definition permission override or additional actions).
- Menu filtering by permission.
- Richer module admin or role UX (e.g. UI to assign module permissions to custom roles).
- Full install/uninstall persistence and dynamic module discovery.

## Files changed (summary)

- **ControlPlane.Security:** PlatformAdminRoleRepairService.cs (registry, EnsureModuleScopedPermissions); IWorkflowModuleResolver.cs, WorkflowModuleResolver.cs; IModuleWorkflowListingService.cs, ModuleWorkflowListingService.cs; IWorkflowStartAuthorizationService.cs, WorkflowStartAuthorizationService.cs; ServiceCollectionExtensions.Governance.cs (register new services).
- **Management.Api:** ModulesController.cs (inject IModuleWorkflowListingService, call GetModulesAsync/GetWorkflowsForModuleAsync, 403 on UnauthorizedAccessException; ModuleSummaryDto, WorkflowSummaryDto); WorkflowsController.cs (inject IWorkflowStartAuthorizationService, call EnsureCanStartWorkflowAsync before validation/501, 403 on UnauthorizedAccessException).
- **Docs:** module-manifest-spec.md, .ai/playbooks/module-sdk.md, authorization-and-governance-foundation.md; new Epic5-prompt2-module-workflow-enforcement-summary.md.
