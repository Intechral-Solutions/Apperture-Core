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
/// </summary>
public sealed class AuditTimelineSqlServerTests
{
    [Fact]
    public async Task Query_ReturnsOrderedEntries_NewestFirst()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
        {
            throw new InvalidOperationException(
                "SQL Server not configured. Set MSSQL_SA_PASSWORD (e.g. in repo-root .env) and ensure SQL Server is running on localhost:1433.");
        }

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddScoped<IAuditService, SqlAuditService>();
        services.AddScoped<IAuditTimelineQueryService, AuditTimelineQueryService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
            {
                throw new InvalidOperationException(
                    "SQL Server not reachable. Is the server running and is MSSQL_SA_PASSWORD correct?");
            }
            await db.Database.MigrateAsync();
        }

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.AppendAsync(CreateRecord(baseTime.AddMinutes(1), "scheduler.schedule.dispatched", "system:scheduler", "success; schedule:s1;RunId=r1"));
            await audit.AppendAsync(CreateRecord(baseTime.AddMinutes(2), RecoveryAuditActionNames.BlockedWorkRetryRequested, "user:alice", "requested; runId=r2;definitionId=wf1"));
            await audit.AppendAsync(CreateRecord(baseTime.AddMinutes(3), "execution.workflow_retry.blocked", "system", "Blocked; runId=r3;definitionId=wf2;reason=module_disabled"));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAuditTimelineQueryService>();
            var page = await svc.QueryAsync(new AuditFilterCriteria(
                FromUtc: baseTime,
                ToUtc: DateTimeOffset.UtcNow.AddMinutes(1),
                PageSize: 10));

            page.Items.Should().HaveCount(3);
            page.Items[0].Action.Should().Be("execution.workflow_retry.blocked");
            page.Items[1].Action.Should().Be(RecoveryAuditActionNames.BlockedWorkRetryRequested);
            page.Items[2].Action.Should().Be("scheduler.schedule.dispatched");
        }
    }

    [Fact]
    public async Task Query_WithActionPrefix_ReturnsFiltered()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException("SQL Server not configured. Set MSSQL_SA_PASSWORD.");

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
            await db.Database.MigrateAsync();
        }

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.AppendAsync(CreateRecord(baseTime, "scheduler.schedule.dispatched", "system:scheduler", "success"));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(1), "recovery.blocked_work.retry_requested", "user:alice", "requested"));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(2), "scheduler.schedule.blocked_by_lifecycle", "system:scheduler", "blocked"));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAuditTimelineQueryService>();
            var page = await svc.QueryAsync(new AuditFilterCriteria(
                FromUtc: baseTime,
                ActionPrefix: "scheduler.",
                PageSize: 10));

            page.Items.Should().HaveCount(2);
            page.Items.Should().OnlyContain(e => e.Action.StartsWith("scheduler."));
        }
    }

    [Fact]
    public async Task Query_WithRunIdFilter_ReturnsMatching()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException("SQL Server not configured. Set MSSQL_SA_PASSWORD.");

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
            await db.Database.MigrateAsync();
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
            throw new InvalidOperationException("SQL Server not configured. Set MSSQL_SA_PASSWORD.");

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
            await db.Database.MigrateAsync();
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
            throw new InvalidOperationException("SQL Server not configured. Set MSSQL_SA_PASSWORD.");

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
            await db.Database.MigrateAsync();
        }

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-3);
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.AppendAsync(CreateRecord(baseTime, "scheduler.schedule.dispatched", "system:scheduler", "success", outcome: "success"));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(1), "scheduler.schedule.failed", "system:scheduler", "failed", outcome: "failed"));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(2), "scheduler.schedule.blocked_by_lifecycle", "system:scheduler", "blocked", outcome: "blocked"));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAuditTimelineQueryService>();
            var page = await svc.QueryAsync(new AuditFilterCriteria(FromUtc: baseTime, Outcome: "failed", PageSize: 10));
            page.Items.Should().HaveCount(1);
            page.Items[0].Action.Should().Be("scheduler.schedule.failed");
            page.Items[0].Outcome.Should().Be("failed");
        }
    }

    [Fact]
    public async Task Query_WithOutcomeFilter_ExcludesNullOutcomeRows()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException("SQL Server not configured. Set MSSQL_SA_PASSWORD.");

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
            await db.Database.MigrateAsync();
        }

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-2);
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.AppendAsync(CreateRecord(baseTime, "test.action", "system", "some details", outcome: null));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(1), "test.other", "system", "success", outcome: "success"));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAuditTimelineQueryService>();
            var page = await svc.QueryAsync(new AuditFilterCriteria(FromUtc: baseTime, Outcome: "success", PageSize: 10));
            page.Items.Should().HaveCount(1);
            page.Items[0].Outcome.Should().Be("success");
        }
    }

    [Fact]
    public async Task Query_WithModuleIdFilter_ReturnsMatching_DBBacked()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException("SQL Server not configured. Set MSSQL_SA_PASSWORD.");

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
            await db.Database.MigrateAsync();
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
            throw new InvalidOperationException("SQL Server not configured. Set MSSQL_SA_PASSWORD.");

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
            await db.Database.MigrateAsync();
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
