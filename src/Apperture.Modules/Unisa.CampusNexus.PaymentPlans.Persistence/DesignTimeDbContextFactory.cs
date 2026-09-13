using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace Unisa.CampusNexus.PaymentPlans.Persistence;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<UnisaPaymentPlansDbContext>
{
    public UnisaPaymentPlansDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? Environment.GetEnvironmentVariable("Apperture__ConnectionString")
            ?? "Server=(localdb)\\mssqllocaldb;Database=Apperture;Trusted_Connection=True;";

        var options = new DbContextOptionsBuilder<UnisaPaymentPlansDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory_UnisaPaymentPlans"))
            .Options;

        return new UnisaPaymentPlansDbContext(options);
    }
}
