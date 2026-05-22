# Execution Contract Handler Patterns

These starter snippets are lightweight copyable patterns for new module handlers.
They reinforce the preferred declared execution-contract model from ADR-0022/ADR-0023 and the platform signals SDK from [ADR-0038](../02-decisions/ADR-0038-platform-signals-sdk-conventions.md).

## Step context and signals (Epic 103)

Step handlers receive **`IStepExecutionContext`**. Use **`context.Signals`** for module-authored signals — do not resolve `IModulePlatformEventEmitter`, `IModuleOperationalLogger`, or `IModuleTelemetry` from DI inside handlers.

| Surface | Use for | v1 behavior |
|---------|---------|-------------|
| `context.Signals.Events` | Durable facts that may trigger notifications | Fail-fast when platform events are enabled and manifest/validation fails |
| `context.Signals.Logs` | Operator diagnostics (sync progress, counts, non-alert detail) | Best-effort via `ILogger`; not notifications |
| `context.Signals.Telemetry` | Optional metrics | No-op unless host wires OTel (later epic) |
| `context.Signals.Correlation` | Emit context (`IntegrationId`, `RunId`, correlation ids) | Same instance as context; pass to `EmitAsync` |

**Compatibility:** `context.PlatformEvents` aliases `context.Signals.Events`. Prefer `Signals.Events` in new code.

**Platform lifecycle:** Generic run completed/failed events are platform-owned (`apperture.execution.run.*`). Modules must not emit those types.

**Manifest:** Declare module signals under `ModuleManifest.Signals` before emitting. Undeclared emits fail validation.

**Handler registration:** `module.RegisterStepHandlers(registry => registry.Register(..., new MyHandler()))` remains acceptable. A DI-backed handler factory is future work — handlers should still use context-only signals.

### Emit a declared platform event

```csharp
await context.Signals.Events.EmitAsync(
    context.Signals.Correlation,
    "demo.completed",
    new Dictionary<string, object?> { ["message"] = "Step finished." },
    cancellationToken: cancellationToken);
```

### Write an operational log

```csharp
context.Signals.Logs.LogOperational(
    OperationalLogLevel.Information,
    "myModule.sync.completed",
    "Sync finished.",
    new Dictionary<string, object?> { ["rows"] = rowCount });
```

### Optional telemetry (no-op in default hosts)

```csharp
context.Signals.Telemetry.RecordCounter("myModule.rows.processed", rowCount);
```

### Assert events and logs in tests (`ModuleTestHarness`)

```csharp
var harness = ModuleTestHarness.ForModule(new MyModule())
    .WithIntegrationId("int-1")
    .WithRunId("run-1")
    .WithSlotResolver(slot => /* stubs */)
    .Build();

var context = harness.CreateStepContext("MyStep");
await handler.ExecuteAsync(context, parameters);

harness.PlatformEvents.ShouldContain("my.signal");
var logs = ModuleTestHarness.GetOperationalLogs(context);
logs.OperationalLogs.Should().ContainSingle(l => l.Code == "myModule.sync.completed");
```

See `Apperture.ModuleSdk.Testing` and `ModuleTestHarnessTests` (HelloWorld `demo.completed` sample).

### Anti-patterns

- Resolving signal services from `IServiceProvider` inside a step handler
- Using `ILogger.LogError` as a substitute for notification-worthy platform events
- Emitting undeclared signal names or platform run lifecycle event types from module code
- Treating operational logs as durable audit or notification triggers (v1)

## Preferred success skeleton

```csharp
public sealed class ExampleStepHandler : IStepHandler
{
    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        // ... domain work ...
        return OperatorExecutionResult.SuccessSummaryOnly(new
        {
            message = "Example step completed.",
            step = "Example"
        });
    }
}
```

## Preferred failure skeleton

```csharp
public sealed class ExampleStepHandler : IStepHandler
{
    public ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var hasPermission = false;
        if (!hasPermission)
            throw OperatorExecutionResult.FailWithheld("Example step failed policy checks.");

        return ValueTask.FromResult<object?>(OperatorExecutionResult.SuccessWithheld());
    }
}
```

## Compatibility note

- Preferred for new code: `OperatorExecutionResult.Success*` + `OperatorExecutionResult.Fail*`.
- Supported fallback: `OperatorRunOutput`, plain success returns, and ordinary exceptions.
