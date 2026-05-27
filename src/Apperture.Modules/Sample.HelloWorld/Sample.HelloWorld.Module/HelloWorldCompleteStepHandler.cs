using System.Data;
using System.Text.Json;
using Apperture.Core.Application.Execution;
using Apperture.ModuleSdk.Abstractions.Signals;

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
                context.Signals.Logs.LogOperational(
                    OperationalLogLevel.Information,
                    "helloworld.complete.started",
                    "HelloWorld Complete step executing.");

                if (ShouldEmitDemoCompleted(parameters))
                {
                    parameters.TryGetValue("name", out var nameObj);
                    var name = nameObj as string;
                    var message = string.IsNullOrWhiteSpace(name)
                        ? "HelloWorld demo platform event completed."
                        : $"HelloWorld demo completed for {name}.";

                    _ = await context.Signals.Events.EmitAsync(
                        context.Signals.Correlation,
                        "demo.completed",
                        new Dictionary<string, object?> { ["message"] = message },
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    context.Signals.Logs.LogOperational(
                        OperationalLogLevel.Information,
                        "helloworld.demo.completed",
                        "Emitted demo.completed module signal.",
                        new Dictionary<string, object?> { ["signal"] = "demo.completed" });
                }

                // Optional v1 demo: telemetry is no-op unless a host wires OpenTelemetry (Epic 103A Signals-07).
                context.Signals.Telemetry.RecordCounter("helloworld.complete", 1);

                // Epic 23 declared execution contract: summary-only success output.
                return OperatorExecutionResult.SuccessSummaryOnly(new { message = "HelloWorld workflow completed.", step = "Complete" });
            }
            finally
            {
                conn.Close();
            }
        }
    }

    private static bool ShouldEmitDemoCompleted(IReadOnlyDictionary<string, object?> parameters)
    {
        if (!parameters.TryGetValue("emitDemoPlatformEvent", out var flag))
            return true;

        return !IsExplicitlyFalse(flag);
    }

    private static bool IsExplicitlyFalse(object? value) =>
        value switch
        {
            false => true,
            JsonElement je when je.ValueKind == JsonValueKind.False => true,
            string s when string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) => true,
            _ => false,
        };
}
