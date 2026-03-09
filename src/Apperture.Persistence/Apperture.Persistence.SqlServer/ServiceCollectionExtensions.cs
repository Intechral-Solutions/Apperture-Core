using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Extension methods for configuring Apperture persistence with SQL Server.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds AppertureDbContext with SQL Server using connection string from config, including retry/resilience for transient failures.
    /// Uses same config structure as bootstrap (SqlServer section or DefaultConnection).
    /// </summary>
    public static IServiceCollection AddApperturePersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        int maxRetryCount = 3)
    {
        var connectionString = GetConnectionString(configuration);
        services.AddDbContext<AppertureDbContext>(options =>
        {
            options.UseSqlServer(
                connectionString,
                sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(maxRetryCount);
                });
        });
        return services;
    }

    /// <summary>
    /// Adds AppertureDbContext with SQL Server using explicit connection string.
    /// </summary>
    public static IServiceCollection AddApperturePersistence(
        this IServiceCollection services,
        string connectionString,
        int maxRetryCount = 3)
    {
        services.AddDbContext<AppertureDbContext>(options =>
        {
            options.UseSqlServer(
                connectionString,
                sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(maxRetryCount);
                });
        });
        return services;
    }

    private static string GetConnectionString(IConfiguration configuration)
    {
        var sql = configuration.GetSection("SqlServer");
        var server = sql["Server"];
        var user = sql["User"];
        var password = sql["Password"];
        if (!string.IsNullOrEmpty(server) && !string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(password))
        {
            var database = sql["Database"] ?? "Apperture";
            return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                UserID = user,
                Password = password,
                TrustServerCertificate = true
            }.ConnectionString;
        }
        return configuration.GetConnectionString("DefaultConnection")
            ?? "Server=(localdb)\\mssqllocaldb;Database=Apperture;Trusted_Connection=True;";
    }
}
