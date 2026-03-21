# Execution Contract Handler Patterns

These starter snippets are lightweight copyable patterns for new module handlers.
They reinforce the preferred declared execution-contract model from ADR-0022/ADR-0023.

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
