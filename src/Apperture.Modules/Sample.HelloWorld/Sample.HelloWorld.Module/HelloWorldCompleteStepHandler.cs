using System.Data;
using System.Text.Json;
using Apperture.Core.Application.Execution;

namespace Sample.HelloWorld;

/// <summary>
/// Minimal worker-side Complete step: proves the step pipeline is executed for the final step.
/// </summary>
public sealed class HelloWorldCompleteStepHandler : IStepHandler
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
                if (parameters.TryGetValue("emitDemoPlatformEvent", out var flag) && IsTruthy(flag))
                {
                    _ = await context.PlatformEvents.EmitAsync(
                        context,
                        "demo.completed",
                        new Dictionary<string, object?> { ["message"] = "HelloWorld demo platform event completed." },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                // Epic 23 declared execution contract: summary-only success output.
                return OperatorExecutionResult.SuccessSummaryOnly(new { message = "HelloWorld workflow completed.", step = "Complete" });
            }
            finally
            {
                conn.Close();
            }
        }
    }

    private static bool IsTruthy(object? value) =>
        value switch
        {
            true => true,
            JsonElement je when je.ValueKind == JsonValueKind.True => true,
            _ => false,
        };
}
