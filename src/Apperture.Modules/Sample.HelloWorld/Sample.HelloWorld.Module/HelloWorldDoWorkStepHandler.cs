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
                return OperatorRunOutput.Inline(new { message = "HelloWorld operation completed.", step = "DoWork" });
            }
            finally
            {
                conn.Close();
            }
        }
    }
}

