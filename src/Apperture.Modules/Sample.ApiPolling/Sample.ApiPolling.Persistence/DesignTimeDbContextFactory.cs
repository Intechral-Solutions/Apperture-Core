using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace Sample.ApiPolling.Persistence;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SampleApiPollingDbContext>
{
    public SampleApiPollingDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? Environment.GetEnvironmentVariable("Apperture__ConnectionString")
            ?? "Server=(localdb)\\mssqllocaldb;Database=Apperture;Trusted_Connection=True;";

        var options = new DbContextOptionsBuilder<SampleApiPollingDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory_SampleApiPolling"))
            .Options;

        return new SampleApiPollingDbContext(options);
    }
}
