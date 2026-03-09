using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Design-time factory for EF migrations. Uses localdb by default; overridable via environment.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppertureDbContext>
{
    public AppertureDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? Environment.GetEnvironmentVariable("Apperture__ConnectionString")
            ?? "Server=(localdb)\\mssqllocaldb;Database=Apperture;Trusted_Connection=True;";

        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppertureDbContext(options);
    }
}
