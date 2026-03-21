using System.Data;
using Apperture.Core.Application.Execution;

namespace Sample.HelloWorld;

/// <summary>
/// Minimal worker-side Validate step: proves the step pipeline is executed and step instances are recorded.
/// </summary>
public sealed class HelloWorldValidateStepHandler : IStepHandler
{
    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (parameters.TryGetValue("simulateFailure", out var simulateFailure) &&
            simulateFailure is true)
        {
            throw OperatorExecutionResult.FailSummaryOnly("HelloWorld validation failed by declared module intent.");
        }

        var conn = await context.ResolveSlotAsync<IDbConnection>("Db", cancellationToken);
        if (conn is null)
            return null;

        using (conn)
        {
            conn.Open();
            try
            {
                return null;
            }
            finally
            {
                conn.Close();
            }
        }
    }
}

