# Epic 5 Prompt 1: Module Permission Declaration and Registration — Implementation Summary

## Status

Implemented. Modules can declare permissions in the manifest; the host registers them at startup into the existing `IResourceDescriptorRegistry`. No endpoint enforcement in this prompt.

## Manifest and model changes

- **ModulePermissionDeclaration** (ModuleSdk.Abstractions): New record with `Action` (required) and `ResourceType` (optional). Used to build `PermissionDescriptor` at registration time.
- **ModuleManifest**: Extended with optional property `IReadOnlyList<ModulePermissionDeclaration>? Permissions`. Existing call sites using the positional constructor remain valid (Permissions is null).

## Startup registration path

- **IModuleManifestProvider**: Interface that returns `IReadOnlyList<(string ModuleId, ModuleManifest Manifest)>`. Implementations supply known modules (e.g. from DI).
- **KnownModuleManifestProvider**: Takes `IEnumerable<IModule>`; returns each module’s Id and `GetManifest()`.
- **ModulePermissionRegistrar**: Static class that takes the registry and the list of (ModuleId, Manifest); for each manifest with non-empty Permissions, validates each declaration and calls `registry.RegisterPermission(...)` with scope `ScopeKeyConstants.Module(moduleId)` and family `ResourceFamily.Operation`. Deduplicates by (Action, ResourceType, ScopeKey).
- **ModulePermissionRegistrationHostedService**: IHostedService that runs once at startup; resolves `IResourceDescriptorRegistry` and `IModuleManifestProvider`, gets manifests, and calls `ModulePermissionRegistrar.Register`.
- **AddAppertureGovernance**: Registers the hosted service and a default empty `IEnumerable<IModule>` and `IModuleManifestProvider` so the host starts without modules. Hosts that have modules call **AddKnownModules(services, modules)** after AddAppertureGovernance to replace with the real module list and provider.
- **AddKnownModules**: Extension method that registers the given `IEnumerable<IModule>` and `IModuleManifestProvider` (KnownModuleManifestProvider), so module permissions are discovered and registered at startup.

## Validation rules

- **Action:** Non-null, non-whitespace; must be lowercase; must have at least two dot-separated segments (ADR-0013 style). Invalid declarations cause `InvalidOperationException` with a clear message.
- **ResourceType:** If provided, must be non-whitespace. If omitted, host uses `ResourceTypeConstants.OperationDefinition`.
- **Scope:** Always `ScopeKeyConstants.Module(moduleId)`; never taken from the declaration.

## Sample module proof

- **Sample.HelloWorld**: `GetManifest()` now returns a manifest with `Permissions = [ new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance") ]`.
- **Management.Host**: References Sample.HelloWorld.Module and calls `AddKnownModules([ new HelloWorldModule() ])` after `AddAppertureGovernance()`.
- **Verification:** After the Management host starts, the hosted service runs and registers the permission. The registry can be inspected (e.g. resolve `IResourceDescriptorRegistry` and call `GetPermissionDescriptors()`) to confirm a permission with action `workflow.execute` and scope `module:Sample.HelloWorld` is present.

## Docs and playbook updates

- **docs/05-modules/module-manifest-spec.md**: Documented Permissions field, ModulePermissionDeclaration shape, and that registration is host-driven with scope `module:{Id}`; noted endpoint enforcement deferred to Prompt 2.
- **.ai/playbooks/module-sdk.md**: Updated "Resource and permission registration" to describe the implemented path (declare in manifest, host registers at startup); added "Module permission declaration" guidance.
- **docs/01-architecture/authorization-and-governance-foundation.md**: Permission catalog row updated to mention module-declared permissions and Epic 5 Prompt 1; endpoint enforcement deferred to Prompt 2.

## Deferred to Prompt 2

- Endpoint protection for module/workflow APIs using the registered permissions.
- Runtime authorization hooks that evaluate module-declared permissions on requests.
- Per-workflow required-permission enforcement (if any).
- Full module install/uninstall persistence and dynamic discovery.

## Files changed (summary)

- **ModuleSdk.Abstractions:** ModulePermissionDeclaration.cs (new), ModuleManifest.cs (optional Permissions property).
- **ControlPlane.Security:** IModuleManifestProvider.cs, KnownModuleManifestProvider.cs, ModulePermissionRegistrar.cs, ModulePermissionRegistrationHostedService.cs; ServiceCollectionExtensions.Governance.cs (hosted service, default empty provider, AddKnownModules); csproj (reference ModuleSdk.Abstractions).
- **Management.Host:** Program.cs (AddKnownModules with HelloWorldModule), csproj (reference Sample.HelloWorld.Module).
- **Sample.HelloWorld.Module:** HelloWorldModule.cs (manifest with Permissions).
- **Tests:** tests/Apperture.UnitTests/Governance/ModulePermissionRegistrarTests.cs (registration, default resource type, skip empty, invalid action throws).
- **Docs:** module-manifest-spec.md, .ai/playbooks/module-sdk.md, authorization-and-governance-foundation.md, Epic5-prompt1-module-permission-registration-summary.md (this file).

## Related

- [ADR-0013](ADR-0013-action-and-scope-naming-conventions.md) — Action and scope naming
- [Module Manifest Spec](../05-modules/module-manifest-spec.md)
- [.ai/playbooks/module-sdk.md](../../.ai/playbooks/module-sdk.md)
