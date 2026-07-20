using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling.Persistence;

public static class SampleApiPollingPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers module-owned <see cref="SampleApiPollingDbContext"/> factory and
    /// <see cref="IApiPollingDurableLastSeenStore"/> against the shared SQL database.
    /// </summary>
    public static IServiceCollection AddSampleApiPollingPersistence(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory,
        bool applyMigrationsOnStartup = false)
    {
        services.AddDbContextFactory<SampleApiPollingDbContext>((sp, builder) =>
        {
            builder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            var connectionString = connectionStringFactory(sp);
            builder.UseSqlServer(
                connectionString,
                sql =>
                {
                    sql.MigrationsHistoryTable("__EFMigrationsHistory_SampleApiPolling");
                });
        });

        services.AddSingleton<IApiPollingDurableLastSeenStore, SqlApiPollingDurableLastSeenStore>();

        if (applyMigrationsOnStartup)
            services.AddHostedService<SampleApiPollingMigrationHostedService>();

        return services;
    }
}

internal sealed class SampleApiPollingMigrationHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SampleApiPollingMigrationHostedService> _logger;

    public SampleApiPollingMigrationHostedService(
        IServiceProvider services,
        ILogger<SampleApiPollingMigrationHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = _services.GetRequiredService<IDbContextFactory<SampleApiPollingDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Sample.ApiPolling module migrations applied successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Sample.ApiPolling module database migration failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
