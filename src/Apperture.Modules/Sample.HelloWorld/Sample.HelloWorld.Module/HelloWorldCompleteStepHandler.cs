using System.Data;
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
                return new { message = "HelloWorld workflow completed.", step = "Complete" };
            }
            finally
            {
                conn.Close();
            }
        }
    }
}

