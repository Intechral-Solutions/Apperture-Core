using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace Proof.CampusNexus.StudentReview.Persistence;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProofStudentReviewDbContext>
{
    public ProofStudentReviewDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? Environment.GetEnvironmentVariable("Apperture__ConnectionString")
            ?? "Server=(localdb)\\mssqllocaldb;Database=Apperture;Trusted_Connection=True;";

        var options = new DbContextOptionsBuilder<ProofStudentReviewDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_ProofStudentReview"))
            .Options;

        return new ProofStudentReviewDbContext(options);
    }
}
