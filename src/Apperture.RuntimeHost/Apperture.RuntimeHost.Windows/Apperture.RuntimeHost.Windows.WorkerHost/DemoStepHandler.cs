using System.Data;
using Apperture.Core.Application.Execution;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// Demo step handler that resolves the "Db" slot to IDbConnection and disposes it after use.
/// Proves slot-bound resolution in execution. If slot is not bound, returns null without throwing.
/// </summary>
public sealed class DemoStepHandler : IStepHandler
{
    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var conn = await context.ResolveSlotAsync<IDbConnection>("Db", cancellationToken);
        if (conn is null)
            return null; // Slot not bound; step completes without dependency
        using (conn)
        {
            conn.Open();
            try
            {
                // Minimal use: open and close to prove resolution and disposal.
                return null;
            }
            finally
            {
                conn.Close();
            }
        }
    }
}
