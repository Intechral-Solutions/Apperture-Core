# Epic 6: Executable Permission Modeling and Workflow Consumption — Implementation Summary

## Status

Implemented. Workflows can now declare definition-level required permissions; workflow start consumes those permissions with explicit fallback behavior when none are declared. The model is aligned with `OperationDefinition.RequiredPermissions`.

## Workflow definition metadata changes

- **WorkflowDefinition** now includes optional `RequiredPermissions`:
  - `IReadOnlyList<PermissionDescriptor>? RequiredPermissions`
  - Backward-compatible via default value on the record constructor.

## Shared executable permission semantics

Executable required-permissions semantics are unified:

- **Non-empty RequiredPermissions:** ALL permissions must pass (logical AND).
- **Null/empty RequiredPermissions:** fall back to coarse executable authorization.

Catalog coherence rule:

- If any required permission referenced by a definition is not present in the active `IResourceDescriptorRegistry`, evaluation denies (403 path).

## Workflow start runtime behavior

`WorkflowStartAuthorizationService` now:

1. Resolves the current actor via `IPrincipalContext`.
2. Resolves the workflow definition via `IWorkflowDefinitionResolver`.
3. If the definition declares `RequiredPermissions`:
   - Evaluates ALL of them via `IExecutablePermissionEvaluator` + `IAuthorizationService`.
   - Denies on any failure or unknown catalog permission.
4. Otherwise, uses the existing fallback unchanged:
   - module-owned workflow → `workflow.execute` at `module:{moduleId}`
   - otherwise → `workflow.start` at instance

## Operation alignment

Operations already have `OperationDefinition.RequiredPermissions`. Epic 6 does **not** implement operation execution, but establishes the shared “definition-level required permissions + fallback” semantics so future operation dispatch can use the same pattern.

## Catalog/validation behavior

- Definition-level required permissions are checked against the active permission catalog (`IResourceDescriptorRegistry`) during evaluation.
- Unknown required permissions deny (403 path), ensuring definitions cannot rely on non-grantable permissions.

## Sample proof

`Sample.HelloWorld` now declares `WorkflowDefinition.RequiredPermissions` using the existing module-declared permission `workflow.execute` scoped to `module:Sample.HelloWorld`.

## Tests

Added unit coverage for:

- Fallback behavior when `RequiredPermissions` is null/empty.
- Evaluation of a single required permission.
- ALL-of semantics with multiple required permissions.
- Unknown required permission denies.

## Deferred beyond Epic 6

- Full workflow runtime and step execution engine.
- Operation execution runtime (`IExecutionDispatchService` implementation).
- Runs/OperationRun read permission modeling and broader run authorization.
- Menu filtering by permission.
- Richer UX/admin surfaces for executable-permission authoring and role assignment.

## Files changed (summary)

- **RuntimePlane.Abstractions:** `WorkflowDefinition.cs` (adds `RequiredPermissions`).
- **ControlPlane.Security:** `IExecutablePermissionEvaluator.cs`, `ExecutablePermissionEvaluator.cs`; `IWorkflowDefinitionResolver.cs`, `WorkflowDefinitionResolver.cs`; `WorkflowStartAuthorizationService.cs` (consumes definition-level required permissions and preserves fallback); `ServiceCollectionExtensions.Governance.cs` (DI wiring).
- **Sample.HelloWorld.Module:** `HelloWorldModule.cs` (sets definition-level `RequiredPermissions`); csproj adds Shared.Kernel reference.
- **Docs:** `authorization-and-governance-foundation.md` updated; this Epic 6 summary added.
- **Tests:** `WorkflowStartAuthorizationServiceTests.cs` added.

