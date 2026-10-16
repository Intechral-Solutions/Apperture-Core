# Module Manifest Spec

The module manifest is the **`ModuleManifest`** record returned by `IModule.GetManifest()` ([`Apperture.ModuleSdk.Abstractions`](../../src/Apperture.ModuleSdk/Apperture.ModuleSdk.Abstractions/ModuleManifest.cs)). It declares metadata the platform registers at host startup: permissions, signals, procedures, governance contributions, and optional connector documentation.

**Authoring guide:** [module-authoring-guide.md](./module-authoring-guide.md)  
**Signal conventions:** [ADR-0038](../02-decisions/ADR-0038-platform-signals-sdk-conventions.md)  
**Signal catalog:** [platform-signal-catalog.md](../01-architecture/platform-signal-catalog.md)

---

## Required fields

| Field | Rule |
|-------|------|
| **Id** | Unique module identifier (e.g. `Sample.HelloWorld`) |
| **Name** | Display name |
| **Version** | Semantic version string |
| **Description** | Human-readable summary |

---

## Optional fields (shipped)

| Property | Type | Purpose |
|----------|------|---------|
| **Permissions** | `ModulePermissionDeclaration[]` | RBAC permissions registered at scope `module:{Id}` |
| **Signals** | `ModuleSignalDeclaration[]` | Module-authored platform event types (declare before emit) |
| **Procedures** | `ModuleProcedureDeclaration[]` | Integration-scoped operator launch definitions |
| **GovernedResources** | `ModuleGovernedResourceDeclaration[]` | Resource types contributed to governance catalog |
| **GovernedActions** | `ModuleGovernedActionDeclaration[]` | Action keys mapped to resource types |
| **ConnectorDefinitions** | `ConnectorDefinitionDeclaration[]` | Documentation/tooling only (not a runtime registry) |

### Future (not on manifest today)

| Field | Status |
|-------|--------|
| **Capabilities** | Not implemented |
| **Configuration schema** | Future JSON Schema for UI-driven module config; credentials as `SecretRef`, not plaintext |

---

## Example (Sample.HelloWorld)

```csharp
public ModuleManifest GetManifest() => new(ModuleId, "Hello World", "1.0.0", "Validate → DoWork → Complete")
{
    Permissions =
    [
        new ModulePermissionDeclaration(
            "workflow.execute", "WorkflowInstance",
            "Run Hello World workflows",
            "Run the workflows provided by the Hello World module.",
            PermissionSensitivity.Elevated),
        new ModulePermissionDeclaration(
            ActionConstants.OperationExecute, ResourceTypeConstants.OperationDefinition,
            "Run Hello World operations",
            "Run the operations provided by the Hello World module.",
            PermissionSensitivity.Elevated)
    ],
    Signals =
    [
        new ModuleSignalDeclaration(
            "demo.completed",
            "Demo completed",
            "Emitted when Complete runs with emitDemoPlatformEvent=true.",
            "info",
            RequiredDataProperties: ["message"])
    ],
    Procedures =
    [
        new ModuleProcedureDeclaration(
            "Sample.HelloWorld.procedures.greet",
            "Greet",
            "Runs HelloWorld with a name in operator output.",
            Category: "Demo",
            ProcedureLaunchKind.Workflow,
            DefinitionId.Value,
            """{"fields":[{"key":"name","type":"string","required":true,"label":"Name"}]}"""),
        new ModuleProcedureDeclaration(
            "Sample.HelloWorld.procedures.opPing",
            "Operation ping",
            "Starts the DoWork operation with a message.",
            Category: "Demo",
            ProcedureLaunchKind.Operation,
            OperationDefId.Value,
            """{"fields":[{"key":"message","type":"string","required":true,"label":"Message"}]}""")
    ]
};
```

**Proof.CampusNexus.StudentReview** contributes `Permissions` and `Procedures` but no `Signals` today — operational diagnostics use `context.Signals.Logs` only.

---

## Permissions (implemented)

Each **ModulePermissionDeclaration**:

| Member | Required | Rule |
|--------|----------|------|
| **Action** | Yes | Lowercase, at least two dot-separated segments (ADR-0013), e.g. `workflow.execute` |
| **ResourceType** | Yes | e.g. `WorkflowInstance`, `OperationDefinition`; registration fails when it is omitted |
| **DisplayName** | Yes | What an operator sees in the role editor; at most 80 characters. Name the module, because every module declares `workflow.execute` |
| **Description** | Yes | One sentence saying what holding the permission allows; at most 300 characters |
| **Sensitivity** | No | `Standard` (default), `Elevated` or `Critical`. A declared classification for operators, not a security boundary against the module (modules are trusted in-process). Running workflows or operations is `Elevated` |
| **RequiresStepUp** | No | Default `false`. Declares that exercising the permission needs recent strong authentication |

A declaration without a display name or description, or with an undefined sensitivity, fails registration with an error that names the module and the action. A module cannot decline the administrator default: every module permission is held by the Platform Administrator role. Names and descriptions are product text: they must not contain planning labels, epic or track identifiers, or migration names.

**Registration:** `ModulePermissionRegistrar` at Management/worker startup into `IResourceDescriptorRegistry` with scope `module:{moduleId}`. The catalog entry is a `PermissionDefinition` with origin `Module` and the module id; the governance catalog endpoint reports source and module id from that origin, not from the scope key.

**Runtime enforcement (Epic 5):**

- `GET /modules/{id}/workflows` — module permission at `module:{id}`
- `POST /workflows/{workflowId}/start` — module workflow permission when definition is module-owned

See [Epic 5 Prompt 2 summary](../02-decisions/Epic5-prompt2-module-workflow-enforcement-summary.md).

---

## Signals (implemented)

Each **ModuleSignalDeclaration**:

| Member | Required | Rule |
|--------|----------|------|
| **Name** | Yes | Short name: `[a-z0-9]+(\.[a-z0-9]+)*` (e.g. `demo.completed`) |
| **DisplayName** | Yes | Non-empty operator title |
| **Description** | Yes | Non-empty |
| **DefaultSeverity** | Yes | `critical`, `error`, `warning`, or `info` |
| **RequiredDataProperties** | No | camelCase JSON property names required in emit payload |
| **RequiresIntegrationId** | No | Catalog/metadata hint |
| **RequiresRunId** | No | Catalog/metadata hint |

**Full event type:** `ModulePlatformEventTypes.Format(manifest.Id, Name)` → `apperture.module.{normalizedModuleId}.{signalName}`.

**Validation:** `ModuleSignalManifestValidator.ThrowIfInvalid` at worker/Management startup when platform events are wired. Duplicate full types across modules in one host fail startup.

**Emit:** Handlers use `context.Signals.Events.EmitAsync(context.Signals.Correlation, signalName, payload)` with the **short Name** only. Undeclared names fail at emit time.

See [module-authoring-guide §6](./module-authoring-guide.md#6-platform-signals) and [ADR-0038](../02-decisions/ADR-0038-platform-signals-sdk-conventions.md).

---

## Procedures (implemented)

Each **ModuleProcedureDeclaration**:

| Member | Required | Rule |
|--------|----------|------|
| **Id** | Yes | Stable procedure id (e.g. `Sample.HelloWorld.procedures.greet`) |
| **DisplayName** | Yes | Operator-facing label |
| **Description** | No | Help text |
| **Category** | No | Grouping in Integration UI |
| **LaunchKind** | Yes | `ProcedureLaunchKind.Workflow` or `Operation` |
| **TargetDefinitionId** | Yes | Workflow or operation definition id |
| **ParameterSchema** | No | JSON schema for operator forms (see below) |
| **RequiredPermissions** | No | Optional override; platform may merge with target definition permissions |

Procedures are registered via `IProcedureRegistryService` from enabled module manifests and launched per integration through Management API/UI.

---

## ParameterSchema (operations and procedures)

Used on **OperationDefinition.ParameterSchema** (code) and **ModuleProcedureDeclaration.ParameterSchema** (manifest). Not on `WorkflowDefinition`.

JSON shape:

```json
{
  "fields": [
    {
      "key": "message",
      "type": "string",
      "required": false,
      "label": "Message",
      "helpText": "Shown on the start form.",
      "defaultValue": null,
      "order": 0,
      "allowedValues": null,
      "min": null,
      "max": null,
      "maxLength": null
    }
  ]
}
```

| Field key | Supported `type` values |
|-----------|-------------------------|
| `key` | Required identifier |
| `type` | `string`, `int`, `decimal`, `bool`, `datetime`, `enum`, `json` |
| `label`, `helpText`, `required`, `defaultValue`, `order` | Form metadata |
| `allowedValues` | Enum-like constraints |
| `min`, `max`, `maxLength` | Numeric/string bounds |

Server validation: `OrchestrationParameterSchemaValidator` on start/procedure launch. UI: Execute page and Integration Detail (`ProcedureParameterForm`).

---

## Governed resources and actions (implemented)

### GovernedResources

**ModuleGovernedResourceDeclaration:**

| Member | Rule |
|--------|------|
| **ResourceType** | Required, non-empty (PascalCase name) |
| **ScopeKey** | Default scope; if empty, host uses `module:{moduleId}` |

Registers a resource descriptor in `IResourceDescriptorRegistry` (`ResourceFamily.Core`).

### GovernedActions

**ModuleGovernedActionDeclaration:**

| Member | Rule |
|--------|------|
| **Action** | Required; must be **lowercase** dot-separated key |
| **ResourceType** | Required, non-empty |
| **DisplayName** | Required; at most 80 characters |
| **Description** | Required; one sentence, at most 300 characters |
| **Sensitivity** | Optional; `Standard` (default), `Elevated` or `Critical` |
| **RequiresStepUp** | Optional; default `false` |

Registers a permission at scope `module:{moduleId}` with the same metadata rules as a `ModulePermissionDeclaration`. (The former `IsPrivileged` flag was never read and has been removed.)

Invalid declarations cause `ModulePermissionRegistrar.Register` to throw `InvalidOperationException` at startup with module-scoped diagnostics.

---

## ConnectorDefinitions (documentation only)

**ConnectorDefinitionDeclaration:** `ConnectorDefinitionId`, `DisplayName`, optional `Description`.

Does not register runtime connectors. Host registers connector factories via DI (e.g. `AddAppertureCampusNexusConnector`). Manifest entries document which connector ids the module expects for bindings.

---

## Format

- **In code:** `ModuleManifest` record with property initializers (primary authoring path).
- **JSON / embedded resource:** TBD for external tooling.

---

## Related validation and startup

| Validator / registrar | Applies to |
|----------------------|------------|
| `ModulePermissionRegistrar` | Permissions, GovernedResources, GovernedActions |
| `ModuleSignalManifestValidator` | Signals |
| `ModuleDescriptorBuilder` | Duplicate workflow/operation ids in module contributions |
| `WorkerHostStartupValidation` | Module list, handler ownership per workflow steps |

---

## Deferred / future

- Per-workflow-definition permission modeling in manifest
- Menu filtering by permission
- Manifest-shipped built-in notification definitions (ADR-0037 §7 — not in module manifests today)
- Configuration schema property on `ModuleManifest`
