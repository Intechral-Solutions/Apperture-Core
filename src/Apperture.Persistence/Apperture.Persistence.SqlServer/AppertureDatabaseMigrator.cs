using Apperture.Persistence.SqlServer.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// The supported way to bring an Apperture database to the current schema. Plain migrations, except
/// that the username backfill data step runs between <c>Iam9_UsernameLedger</c> and
/// <c>Iam9_UsernameRequired</c>.
/// </summary>
/// <remarks>
/// Entry points that call <c>Database.MigrateAsync</c> or <c>dotnet ef database update</c> directly skip
/// the data step. On a database that already has users, <c>Iam9_UsernameRequired</c> then fails closed
/// before changing anything, and the next Management Host start completes the sequence. Restart-safe:
/// the data step is idempotent and is only run while <c>Iam9_UsernameRequired</c> is not yet applied.
/// </remarks>
public static class AppertureDatabaseMigrator
{
    public static async Task MigrateAsync(
        AppertureDbContext db,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        logger ??= NullLogger.Instance;
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);

        if (!applied.Contains(Iam9_UsernameRequired.UsernameRequiredMigrationId))
        {
            if (!applied.Contains(Iam9_UsernameLedger.UsernameLedgerMigrationId))
            {
                // Forward only: the target is never behind the database here, because Iam9_UsernameRequired
                // (the next migration) is not applied.
                await db.GetInfrastructure().GetRequiredService<IMigrator>()
                    .MigrateAsync(Iam9_UsernameLedger.UsernameLedgerMigrationId, cancellationToken)
                    .ConfigureAwait(false);
            }

            await UsernameBackfill.RunAsync(db, logger, cancellationToken).ConfigureAwait(false);
        }

        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
