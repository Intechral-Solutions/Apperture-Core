# Module SDK

**Full guide:** [module-authoring-guide.md](../../docs/05-modules/module-authoring-guide.md)  
**Manifest reference:** [module-manifest-spec.md](../../docs/05-modules/module-manifest-spec.md)  
**Integration SDK (SDK-6A):** [SDK-6A spec](../../docs/superpowers/specs/SDK-6A-Integration-Sdk-Contract-Consolidation.md) · [contract inventory](../../docs/audits/sdk6a-integration-sdk-contract-inventory.md)  
**Signals conventions:** [ADR-0038](../../docs/02-decisions/ADR-0038-platform-signals-sdk-conventions.md)  
**Handler snippets:** [execution-contract-handler-patterns.md](../../docs/05-modules/execution-contract-handler-patterns.md)

## Module structure

A module provides:

- Workflow definitions and step handlers (`IModule`, `IModuleStepHandlerRegistrar`) from **`Apperture.ModuleSdk.Integration`** (not Core.Application)
- Optional operations (`IOperationDefinitionProvider`) and manifest **procedures**
- Manifest: permissions, **signals**, governance, connector documentation
- Optional menu links (`IModuleNavigationProvider`)
- Slot-bound connections (resolve via `IStepExecutionContext.ResolveSlotAsync` — no connection ids in handlers)

Optional `.Ui` projects exist but are **not** loaded by the Management shell today.

## Platform signals (Epic 103 — shipped)

Step handlers use **`context.Signals` only** — do not resolve signal services from DI.

| API | Use |
|-----|-----|
| `context.Signals.Events` | Declared platform events (`ModuleManifest.Signals`); may trigger notifications |
| `context.Signals.Logs` | Correlated operational diagnostics; best-effort; not notifications |
| `context.Signals.Telemetry` | Optional metrics; no-op unless host wires OTel (later) |
| `context.Signals.Correlation` | Pass to `EmitAsync` (same fields as execution context) |

**Compatibility:** `context.PlatformEvents` → `Signals.Events`. Prefer `Signals` in new code.

```csharp
await context.Signals.Events.EmitAsync(context.Signals.Correlation, "my.event", payload, ct);
context.Signals.Logs.LogOperational(OperationalLogLevel.Information, "myModule.step.done", "Done.");
context.Signals.Telemetry.RecordCounter("myModule.rows", count);
```

**Rules:** Declare signals in manifest before emit. Do not emit `apperture.execution.run.*` from modules (platform-owned). See [ADR-0038](../../docs/02-decisions/ADR-0038-platform-signals-sdk-conventions.md) and [platform-signal-catalog.md](../../docs/01-architecture/platform-signal-catalog.md).

## Testing

**`Apperture.ModuleSdk.Testing`** — `ModuleTestHarness.ForModule(module).WithRunId(...).WithSlotResolver(...).Build()` records events, logs, and telemetry without SQL.

## Resource and permission registration (implemented)

Modules declare **Permissions** (and optional **GovernedResources** / **GovernedActions**) in the manifest. The host registers them at startup; modules do not call `RegisterPermission` themselves.

**Constraints:**

- Do not browse arbitrary global resources at runtime.
- Resolve only declared slots via bindings.
- Do not use audit or SMTP for operational outcomes.

See [module-manifest-spec.md](../../docs/05-modules/module-manifest-spec.md) and [Epic 5 Prompt 2 summary](../../docs/02-decisions/Epic5-prompt2-module-workflow-enforcement-summary.md).

## Metadata (manifest)

| Field | Status |
|-------|--------|
| Id, Name, Version, Description | Required |
| Permissions, Signals, Procedures, GovernedResources, GovernedActions | Optional, shipped |
| ConnectorDefinitions | Optional, documentation only |
| Configuration schema | Future |

## Workflow and step execution

- Workflows: linear step lists on `WorkflowDefinition`; handlers registered per `(moduleId, stepName)`.
- Operations: single step; `OperationDefinition.Name` = handler name; `ParameterSchema` on operation definition.
- Procedures: manifest entries launching workflow/operation per integration with `ParameterSchema`.
- Outcomes: prefer `OperatorExecutionResult` for operator-visible success/failure ([ADR-0022/0023](../../docs/02-decisions/)).

## Deployment

- **On-prem:** Compiled into worker host; explicit `IEnumerable<IModule>` registration. One worker process may host several modules today; one module per process is a supported future deployment option (RC1 program gate, D-23).
- **Cloud (not implemented; outside RC1 support):** One module per Function App is the intended model.

## Runtime-visible naming

Do not encode epic/track IDs in module, procedure, workflow, notification, or settings **display** strings. Prefer product-like names (`Sample File Import`, `Import completed with errors`). Planning labels stay in docs/ADRs/tests. See [anti-patterns](./anti-patterns.md) and [AGENTS.md](../../AGENTS.md).

## Signals backlog (not module SDK basics)

Tracked in [Epic103A plan](../../docs/02-decisions/Epic103A-Platform-Signals-SDK-Stabilization-Implementation-Plan.md): OpenTelemetry adapter, log elevation, handler factory/DI, built-in module notification definitions in manifest, calendar/event SDK — **not available** for authors today.
