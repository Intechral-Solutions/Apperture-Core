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
        var baseConnectionString = SqlServerTestConfiguration.RequireConnectionString();

        var catalog = "Apperture_AuditCorrelation_" + Guid.NewGuid().ToString("N")[..12];
        var connectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        try
        {
            await using var db = NewDb(connectionString);
            if (!await db.Database.CanConnectAsync())
                throw SqlTestExecution.Unavailable("the test catalog was created but cannot be connected to");
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

    /// <summary>
    /// Whole-branch review Finding 1 (CRITICAL) regression guard: <c>AuditRecord.CorrelationId</c> is
    /// typed as the non-nullable reference <c>CorrelationId CorrelationId</c>, but callers can still pass
    /// a <c>CorrelationId</c> instance wrapping a null <c>Value</c> (the documented convention -- see
    /// <c>RecoveryController.cs</c>'s own <c>new CorrelationId(null)</c> -- for a caller with no real
    /// correlation to report). Before this fix, <c>SqlAuditService.AppendAsync</c> dereferenced
    /// <c>record.CorrelationId.Value</c> unconditionally, so any such append against a REAL
    /// <see cref="AppertureDbContext"/> threw a <see cref="NullReferenceException"/> -- silently, in
    /// production, wherever the append sat behind a best-effort <c>catch {}</c> (exactly
    /// <c>SchedulerOperationsController.TryAuditAsync</c>'s shape, which is what this bug was actually
    /// found in). This proves the fixed <c>record.CorrelationId?.Value</c> does not throw and reads back
    /// with a null <c>CorrelationId.Value</c>.
    /// </summary>
    [Fact]
    public async Task Null_correlation_id_does_not_throw_and_reads_back_null()
    {
        var baseConnectionString = SqlServerTestConfiguration.RequireConnectionString();

        var catalog = "Apperture_AuditCorrelationNull_" + Guid.NewGuid().ToString("N")[..12];
        var connectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        try
        {
            await using var db = NewDb(connectionString);
            if (!await db.Database.CanConnectAsync())
                throw SqlTestExecution.Unavailable("the test catalog was created but cannot be connected to");
            await db.Database.MigrateAsync();

            var service = new SqlAuditService(db);
            var recordId = Guid.NewGuid().ToString("N");
            var act = async () => await service.AppendAsync(new AuditRecord(
                new AuditRecordId(recordId),
                new ActorId("test-actor"),
                "scheduler.run_cycle.requested",
                Resource: null,
                Outcome: "requested",
                DateTimeOffset.UtcNow,
                new CorrelationId(null),
                Details: null,
                ResourceContextId: "app-null-correlation"));

            await act.Should().NotThrowAsync(
                "a null-Value CorrelationId is a documented, legitimate append shape (see RecoveryController/SchedulerOperationsController), not an error");

            var page = await service.QueryAsync(new AuditQuery(FilterByResourceContextId: "app-null-correlation", PageSize: 10));
            page.Items.Should().ContainSingle().Which.CorrelationId.Value.Should().BeNull();
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
