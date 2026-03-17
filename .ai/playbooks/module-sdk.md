# Module SDK

## Module structure

A module provides:

- Workflow definitions ("procedures")
- Step implementations
- Optional connectors/adapters
- Optional UI metadata (nav links, screens)
- Permission/resource declarations

## Resource and permission registration (implemented)

Modules **declare** permissions in the **manifest** via the optional `Permissions` property (list of `ModulePermissionDeclaration`: Action, optional ResourceType). The host discovers known modules (e.g. DI-registered `IModule` instances), reads each manifest’s declared permissions, validates them per ADR-0013, and registers them at startup into the single `IResourceDescriptorRegistry` with scope `module:{moduleId}`. Modules do not receive the registry or call `RegisterPermission` themselves; registration is host-driven from manifest data.

**Module permission declaration:** Add a `Permissions` list to your `ModuleManifest` (e.g. in `GetManifest()`). Each entry must have a valid Action (lowercase, at least two dot-separated segments). ResourceType is optional. Registration is automatic for modules that are registered with the host (e.g. via `AddKnownModules`). **Runtime enforcement:** Registered permissions are enforced on module/workflow API operations (e.g. listing workflows for a module, starting a workflow). The same `IAuthorizationService` and RBAC model apply; see [module-manifest-spec](../../docs/05-modules/module-manifest-spec.md) and [Epic 5 Prompt 2 summary](../../docs/02-decisions/Epic5-prompt2-module-workflow-enforcement-summary.md).

**Constraints:**
- Do not browse arbitrary global resources at runtime.
- Resolve only explicitly declared and bound dependencies (slots, connections).
- Binding is an explicit delegation mechanism; do not bypass it.

See auth and resource governance plan Section 7.2 (Forbidden) and 7.3 (Anti-Patterns).

## Metadata (manifest)

- Id, Name, Version, Description
- Permissions (optional) — list of ModulePermissionDeclaration; registered at host startup
- Capabilities (future)
- Configuration schema (future UI-driven config)

## Workflow registration

Modules register workflow definitions with the runtime. Each definition includes:

- Typed parameter schema (for UI-generated forms)
- Step graph (linear sequence first; extensible later)
- Operational attributes: category (Query vs Command), required permissions, visibility, retry policy, idempotency rules

## Step implementation

Steps are the unit of execution. The runtime invokes them in order according to the workflow definition. Steps receive context and parameters; they return results that feed the next step or the final OperationResult.

## Deployment

- **On-prem:** Modules are compiled-in projects referenced by the worker host. One module per process.
- **Cloud:** Modules are deployed as their own Function App. "Install" is deployment configuration.
