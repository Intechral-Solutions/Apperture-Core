using Apperture.Core.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Extension methods for configuring Apperture persistence with SQL Server.
/// Persistence consumes a resolved connection string; bootstrap (or host) provides it.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds AppertureDbContext with SQL Server using a connection-string factory.
    /// Host wires the factory to resolve from bootstrap (e.g. IBootstrapConnectionProfile.ConnectionString).
    /// Persistence never parses config or resolves credentials.
    /// </summary>
    public static IServiceCollection AddApperturePersistence(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory,
        SqlPersistenceOptions? options = null)
    {
        var opts = options ?? new SqlPersistenceOptions();
        services.AddDbContext<AppertureDbContext>((sp, builder) =>
        {
            var connectionString = connectionStringFactory(sp);
            var sqlBuilder = builder.UseSqlServer(
                connectionString,
                sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(opts.MaxRetryCount);
                    if (opts.CommandTimeout is { } timeout)
                        sqlOptions.CommandTimeout((int)timeout.TotalSeconds);
                });
        });
        return services;
    }

    /// <summary>
    /// Adds IDbContextFactory&lt;AppertureDbContext&gt; for background workers and message-driven flows.
    /// Use when scoped DbContext per request is not suitable (e.g. hosted services, queues).
    /// </summary>
    public static IServiceCollection AddAppertureDbContextFactory(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory,
        SqlPersistenceOptions? options = null)
    {
        var opts = options ?? new SqlPersistenceOptions();
        services.AddDbContextFactory<AppertureDbContext>((sp, builder) =>
        {
            var connectionString = connectionStringFactory(sp);
            builder.UseSqlServer(
                connectionString,
                sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(opts.MaxRetryCount);
                    if (opts.CommandTimeout is { } timeout)
                        sqlOptions.CommandTimeout((int)timeout.TotalSeconds);
                });
        });
        return services;
    }

    /// <summary>
    /// Registers an IHostedService that runs MigrateAsync on startup. Opt-in; use when automatic migration is desired.
    /// Requires AddApperturePersistence (scoped DbContext) to be registered first.
    /// </summary>
    public static IServiceCollection AddApperturePersistenceMigrationsOnStartup(this IServiceCollection services)
    {
        services.AddHostedService<PersistenceMigrationHostedService>();
        return services;
    }

    /// <summary>
    /// Adds AppertureDbContext with SQL Server using an explicit connection string.
    /// Use for tests or hosts that resolve the string outside persistence.
    /// </summary>
    public static IServiceCollection AddApperturePersistence(
        this IServiceCollection services,
        string connectionString,
        SqlPersistenceOptions? options = null)
    {
        return services.AddApperturePersistence(_ => connectionString, options);
    }

    /// <summary>
    /// Registers connector repositories only (IConnectionRepository, IBindingRepository).
    /// For runtime hosts that need slot-bound resolution without the full control-plane connector workflows.
    /// Requires AddApperturePersistence (DbContext) to be registered first.
    /// </summary>
    public static IServiceCollection AddConnectorRepositories(this IServiceCollection services)
    {
        services.AddScoped<IConnectionRepository, SqlConnectionRepository>();
        services.AddScoped<IBindingRepository, SqlBindingRepository>();
        return services;
    }
}
