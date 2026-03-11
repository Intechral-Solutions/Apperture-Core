using Apperture.Core.Application.Execution;

namespace Apperture.RuntimePlane.Engine;

/// <summary>
/// Extension methods for step execution context, including "resolve, use, dispose" for IDisposable (ExecuteWithSlotAsync) and IAsyncDisposable (ExecuteWithSlotAndDisposeAsync) clients.
/// </summary>
public static class StepExecutionContextExtensions
{
    /// <summary>
    /// Resolves the slot to T, invokes the callback with the client, then disposes the client.
    /// Returns the callback result. If resolution returns null, throws SlotNotBoundException.
    /// Use when the step requires the dependency and T is IDisposable.
    /// </summary>
    public static async ValueTask<object?> ExecuteWithSlotAsync<T>(
        this IStepExecutionContext context,
        string slotName,
        Func<T, CancellationToken, ValueTask<object?>> useAsync,
        CancellationToken cancellationToken = default)
        where T : class, IDisposable
    {
        var client = await context.ResolveSlotAsync<T>(slotName, cancellationToken);
        if (client is null)
            throw new SlotNotBoundException(context.ModuleId, slotName);
        try
        {
            return await useAsync(client, cancellationToken);
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// Resolves the slot to T, invokes the callback with the client, then asynchronously disposes the client.
    /// Returns the callback result. If resolution returns null, throws SlotNotBoundException.
    /// Use when the step requires the dependency and T is IAsyncDisposable.
    /// </summary>
    public static async ValueTask<object?> ExecuteWithSlotAndDisposeAsync<T>(
        this IStepExecutionContext context,
        string slotName,
        Func<T, CancellationToken, ValueTask<object?>> useAsync,
        CancellationToken cancellationToken = default)
        where T : class, IAsyncDisposable
    {
        var client = await context.ResolveSlotAsync<T>(slotName, cancellationToken);
        if (client is null)
            throw new SlotNotBoundException(context.ModuleId, slotName);
        try
        {
            return await useAsync(client, cancellationToken);
        }
        finally
        {
            await client.DisposeAsync();
        }
    }
}
