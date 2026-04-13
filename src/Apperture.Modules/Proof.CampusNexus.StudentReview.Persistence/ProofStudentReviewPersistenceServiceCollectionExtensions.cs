using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Proof.CampusNexus.StudentReview.Persistence;

public static class ProofStudentReviewPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ProofStudentReviewDbContext"/> with isolated migration history on the shared SQL database.
    /// </summary>
    public static IServiceCollection AddProofStudentReviewPersistence(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory,
        bool applyMigrationsOnStartup = false)
    {
        services.AddDbContext<ProofStudentReviewDbContext>((sp, builder) =>
        {
            builder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            var connectionString = connectionStringFactory(sp);
            builder.UseSqlServer(
                connectionString,
                sql =>
                {
                    sql.MigrationsHistoryTable("__EFMigrationsHistory_ProofStudentReview");
                });
        });

        if (applyMigrationsOnStartup)
            services.AddHostedService<ProofStudentReviewMigrationHostedService>();

        return services;
    }
}
