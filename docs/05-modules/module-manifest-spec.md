# Module Manifest Spec

## Fields

- **Id** — Unique module identifier
- **Name** — Display name
- **Version** — Semantic version
- **Description** — Human-readable description
- **Permissions** — Optional list of permissions the module declares (see below). Registered at host startup into `IResourceDescriptorRegistry` with scope `module:{Id}`. Omitted or empty for modules that do not contribute permissions.
- **Capabilities** — List of capabilities the module provides (future)
- **Configuration schema** — Future: JSON Schema for UI-driven config
  - Sensitive configuration values must be represented as `SecretRef`-style references.
  - Plaintext credentials must not be transported from Control Plane to Worker by default.

## Permissions (implemented)

Each item is a **ModulePermissionDeclaration** with:

- **Action** (required) — Permission action string per ADR-0013: lowercase, at least two dot-separated segments (e.g. `workflow.execute`, `helloworld.run`).
- **ResourceType** (optional) — Resource type for the permission descriptor (e.g. `WorkflowInstance`, `OperationDefinition`). If omitted, the host uses a default at registration time.

The host discovers known modules (e.g. via DI-registered `IModule` instances), reads each manifest’s `Permissions`, validates declarations, and registers them into the single platform `IResourceDescriptorRegistry` with scope `ScopeKeyConstants.Module(moduleId)`. Declaration format and validation follow ADR-0013.

## Runtime enforcement (Epic 5 Prompt 2)

Registered module permissions are enforced at runtime. The same `IAuthorizationService` and RBAC model used for platform operations (e.g. connections, bindings) are used for module- and workflow-related API operations:

- **GET /modules/{id}/workflows** — Requires the module-declared permission (e.g. `workflow.execute`) at scope `module:{id}`. Unauthorized callers receive 403.
- **POST /workflows/{workflowId}/start** — When the workflow is owned by a known module, requires that module’s declared permission (e.g. `workflow.execute`) at scope `module:{moduleId}`; otherwise requires platform `workflow.start` at instance scope. Unauthorized callers receive 403.

Evaluation uses the same descriptor shape as registered (action, resource type, scope). The platform administrator role is repaired at startup to include all module-scoped permissions from the registry, so platform admins can perform module operations. See [Epic 5 Prompt 2 summary](../02-decisions/Epic5-prompt2-module-workflow-enforcement-summary.md).

**Deferred:** Broader per-workflow-definition required-permission modeling, menu filtering by permission, and richer module admin or role UX.

## Format

In code, the manifest is the `ModuleManifest` record (ModuleSdk.Abstractions); optional `Permissions` is set via the property initializer or a constructor overload. JSON or embedded resource format for other tooling remains TBD.
