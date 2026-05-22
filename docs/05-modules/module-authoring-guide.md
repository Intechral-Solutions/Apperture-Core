# Module Authoring Guide

See [.ai/playbooks/module-sdk.md](../../.ai/playbooks/module-sdk.md) and [module-manifest-spec.md](./module-manifest-spec.md).

## Steps

1. Create module project(s) — Module, Contracts, optional UI
2. Define workflow(s) with parameter schema and step graph
3. Implement step handlers
4. Register workflows with the runtime
5. Provide manifest metadata

Sample.HelloWorld demonstrates the pattern.

## Execution Contract Authoring (Epic 23+)

Use one explicit model for operator-visible run semantics.

### Preferred patterns

- **Success intent:** return `OperatorExecutionResult.SuccessInline(...)`, `SuccessSummaryOnly(...)`, or `SuccessWithheld()`.
- **Failure intent:** throw `OperatorExecutionResult.FailInline(...)`, `FailSummaryOnly(...)`, or `FailWithheld(...)`.

### Supported fallback patterns

- `OperatorRunOutput` remains a supported compatibility path for success intent, but it is not the preferred authoring surface for new handlers.
- Plain successful return values (`object?`) are still supported for backward compatibility.
- Ordinary exceptions are still supported and are captured by default failure behavior.

### Discouraged patterns

- New handlers relying on plain successful returns or ordinary exceptions when declared intent is feasible.
- Using implicit behavior where operator intent (inline vs summary-only vs withheld) should be explicit.

### Quick selection matrix

| Goal | Preferred authoring |
|---|---|
| Show inline operator payload on success | `return OperatorExecutionResult.SuccessInline(payload);` |
| Show only a success headline | `return OperatorExecutionResult.SuccessSummaryOnly(payload);` |
| Intentionally suppress success payload | `return OperatorExecutionResult.SuccessWithheld();` |
| Fail with summary-only diagnostics | `throw OperatorExecutionResult.FailSummaryOnly(summary);` |
| Fail with inline-safe diagnostics payload | `throw OperatorExecutionResult.FailInline(summary, diagnostics);` |
| Fail while withholding diagnostics payload | `throw OperatorExecutionResult.FailWithheld(summary);` |

### Copyable snippets (preferred + compatibility)

```csharp
// Preferred success pattern for new handlers:
return OperatorExecutionResult.SuccessInline(new
{
    message = "Operation completed.",
    step = "DoWork"
});
```

```csharp
// Preferred declared failure pattern for new handlers:
throw OperatorExecutionResult.FailInline(
    "Validation failed.",
    new { reason = "validation.missing_required_field" });
```

```csharp
// Supported compatibility path (not preferred for new handlers):
return OperatorRunOutput.SummaryOnly(new { message = "Compatibility-mode summary." });
```

## Platform Signals (Epic 103 — shipped)

Step handlers receive **`IStepExecutionContext`**. Module-authored signals use **`context.Signals`** only — do not resolve `IModulePlatformEventEmitter`, `IModuleOperationalLogger`, or `IModuleTelemetry` from DI inside handlers.

Canonical conventions: [ADR-0038](../02-decisions/ADR-0038-platform-signals-sdk-conventions.md). Copyable handler snippets and anti-patterns: [execution-contract-handler-patterns.md](./execution-contract-handler-patterns.md). Catalog and implementation status: [platform-signal-catalog.md](../01-architecture/platform-signal-catalog.md).

**Sample:** `Sample.HelloWorld` (`HelloWorldCompleteStepHandler`, manifest `Signals`, `ModuleTestHarnessTests`).

### Surfaces on `context.Signals`

| Member | Purpose | v1 behavior |
|--------|---------|-------------|
| **`Events`** | Durable facts persisted as platform events | Fail-fast when platform events are enabled and manifest/validation fails |
| **`Logs`** | Correlated operator diagnostics (progress, counts, context) | Best-effort via structured `ILogger`; **not** notification triggers |
| **`Telemetry`** | Optional metrics | No-op in default hosts; best-effort when wired (OpenTelemetry adapter is later work) |
| **`Correlation`** | Emit context (`IntegrationId`, `RunId`, `ProcedureId`, `ScheduleId`, correlation ids) | Same object as the step context; pass to `EmitAsync` |

```csharp
// Preferred emit (declare signal in manifest first):
await context.Signals.Events.EmitAsync(
    context.Signals.Correlation,
    "demo.completed",
    new Dictionary<string, object?> { ["message"] = "Done." },
    cancellationToken: cancellationToken);

context.Signals.Logs.LogOperational(
    OperationalLogLevel.Information,
    "myModule.step.completed",
    "Step finished.",
    new Dictionary<string, object?> { ["rows"] = count });

// Optional; no-op unless host wires telemetry (Signals-07):
context.Signals.Telemetry.RecordCounter("myModule.rows", count);
```

### `context.PlatformEvents` (compatibility only)

`context.PlatformEvents` aliases **`context.Signals.Events`**. Use **`context.Signals.Events`** in new code. Compatibility remains for existing modules and tests; obsolescence/removal is tracked in the Epic 103A plan.

### Manifest: declare before emit

Declare every module signal the handler may emit on **`ModuleManifest.Signals`** (`ModuleSignalDeclaration`: short name, title, description, severity, optional `RequiredDataProperties`). Startup validation builds the module signal registry; **undeclared emits fail** at emit time.

```csharp
Signals =
[
    new ModuleSignalDeclaration(
        "demo.completed",
        "Demo completed",
        "Emitted when the Complete step finishes.",
        "info",
        RequiredDataProperties: ["message"])
],
```

Emit using the **short name** from the manifest (for example `"demo.completed"`); the platform resolves the full event type (`apperture.module.{moduleId}.{signal}`).

**Platform-owned lifecycle:** Generic run completed/failed (and related execution lifecycle) events are emitted by the platform, not modules. Do not emit `apperture.execution.run.*` (or equivalent) from module handlers.

### When to use which surface

| Need | Use |
|------|-----|
| Fact operators or notifications should react to | `Signals.Events` (declared in manifest) |
| Troubleshooting detail tied to run/integration/step | `Signals.Logs` |
| Dev-time or optional metrics | `Signals.Telemetry` (optional in v1) |

Operational logs are **correlated diagnostics** (module id, run id, integration, step name in logger scope). They are not durable audit storage and do not trigger notifications in v1.

### Notifications (v1)

**Notification rules evaluate platform events only** in the current release. Emit a declared module event when alerting or workflow-adjacent reactions are required. Do not use `ILogger.LogError` or operational logs as a substitute for notification-worthy facts.

> **Future:** Calendar- or schedule-specific signal documentation and additional notification sources are out of scope for this section; see Epic 103A backlog and [epic77-notification-system-backlog.md](../03-operations/epic77-notification-system-backlog.md).

### Testing with `ModuleTestHarness`

Package: **`Apperture.ModuleSdk.Testing`**. Build a harness from your module, stub slots, then assert recorded events, logs, and telemetry.

```csharp
var harness = ModuleTestHarness.ForModule(new MyModule())
    .WithIntegrationId("int-1")
    .WithRunId("run-1")
    .WithSlotResolver(slot => /* IDbConnection, API clients, etc. */)
    .Build();

var context = harness.CreateStepContext("MyStep");
await new MyStepHandler().ExecuteAsync(context, parameters);

harness.PlatformEvents.ShouldContain("my.signal");
var logs = ModuleTestHarness.GetOperationalLogs(context);
logs.OperationalLogs.Should().ContainSingle(l => l.Code == "myModule.step.completed");
harness.Telemetry.Counters.Should().ContainSingle(c => c.Name == "myModule.rows");
```

`context.PlatformEvents` on a harness context remains valid for compatibility assertions. See `ModuleTestHarnessTests` and `HelloWorldSignalsAdoptionTests`.

### Handler registration (current)

Handlers may still be registered with **`new MyHandler()`** via `IModuleStepHandlerRegistrar`. A DI-backed handler factory is **future work**; regardless of construction, handlers must use **context-only** signals.

### Anti-patterns

- Resolving signal services from `IServiceProvider` inside a step handler
- Emitting signal names not declared in `ModuleManifest.Signals`
- Emitting platform run lifecycle event types from module code
- Using operational logs or generic `ILogger` severity as notification triggers
- Treating operational logs as durable audit or compliance records (v1 has no log store)

## Operations (Execution MVP Contract)

Modules can also expose direct executable operations by implementing `IOperationDefinitionProvider` and returning `OperationDefinition` records.

For the current MVP, operation execution reuses the existing step-handler execution pipeline with a strict one-step mapping contract:

1. `OperationDefinition.Name` is treated as the single step/handler name to execute.
2. That handler must be registered by the module via the existing step-handler registrar path.
3. Operation execution is direct and single-step; use workflows for multi-step durable orchestration.

This contract is intentionally narrow to keep operations and workflows distinct while sharing the runtime substrate.

### Operation identity and metadata conventions

For operator/API surfaces:

1. Use `OperationDefinition.Id` as the stable `OperationDefinitionId`.
2. Treat `OperationDefinition.Name` as execution mapping metadata (single-step handler name), not external identity.
3. Keep `OperationDefinition.Id` stable once published; changing display labels should not break API routes or persisted run references.

### Input metadata contract

Modules can provide a JSON `ParameterSchema` with a `fields` array to drive platform-generated start forms and validation.

Per-field keys supported by current platform rendering/validation:

- `key` (required)
- `type` (`string`, `int`, `decimal`, `bool`, `datetime`, `enum`, `json`)
- `label`
- `helpText`
- `required`
- `defaultValue`
- `order`
- `allowedValues`
- `min` / `max` / `maxLength`

If schema metadata is unsupported or malformed, the platform falls back safely and may reject invalid input at API boundary.

Current server-side enforcement for declared metadata includes:

- required fields
- type compatibility (`string`, `int`, `decimal`, `bool`, `datetime`, `enum`, `json`)
- `allowedValues` (for enum-like constraints)
- `maxLength` for string inputs
- numeric `min` / `max` bounds

## Governance Contributions

Modules can contribute governance metadata in `ModuleManifest`:

- `Permissions` for direct permission declarations (existing behavior).
- `GovernedResources` for module-defined resource types and default scope keys.
- `GovernedActions` for module-defined action keys mapped to resource types.

Validation rules enforced at startup:

- action keys must be lowercase and dot-separated.
- resource type must be non-empty.
- invalid declarations fail startup registration with explicit diagnostics.
