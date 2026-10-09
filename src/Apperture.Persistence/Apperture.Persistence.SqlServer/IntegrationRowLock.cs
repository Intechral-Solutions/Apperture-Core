using Apperture.Core.Domain.Integration;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// The integration row lock of a writer that creates something which refers to an integration by id (AUD-1.4 slice 3).
/// </summary>
/// <remarks>
/// <para>Notification definitions, notification subscriptions and attention-routing overrides carry an integration id without a
/// foreign key, so nothing in the database stops one from committing after an integration delete has counted its blockers. The
/// writer takes the integration row lock (<c>UPDLOCK</c>, the same statement an ownership move and a binding create use) inside
/// the transaction that inserts its row, requires the integration to exist, and keeps the lock to the commit. A delete that holds
/// the row makes the writer wait and then refuse; a row that committed first is visible to the delete's blocker count.</para>
/// <para>It must be called inside an open transaction. It takes no other lock, so the order is whatever the caller already holds,
/// then the integration row, then the rows the caller writes.</para>
/// </remarks>
internal static class IntegrationRowLock
{
    /// <exception cref="InvalidOperationException">The integration does not exist.</exception>
    public static async ValueTask AcquireAsync(
        AppertureDbContext db,
        string integrationId,
        Func<CancellationToken, ValueTask>? reauthorize,
        CancellationToken cancellationToken)
    {
        // The same locking read the other integration writers use; untracked, so a retried attempt reads afresh.
        var integration = await new SqlIntegrationRepository(db)
            .GetByIdForUpdateAsync(new IntegrationId(integrationId), cancellationToken)
            .ConfigureAwait(false);
        if (integration is null)
            throw new InvalidOperationException($"Integration {integrationId} not found.");

        // The row is fixed until the commit, so a check re-run here sees any ownership move that committed first.
        if (reauthorize is not null)
            await reauthorize(cancellationToken).ConfigureAwait(false);
    }
}
