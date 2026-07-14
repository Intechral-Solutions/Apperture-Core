using System.Data;
using System.Text.Json;
using Apperture.Core.Application.Execution;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Sample.HelloWorld;

/// <summary>
/// Minimal worker-side Complete step: emits <c>demo.completed</c> then proves Db slot resolution.
/// Emit is independent of the Db slot so a missing binding cannot silently skip the platform event.
/// </summary>
public sealed class HelloWorldCompleteStepHandler : IStepHandler
{
    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
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

            var emitResult = await context.Signals.Events.EmitAsync(
                context.Signals.Correlation,
                "demo.completed",
                new Dictionary<string, object?> { ["message"] = message },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!emitResult.Emitted)
            {
                var reason = !string.IsNullOrWhiteSpace(emitResult.SkipReason)
                    ? emitResult.SkipReason
                    : emitResult.SkippedBecauseDisabled
                        ? "PlatformEvents:Enabled=false"
                        : "emit returned Emitted=false";
                throw OperatorExecutionResult.FailSummaryOnly(
                    "HelloWorld demo.completed platform event was not written. "
                    + reason
                    + " Enable PlatformEvents on Worker Host, confirm IModulePlatformEventEmitter is registered (not Null), and re-run.");
            }

            context.Signals.Logs.LogOperational(
                OperationalLogLevel.Information,
                "helloworld.demo.completed",
                "Emitted demo.completed module signal.",
                new Dictionary<string, object?>
                {
                    ["signal"] = "demo.completed",
                    ["eventId"] = emitResult.EventId,
                });
        }

        var conn = await context.ResolveSlotAsync<IDbConnection>("Db", cancellationToken).ConfigureAwait(false);
        if (conn is null)
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                "HelloWorld Complete could not resolve the Db slot. Bind an Active connection to Sample.HelloWorld / Db on this integration.");
        }

        using (conn)
        {
            conn.Open();
            try
            {
                context.Signals.Telemetry.RecordCounter("helloworld.complete", 1);
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
