# Runtime Step Execution

This document describes how workflow steps obtain slot-bound dependencies at execution time via the step execution context. It links the runtime resolution boundary to the workflow/step execution model.

## Overview

Step code does not receive connection ids or the slot resolver directly. It receives an **IStepExecutionContext** from the executor, with a fixed **ModuleId** (from the workflow definition) and **ResolveSlotAsync&lt;T&gt;(slotName)**. Step handlers call `context.ResolveSlotAsync<T>("SlotName")` to obtain a live client; the executor supplies the module id from the workflow. Resolution remains slot-only; binding is the sole gate. No authorization is evaluated at resolution in the current phase.

## Execution Context

| Contract | Layer | Purpose |
|----------|--------|---------|
| **IStepExecutionContext** | Core.Application | ModuleId (read-only); ResolveSlotAsync&lt;T&gt;(slotName) for slot-bound dependency resolution. Step code receives this from the executor. |
| **StepExecutionContext** | RuntimePlane.Engine | Implementation that holds IRuntimeSlotResolver and module id; delegates ResolveSlotAsync to the resolver with (ModuleId, slotName). |

**Where module id and slot name come from**

- **ModuleId**: Supplied by the executor from the workflow under execution (e.g. `WorkflowDefinition.ModuleId`). One context per step run.
- **Slot name**: Supplied by the step (e.g. constant or parameter). The step calls `context.ResolveSlotAsync&lt;T&gt;("SlotName")`.

## Step Handler Contract

| Contract | Layer | Purpose |
|----------|--------|---------|
| **IStepHandler** | Core.Application | ExecuteAsync(context, parameters, ct) → ValueTask&lt;object?&gt;. The handler may call context.ResolveSlotAsync&lt;T&gt;(slotName). |
| **IStepHandlerRegistry** | Core.Application | Register(moduleId, stepName, handler); Get(moduleId, stepName) → handler or null. |
| **IWorkflowExecutor** | RuntimePlane.Abstractions | ExecuteStepAsync(definition, stepName, instance, parameters, ct). Creates context from definition.ModuleId, looks up handler, invokes handler with context. |

The executor creates **StepExecutionContext(IRuntimeSlotResolver, definition.ModuleId)**, looks up the handler for (moduleId, stepName), and invokes **handler.ExecuteAsync(context, parameters, ct)**. Step code uses only the context to resolve dependencies.

## Step Contract and Secrets

Steps do **not** receive **ISecretResolver**. Connection secrets are resolved inside the resolution pipeline (before the step runs); step code sees only the resolved client or dependency object. Step results must not return ResolvedSecret or secret material.

## Disposal Expectations

When the resolved client type **T** is **IDisposable** or **IAsyncDisposable**, the **caller** (step/handler) owns the instance and must dispose it (e.g. `using` or `await using`). The runtime does not take ownership. The **ExecuteWithSlotAsync&lt;T&gt;** extension (for **IDisposable**) and **ExecuteWithSlotAndDisposeAsync&lt;T&gt;** (for **IAsyncDisposable**) on IStepExecutionContext (RuntimePlane.Engine) implement “resolve, use callback, dispose” and throw SlotNotBoundException if the slot is not bound. When using **ResolveSlotAsync** directly, the step owns disposal.

## Resolution Failure Behavior

- **ResolveSlotAsync&lt;T&gt;** returns **null** when the slot has no active connection binding, binding is inactive, connection is missing or inactive, or (in current implementation) resolution fails. It does not throw for “not bound.”
- **Step behavior**: If the step requires the dependency, it should treat null as failure: throw **SlotNotBoundException** (or InvalidOperationException) with module id and slot name. See **SlotNotBoundException** in Core.Application.Execution.
- **Other failures** (e.g. secret resolution failure inside the pipeline): Exceptions propagate from the resolution pipeline to the step; no new error taxonomy.

## No Auth at Resolution (Current Phase)

No authorization is performed at resolution in this phase. **Binding is the sole gate**. The **capability.consume** permission at resolution time is **deferred**. See [Runtime Connection Resolution](runtime-connection-resolution.md).

## Deferred

- Slot name validation against declared module slots.
- Runtime principal / execution identity in step context.

## Worker Host Wiring

To run steps that use slot-bound resolution, the worker host must register:

- **ISecretResolver** (e.g. AddAppertureSqlSecrets)
- Persistence and connector repositories (AddApperturePersistence, AddConnectorRepositories)
- Connector resolution and platform SQL as needed (AddConnectorResolution, AddPlatformSqlConnector)
- **IStepHandlerRegistry** (with step handlers registered for each (moduleId, stepName))
- **IWorkflowExecutor** (e.g. DefaultWorkflowExecutor)

See WorkerHost Program.cs and WorkerStub for an example that runs one demo step (Sample.HelloWorld / DoWork) resolving the "Db" slot.

## Related

- [Runtime Connection Resolution](runtime-connection-resolution.md) — Slot-bound resolution flow, secret boundary, worker host requirements
- [Control-Plane Connector Workflows](control-plane-connector-workflows.md) — Connection and binding management
- [Connector Resource Family](connector-resource-family.md)
