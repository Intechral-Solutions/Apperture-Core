using Apperture.Core.Application.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Apperture.Persistence.SqlServer.Audit;

/// <summary>
/// Rejects any save of a recorder row when no transaction is current on the context (AUD-1 design gate §13.4). A
/// recorder row only ever commits with the change it describes, or in its writer's own short transaction.
/// </summary>
public sealed class AuditRecorderRowGuardInterceptor : SaveChangesInterceptor
{
    public static AuditRecorderRowGuardInterceptor Instance { get; } = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Check(DbContext? context)
    {
        if (context is null || context.Database.CurrentTransaction is not null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<AuditLogEntity>())
        {
            if (entry.State == EntityState.Added && entry.Entity.Producer == AuditProducers.Recorder)
                throw new AuditInvariantException("A recorder audit row can only be saved inside its unit of work's transaction.");
        }
    }
}
