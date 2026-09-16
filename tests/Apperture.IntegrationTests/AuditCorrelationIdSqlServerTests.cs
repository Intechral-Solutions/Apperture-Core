using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>Design gate §8.8: CorrelationId must round-trip through real SQL Server, not just the domain model.</summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AuditCorrelationIdSqlServerTests
{
    [Fact]
    public async Task CorrelationId_round_trips_through_append_and_query()
    {
        var baseConnectionString = SqlServerTestConfiguration.GetConnectionString();
        if (baseConnectionString is null)
            return; // soft-skip

        var catalog = "Apperture_AuditCorrelation_" + Guid.NewGuid().ToString("N")[..12];
        var connectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;

        try
        {
            SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);
        }
        catch (InvalidOperationException)
        {
            return; // soft-skip
        }

        try
        {
            await using var db = NewDb(connectionString);
            if (!await db.Database.CanConnectAsync())
                return; // soft-skip
            await db.Database.MigrateAsync();

            var service = new SqlAuditService(db);
            var correlationId = Guid.NewGuid().ToString("N");
            await service.AppendAsync(new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                new ActorId("test-actor"),
                "integration.ownership.moved_out",
                new ResourceDescriptor(ResourceTypeConstants.Integration, "int-1"),
                Outcome: "",
                DateTimeOffset.UtcNow,
                new CorrelationId(correlationId),
                Details: null,
                ResourceContextId: "app-a"));

            var page = await service.QueryAsync(new AuditQuery(FilterByResourceContextId: "app-a", PageSize: 10));

            page.Items.Should().ContainSingle().Which.CorrelationId.Value.Should().Be(correlationId);
        }
        finally
        {
            await TryDropDatabaseAsync(baseConnectionString, catalog);
        }
    }

    private static AppertureDbContext NewDb(string connectionString) =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(connectionString)
            .Options);

    private static async Task TryDropDatabaseAsync(string baseConnectionString, string catalog)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" };
            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalog}];";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }
}
