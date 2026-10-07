using Apperture.ControlPlane.Security.Audit;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Integration tests for audit timeline discoverability. Epic 32, Epic 37.
/// Requires SQL Server (e.g. Docker on port 1433).
/// Uses a collection to run sequentially and avoid shared-database interference.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AuditTimelineSqlServerTests
{
    [Fact]
    public async Task Query_ReturnsOrderedEntries_NewestFirst()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
        {
            throw new InvalidOperationException(
                SqlServerTestConfiguration.DescribeActiveConfiguration());
        }

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddScoped<IAuditService, SqlAuditService>();
        services.AddScoped<IAuditTimelineQueryService, AuditTimelineQueryService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException(SqlServerTestConfiguration.DescribeConnectionFailure());
            await AppertureDatabaseMigrator.MigrateAsync(db);
        }

        var prefix = $"ep37.ordered.{Guid.NewGuid():N}.";
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.AppendAsync(CreateRecord(baseTime.AddMinutes(1), prefix + "dispatched", "system:scheduler", "success; schedule:s1;RunId=r1"));
            await audit.AppendAsync(CreateRecord(baseTime.AddMinutes(2), prefix + "retry_requested", "user:alice", "requested; runId=r2;definitionId=wf1"));
            await audit.AppendAsync(CreateRecord(baseTime.AddMinutes(3), prefix + "blocked", "system", "Blocked; runId=r3;definitionId=wf2;reason=module_disabled"));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAuditTimelineQueryService>();
            var page = await svc.QueryAsync(new AuditFilterCriteria(
                FromUtc: baseTime,
                ToUtc: DateTimeOffset.UtcNow.AddMinutes(1),
                ActionPrefix: prefix,
                PageSize: 10));

            page.Items.Should().HaveCount(3);
            page.Items[0].Action.Should().Be(prefix + "blocked");
            page.Items[1].Action.Should().Be(prefix + "retry_requested");
            page.Items[2].Action.Should().Be(prefix + "dispatched");
        }
    }

    [Fact]
    public async Task Query_WithActionPrefix_ReturnsFiltered()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddScoped<IAuditService, SqlAuditService>();
        services.AddScoped<IAuditTimelineQueryService, AuditTimelineQueryService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException("SQL Server not reachable.");
            await AppertureDatabaseMigrator.MigrateAsync(db);
        }

        var prefix = $"ep37.actionprefix.{Guid.NewGuid():N}.";
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.AppendAsync(CreateRecord(baseTime, prefix + "dispatched", "system:scheduler", "success"));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(1), "recovery.blocked_work.retry_requested", "user:alice", "requested"));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(2), prefix + "blocked", "system:scheduler", "blocked"));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAuditTimelineQueryService>();
            var page = await svc.QueryAsync(new AuditFilterCriteria(
                FromUtc: baseTime,
                ActionPrefix: prefix,
                PageSize: 10));

            page.Items.Should().HaveCount(2);
            page.Items.Should().OnlyContain(e => e.Action.StartsWith(prefix));
        }
    }

    [Fact]
    public async Task Query_WithRunIdFilter_ReturnsMatching()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddScoped<IAuditService, SqlAuditService>();
        services.AddScoped<IAuditTimelineQueryService, AuditTimelineQueryService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException("SQL Server not reachable.");
            await AppertureDatabaseMigrator.MigrateAsync(db);
        }

        var runId = "ep37-run-" + Guid.NewGuid().ToString("N")[..8];
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-2);
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.AppendAsync(CreateRecord(baseTime, RecoveryAuditActionNames.BlockedWorkRetryRequested, "user:alice", $"requested; runId={runId};definitionId=wf1", outcome: "requested", runId: runId));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(1), RecoveryAuditActionNames.BlockedWorkRetryRequested, "user:bob", "requested; runId=other-run;definitionId=wf2", outcome: "requested", runId: "other-run"));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAuditTimelineQueryService>();
            var page = await svc.QueryAsync(new AuditFilterCriteria(
                FromUtc: baseTime,
                RunId: runId,
                PageSize: 10));

            page.Items.Should().HaveCount(1);
            page.Items[0].RelatedReferences.Should().Contain(r => r.Kind == "run" && r.Id == runId);
        }
    }

    [Fact]
    public async Task GetById_ReturnsDetailWithRefs()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddScoped<IAuditService, SqlAuditService>();
        services.AddScoped<IAuditTimelineQueryService, AuditTimelineQueryService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException("SQL Server not reachable.");
            await AppertureDatabaseMigrator.MigrateAsync(db);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.AppendAsync(CreateRecord(DateTimeOffset.UtcNow, "scheduler.schedule.dispatched", "system:scheduler", "success; schedule:s99;RunId=r99", outcome: "success", scheduleId: "s99", runId: "r99"));
        }

        string savedId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            var page = await audit.QueryAsync(new AuditQuery(PageSize: 1));
            page.Items.Should().NotBeEmpty();
            savedId = page.Items[0].Id.Value;
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAuditTimelineQueryService>();
            var entry = await svc.GetByIdAsync(savedId!);
            entry.Should().NotBeNull();
            entry!.Action.Should().Be("scheduler.schedule.dispatched");
            entry.Actor.Should().Be("system:scheduler");
            entry.Category.Should().Be("scheduler");
            entry.Details.Should().Contain("schedule:s99");
            entry.RelatedReferences.Should().Contain(r => r.Kind == "schedule" && r.Id == "s99");
        }
    }

    [Fact]
    public async Task Query_WithOutcomeFilter_ReturnsMatching_DBBacked()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddScoped<IAuditService, SqlAuditService>();
        services.AddScoped<IAuditTimelineQueryService, AuditTimelineQueryService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException("SQL Server not reachable.");
            await AppertureDatabaseMigrator.MigrateAsync(db);
        }

        var prefix = $"ep37.outcome.{Guid.NewGuid():N}.";
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-3);
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.AppendAsync(CreateRecord(baseTime, prefix + "dispatched", "system:scheduler", "success", outcome: "success"));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(1), prefix + "failed", "system:scheduler", "failed", outcome: "failed"));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(2), prefix + "blocked", "system:scheduler", "blocked", outcome: "blocked"));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAuditTimelineQueryService>();
            var page = await svc.QueryAsync(new AuditFilterCriteria(FromUtc: baseTime, ActionPrefix: prefix, Outcome: "failed", PageSize: 10));
            page.Items.Should().HaveCount(1);
            page.Items[0].Action.Should().Be(prefix + "failed");
            page.Items[0].Outcome.Should().Be("failed");
        }
    }

    [Fact]
    public async Task Query_WithOutcomeFilter_ExcludesNullOutcomeRows()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddScoped<IAuditService, SqlAuditService>();
        services.AddScoped<IAuditTimelineQueryService, AuditTimelineQueryService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException("SQL Server not reachable.");
            await AppertureDatabaseMigrator.MigrateAsync(db);
        }

        var testPrefix = $"test.{Guid.NewGuid():N}.";
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-2);
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.AppendAsync(CreateRecord(baseTime, testPrefix + "action", "system", "some details", outcome: null));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(1), testPrefix + "other", "system", "success", outcome: "success"));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAuditTimelineQueryService>();
            var page = await svc.QueryAsync(new AuditFilterCriteria(FromUtc: baseTime, ActionPrefix: testPrefix, Outcome: "success", PageSize: 10));
            page.Items.Should().HaveCount(1);
            page.Items[0].Outcome.Should().Be("success");
        }
    }

    [Fact]
    public async Task Query_WithModuleIdFilter_ReturnsMatching_DBBacked()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddScoped<IAuditService, SqlAuditService>();
        services.AddScoped<IAuditTimelineQueryService, AuditTimelineQueryService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException("SQL Server not reachable.");
            await AppertureDatabaseMigrator.MigrateAsync(db);
        }

        var moduleId = "ep37-mod-" + Guid.NewGuid().ToString("N")[..6];
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-2);
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.AppendAsync(CreateRecord(baseTime, "module.lifecycle.enable", "system", $"succeeded; module={moduleId}", outcome: "succeeded", moduleId: moduleId));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(1), "module.lifecycle.disable", "system", "succeeded; module=other-mod", outcome: "succeeded", moduleId: "other-mod"));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAuditTimelineQueryService>();
            var page = await svc.QueryAsync(new AuditFilterCriteria(FromUtc: baseTime, ModuleId: moduleId, PageSize: 10));
            page.Items.Should().HaveCount(1);
            page.Items[0].RelatedReferences.Should().Contain(r => r.Kind == "module" && r.Id == moduleId);
        }
    }

    [Fact]
    public async Task Query_WithScheduleIdFilter_ReturnsMatching_DBBacked()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddScoped<IAuditService, SqlAuditService>();
        services.AddScoped<IAuditTimelineQueryService, AuditTimelineQueryService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException("SQL Server not reachable.");
            await AppertureDatabaseMigrator.MigrateAsync(db);
        }

        var scheduleId = "ep37-sched-" + Guid.NewGuid().ToString("N")[..6];
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-2);
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.AppendAsync(CreateRecord(baseTime, "scheduler.schedule.dispatched", "system:scheduler", $"success; schedule:{scheduleId};RunId=r1", outcome: "success", scheduleId: scheduleId, runId: "r1"));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(1), "scheduler.schedule.dispatched", "system:scheduler", "success; schedule:other;RunId=r2", outcome: "success", scheduleId: "other", runId: "r2"));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAuditTimelineQueryService>();
            var page = await svc.QueryAsync(new AuditFilterCriteria(FromUtc: baseTime, ScheduleId: scheduleId, PageSize: 10));
            page.Items.Should().HaveCount(1);
            page.Items[0].RelatedReferences.Should().Contain(r => r.Kind == "schedule" && r.Id == scheduleId);
        }
    }

    /// <summary>
    /// Epic 37: Verifies historical backfill populates Outcome, ModuleId, ScheduleId, RunId from Details.
    /// Inserts pre-Epic-37 shaped rows (null first-class columns, data in Details), runs backfill SQL, asserts.
    /// </summary>
    [Fact]
    public async Task Migration_Backfill_PopulatesFirstClassFieldsFromDetails()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());

        // AUD-1.1: the integrity guard refuses any UPDATE of evidence columns, so the Epic 37 backfill can no longer be
        // replayed on a current database (it never should be). Instead, rows are written at the schema before Epic 37 and
        // the real migration runs its own backfill on the way to the current schema, in a throwaway catalog.
        var catalog = "Apperture_Ep37Backfill_" + Guid.NewGuid().ToString("N")[..12];
        connectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString) { InitialCatalog = catalog }.ConnectionString;
        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddScoped<IAuditService, SqlAuditService>();
        services.AddScoped<IAuditTimelineQueryService, AuditTimelineQueryService>();
        await using var provider = services.BuildServiceProvider();
        try
        {
            await RunEpic37BackfillScenarioAsync(provider);
        }
        finally
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            await DropCatalogAsync(connectionString, catalog);
        }
    }

    private static async Task RunEpic37BackfillScenarioAsync(IServiceProvider provider)
    {
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException("SQL Server not reachable.");
            await Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
                .GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db)
                .MigrateAsync("20260322230000_AddScheduleRunClaimedByHostId");
        }

        var prefix = $"ep37.backfill.{Guid.NewGuid():N}.";
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        var at1 = baseTime;
        var at2 = baseTime.AddSeconds(1);
        var at3 = baseTime.AddSeconds(2);
        var at4 = baseTime.AddSeconds(3);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            // The pre-Epic 37 schema has no Outcome or reference columns: the outcome is the first Details segment.
            db.Database.ExecuteSqlRaw("""
                INSERT INTO AuditLogs (At, Actor, Action, Details)
                VALUES
                ({0}, 'system', {1}, 'success; schedule:s-backfill;runId=r-backfill;module=m-backfill'),
                ({2}, 'system', {3}, 'failed; schedule:other'),
                ({4}, 'system', {5}, 'blocked'),
                ({6}, 'system', {7}, 'requested; originalRunId=orig-123')
                """,
                at1, prefix + "dispatched",
                at2, prefix + "failed",
                at3, prefix + "blocked",
                at4, prefix + "retry");
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            // The Epic 37 migration's own backfill runs here, then every later migration up to the current schema.
            await AppertureDatabaseMigrator.MigrateAsync(db);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAuditTimelineQueryService>();
            var page = await svc.QueryAsync(new AuditFilterCriteria(FromUtc: baseTime, ActionPrefix: prefix, PageSize: 20));
            page.Items.Should().HaveCount(4);

            var dispatched = page.Items.FirstOrDefault(e => e.Action == prefix + "dispatched");
            dispatched.Should().NotBeNull();
            dispatched!.Outcome.Should().Be("success");
            dispatched.RelatedReferences.Should().Contain(r => r.Kind == "schedule" && r.Id == "s-backfill");
            dispatched.RelatedReferences.Should().Contain(r => r.Kind == "run" && r.Id == "r-backfill");
            dispatched.RelatedReferences.Should().Contain(r => r.Kind == "module" && r.Id == "m-backfill");

            var failed = page.Items.FirstOrDefault(e => e.Action == prefix + "failed");
            failed.Should().NotBeNull();
            failed!.Outcome.Should().Be("failed");
            failed.RelatedReferences.Should().Contain(r => r.Kind == "schedule" && r.Id == "other");

            var blocked = page.Items.FirstOrDefault(e => e.Action == prefix + "blocked");
            blocked.Should().NotBeNull();
            blocked!.Outcome.Should().Be("blocked");
            blocked.RelatedReferences.Should().BeEmpty();

            var retry = page.Items.FirstOrDefault(e => e.Action == prefix + "retry");
            retry.Should().NotBeNull();
            retry!.RelatedReferences.Should().Contain(r => r.Kind == "run" && r.Id == "orig-123");
        }
    }

    private static async Task DropCatalogAsync(string connectionString, string catalog)
    {
        try
        {
            var master = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(master);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{catalog}')
                BEGIN
                    ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{catalog}];
                END
                """;
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            // Cleanup is best-effort; a leftover throwaway catalog must not fail the run.
        }
    }

    private static AuditRecord CreateRecord(DateTimeOffset at, string action, string actor, string details,
        string? outcome = "", string? moduleId = null, string? scheduleId = null, string? runId = null) =>
        new(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId(actor),
            action,
            Resource: null,
            Outcome: outcome ?? "",
            at,
            new CorrelationId(null),
            details,
            ModuleId: moduleId,
            ScheduleId: scheduleId,
            RunId: runId);
}
