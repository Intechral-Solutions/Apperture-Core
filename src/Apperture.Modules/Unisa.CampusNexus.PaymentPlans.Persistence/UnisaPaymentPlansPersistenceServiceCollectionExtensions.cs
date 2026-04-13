using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Unisa.CampusNexus.PaymentPlans.Persistence;

public static class UnisaPaymentPlansPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="UnisaPaymentPlansDbContext"/> against the shared SQL Server database with an isolated migrations history table.
    /// </summary>
    public static IServiceCollection AddUnisaPaymentPlansPersistence(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory,
        bool applyMigrationsOnStartup = false)
    {
        services.AddDbContext<UnisaPaymentPlansDbContext>((sp, builder) =>
        {
            builder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            var connectionString = connectionStringFactory(sp);
            builder.UseSqlServer(
                connectionString,
                sql =>
                {
                    sql.MigrationsHistoryTable("__EFMigrationsHistory_UnisaPaymentPlans");
                });
        });

        if (applyMigrationsOnStartup)
            services.AddHostedService<UnisaPaymentPlansMigrationHostedService>();

        return services;
    }
}
