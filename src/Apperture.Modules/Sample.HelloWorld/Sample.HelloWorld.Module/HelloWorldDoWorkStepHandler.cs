using System.Data;
using Apperture.Core.Application.Execution;

namespace Sample.HelloWorld;

/// <summary>
/// Minimal module-owned DoWork handler: resolves the "Db" slot and opens/closes the connection.
/// Proves slot-bound resolution without relying on WorkerHost demo handlers.
/// </summary>
public sealed class HelloWorldDoWorkStepHandler : IStepHandler
{
    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var conn = await context.ResolveSlotAsync<IDbConnection>("Db", cancellationToken);
        if (conn is null)
            return null;

        using (conn)
        {
            conn.Open();
            try
            {
                if (parameters.TryGetValue("successMode", out var successModeObj) &&
                    successModeObj is string successMode &&
                    string.Equals(successMode, "withheld", StringComparison.OrdinalIgnoreCase))
                {
                    return OperatorExecutionResult.SuccessWithheld();
                }

                parameters.TryGetValue("name", out var nameObj);
                var name = nameObj as string;
                if (string.IsNullOrWhiteSpace(name))
                    name = null;

                return OperatorExecutionResult.SuccessInline(new
                {
                    message = string.IsNullOrWhiteSpace(name)
                        ? "HelloWorld operation completed."
                        : $"Hello, {name}!",
                    step = "DoWork"
                });
            }
            finally
            {
                conn.Close();
            }
        }
    }
}

