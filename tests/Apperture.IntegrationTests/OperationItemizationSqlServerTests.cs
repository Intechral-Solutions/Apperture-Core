using Apperture.Core.Application.OperationItems;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// SDK-3A: SQL round-trip for platform-owned operation groups/items.
/// Soft-skips when no reachable APPERTURE_TEST_SQL_* endpoint is configured.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class OperationItemizationSqlServerTests
{
    private static async Task<AppertureDbContext?> TryCreateMigratedDbAsync()
    {
        var cs = SqlServerTestConfiguration.GetConnectionString();
        if (cs is null)
            return null;

        try
        {
            SqlServerTestConfiguration.EnsureTestDatabaseExists(cs);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(cs)
            .Options;

        var db = new AppertureDbContext(options);
        if (!await db.Database.CanConnectAsync())
        {
            await db.DisposeAsync();
            return null;
        }

        await AppertureDatabaseMigrator.MigrateAsync(db);
        return db;
    }

    [Fact]
    public async Task Sql_store_persists_group_items_and_run_queries()
    {
        await using var db = await TryCreateMigratedDbAsync();
        if (db is null)
            return; // soft-skip: unavailable SQL endpoint

        IOperationItemStore store = new SqlOperationItemStore(db);
        var runId = "sdk3a-" + Guid.NewGuid().ToString("N")[..12];

        var group = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            RunId: runId,
            ModuleId: "Sample.FileImport",
            IntegrationId: "int-sdk3a",
            ProcedureId: "Sample.FileImport.procedures.import",
            OperationKind: "file.import",
            SourceLabel: "customers-mixed"));

        group.Status.Should().Be(OperationGroupStatuses.Running);
        group.RunId.Should().Be(runId);

        await store.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId,
            runId,
            Sequence: 1,
            Status: OperationItemStatuses.Succeeded,
            SubjectKey: "C-1",
            DisplayLabel: "Customer C-1",
            ChangeSummaryJson: """{"action":"upsert"}"""));

        await store.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId,
            runId,
            Sequence: 2,
            Status: OperationItemStatuses.Failed,
            SubjectKey: "C-2",
            ErrorCode: "invalid_email",
            ErrorMessage: "bad email",
            IsRetryable: true,
            RetryCount: 0,
            LastAttemptedAtUtc: DateTimeOffset.UtcNow));

        var completed = await store.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId,
            OperationGroupStatuses.CompletedWithErrors,
            TotalCount: 2,
            SucceededCount: 1,
            FailedCount: 1,
            SkippedCount: 0));

        completed.FailedCount.Should().Be(1);
        completed.CompletedAtUtc.Should().NotBeNull();

        var byRun = await store.GetGroupsByRunAsync(runId);
        byRun.Should().ContainSingle(g => g.OperationGroupId == group.OperationGroupId);

        var items = await store.GetItemsByGroupAsync(group.OperationGroupId);
        items.Should().HaveCount(2);
        items.Should().BeInAscendingOrder(i => i.Sequence);
        items.Single(i => i.Status == OperationItemStatuses.Failed).IsRetryable.Should().BeTrue();

        // Cleanup
        db.OperationItems.RemoveRange(db.OperationItems.Where(i => i.RunId == runId));
        db.OperationGroups.RemoveRange(db.OperationGroups.Where(g => g.RunId == runId));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sql_store_QueryItems_filters_and_GetRunSummary()
    {
        await using var db = await TryCreateMigratedDbAsync();
        if (db is null)
            return;

        IOperationItemStore store = new SqlOperationItemStore(db);
        var runId = "sdk3b-" + Guid.NewGuid().ToString("N")[..12];

        var group = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            runId, "Sample.FileImport", null, null, "file.import", "batch"));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, runId, 1, OperationItemStatuses.Failed, "X-1", IsRetryable: true));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, runId, 2, OperationItemStatuses.Succeeded, "X-2"));
        await store.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, 2, 1, 1, 0));

        var page = await store.QueryItemsByGroupAsync(
            group.OperationGroupId,
            new OperationItemQuery(Status: OperationItemStatuses.Failed, RetryableOnly: true));
        page.TotalCount.Should().Be(1);
        page.Items.Single().SubjectKey.Should().Be("X-1");

        var summary = await store.GetRunSummaryAsync(runId);
        summary.GroupCount.Should().Be(1);
        summary.FailedItemCount.Should().Be(1);
        summary.HasOperationItems.Should().BeTrue();

        db.OperationItems.RemoveRange(db.OperationItems.Where(i => i.RunId == runId));
        db.OperationGroups.RemoveRange(db.OperationGroups.Where(g => g.RunId == runId));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AddOperationItemizationPersistence_registers_Sql_store()
    {
        var cs = SqlServerTestConfiguration.GetConnectionString()
                 ?? "Server=(localdb)\\mssqllocaldb;Database=Apperture_IntegrationTests;Trusted_Connection=True;";

        var services = new ServiceCollection();
        services.AddApperturePersistence(cs);
        services.AddOperationItemizationPersistence();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IOperationItemStore>();
        store.Should().BeOfType<SqlOperationItemStore>();
        scope.ServiceProvider.GetRequiredService<IOperationItemRetryAttemptStore>()
            .Should().BeOfType<SqlOperationItemRetryAttemptStore>();
        scope.ServiceProvider.GetRequiredService<IOperationItemRetryReconciliationService>()
            .Should().BeOfType<OperationItemRetryReconciliationService>();
    }

    [Fact]
    public async Task Sql_retry_attempt_store_persists_and_idempotency_replays()
    {
        await using var db = await TryCreateMigratedDbAsync();
        if (db is null)
            return;

        IOperationItemStore items = new SqlOperationItemStore(db);
        IOperationItemRetryAttemptStore attempts = new SqlOperationItemRetryAttemptStore(db);
        var runId = "sdk4a-" + Guid.NewGuid().ToString("N")[..12];

        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            runId, "Sample.FileImport", null, null, "file_import", "customers-mixed"));
        var item = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, runId, 2, OperationItemStatuses.Failed,
            SubjectKey: "C-bad",
            ErrorCode: "invalid_email",
            ErrorMessage: "bad",
            IsRetryable: true,
            MetadataJson: """{"fixtureId":"customers-mixed","rowNumber":2}"""));
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, 1, 0, 1, 0));

        var created = await attempts.CreateAsync(new CreateOperationItemRetryAttemptRequest(
            item.OperationItemId, runId, group.OperationGroupId, 1, "idem-sql",
            "actor-1", "retry", OperationItemRetryAttemptStatuses.Requested));
        var replay = await attempts.CreateAsync(new CreateOperationItemRetryAttemptRequest(
            item.OperationItemId, runId, group.OperationGroupId, 2, "idem-sql",
            "actor-1", "retry", OperationItemRetryAttemptStatuses.Requested));
        replay.OperationItemRetryAttemptId.Should().Be(created.OperationItemRetryAttemptId);

        var dispatched = await attempts.MarkDispatchedAsync(
            new MarkOperationItemRetryAttemptDispatchedRequest(created.OperationItemRetryAttemptId, "child-run-x"));
        dispatched.Status.Should().Be(OperationItemRetryAttemptStatuses.Dispatched);
        dispatched.ChildRunId.Should().Be("child-run-x");

        var parent = await items.GetItemAsync(item.OperationItemId);
        parent!.Status.Should().Be(OperationItemStatuses.Failed);
        parent.ErrorCode.Should().Be("invalid_email");

        await items.RecordRetryDispatchAsync(item.OperationItemId, DateTimeOffset.UtcNow);
        var bumped = await items.GetItemAsync(item.OperationItemId);
        bumped!.RetryCount.Should().Be(1);
        bumped.Status.Should().Be(OperationItemStatuses.Failed);
        bumped.ErrorMessage.Should().Be("bad");

        db.OperationItemRetryAttempts.RemoveRange(
            db.OperationItemRetryAttempts.Where(a => a.RunId == runId));
        db.OperationItems.RemoveRange(db.OperationItems.Where(i => i.RunId == runId));
        db.OperationGroups.RemoveRange(db.OperationGroups.Where(g => g.RunId == runId));
        await db.SaveChangesAsync();
    }
}
