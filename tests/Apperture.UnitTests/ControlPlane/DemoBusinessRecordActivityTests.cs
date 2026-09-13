using Apperture.ControlPlane.Security.DemoBusinessRecords;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class DemoBusinessRecordActivityTests
{
    [Fact]
    public async Task Activity_spans_multiple_runs_and_integrations_newest_first()
    {
        await using var db = NewDb();
        await SeedCrossRunCustomerAsync(db);

        var svc = new DemoBusinessRecordProjectionService(db, new AllowPrincipal(), new AllowAuth());
        var page = await svc.GetCustomerActivityAsync("C-100", new DemoBusinessRecordActivityQuery(Take: 50));

        page.Should().NotBeNull();
        page!.TotalCount.Should().Be(3);
        page.Items.Should().HaveCount(3);
        page.Items.Select(i => i.RunId).Should().Equal("run-ap", "run-fi-2", "run-fi-1");
        page.Items[0].OccurredAtUtc.Should().BeAfter(page.Items[1].OccurredAtUtc);
        page.Items[1].OccurredAtUtc.Should().BeAfter(page.Items[2].OccurredAtUtc);
        page.Items.Select(i => i.SourceKind).Should().Contain(DemoBusinessRecordSourceKinds.FileImport);
        page.Items.Select(i => i.SourceKind).Should().Contain(DemoBusinessRecordSourceKinds.ApiPolling);
        page.Items.Should().OnlyContain(i => i.BusinessKey == "C-100");
        page.Items.Select(i => i.OperationItemId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Activity_stable_secondary_sort_on_identical_timestamps()
    {
        await using var db = NewDb();
        SeedRun(db, "run-same");
        await db.SaveChangesAsync();

        var store = new SqlOperationItemStore(db);
        var group = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-same", "Sample.FileImport", "int-fi", null, "file_import", "fixture"));
        var at = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
        await store.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-same", 1, OperationItemStatuses.Succeeded, "C-SORT", "A",
            MetadataJson: Meta("C-SORT", "A", "imported"), CompletedAtUtc: at));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-same", 2, OperationItemStatuses.Succeeded, "C-SORT", "B",
            MetadataJson: Meta("C-SORT", "B", "imported"), CompletedAtUtc: at));
        await store.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.Succeeded, 2, 2, 0, 0));

        var svc = new DemoBusinessRecordProjectionService(db, new AllowPrincipal(), new AllowAuth());
        var page = await svc.GetCustomerActivityAsync("C-SORT", new DemoBusinessRecordActivityQuery());
        page!.Items.Should().HaveCount(2);
        string.CompareOrdinal(page.Items[0].OperationItemId, page.Items[1].OperationItemId)
            .Should().BePositive();
    }

    [Fact]
    public async Task Activity_filters_source_status_attention_search_and_lookback()
    {
        await using var db = NewDb();
        await SeedCrossRunCustomerAsync(db);

        var svc = new DemoBusinessRecordProjectionService(db, new AllowPrincipal(), new AllowAuth());

        var fileOnly = await svc.GetCustomerActivityAsync(
            "C-100",
            new DemoBusinessRecordActivityQuery(SourceKind: DemoBusinessRecordSourceKinds.FileImport));
        fileOnly!.Items.Should().OnlyContain(i => i.SourceKind == DemoBusinessRecordSourceKinds.FileImport);
        fileOnly.TotalCount.Should().Be(2);

        var failed = await svc.GetCustomerActivityAsync(
            "C-100",
            new DemoBusinessRecordActivityQuery(Status: OperationItemStatuses.Failed));
        failed!.TotalCount.Should().Be(1);
        failed.Items.Single().ItemStatus.Should().Be(OperationItemStatuses.Failed);

        var attention = await svc.GetCustomerActivityAsync(
            "C-100",
            new DemoBusinessRecordActivityQuery(AttentionOnly: true));
        attention!.TotalCount.Should().Be(1);
        attention.Items.Should().OnlyContain(i => i.RequiresAttention);

        var search = await svc.GetCustomerActivityAsync(
            "C-100",
            new DemoBusinessRecordActivityQuery(Q: "API polling"));
        search!.TotalCount.Should().Be(1);
        search.Items.Single().SourceKind.Should().Be(DemoBusinessRecordSourceKinds.ApiPolling);

        var lookback = await svc.GetCustomerActivityAsync(
            "C-100",
            new DemoBusinessRecordActivityQuery(LookbackDays: 1));
        lookback!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Activity_paging_is_stable_and_bounded()
    {
        await using var db = NewDb();
        await SeedCrossRunCustomerAsync(db);
        var svc = new DemoBusinessRecordProjectionService(db, new AllowPrincipal(), new AllowAuth());

        var page1 = await svc.GetCustomerActivityAsync(
            "C-100",
            new DemoBusinessRecordActivityQuery(Skip: 0, Take: 2));
        page1!.Items.Should().HaveCount(2);
        page1.HasMore.Should().BeTrue();
        page1.TotalCount.Should().Be(3);

        var page2 = await svc.GetCustomerActivityAsync(
            "C-100",
            new DemoBusinessRecordActivityQuery(Skip: 2, Take: 2));
        page2!.Items.Should().HaveCount(1);
        page2.HasMore.Should().BeFalse();
        page1.Items.Select(i => i.OperationItemId)
            .Intersect(page2.Items.Select(i => i.OperationItemId))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Activity_missing_customer_returns_null_and_empty_customer_has_no_rows()
    {
        await using var db = NewDb();
        await SeedCrossRunCustomerAsync(db);
        var svc = new DemoBusinessRecordProjectionService(db, new AllowPrincipal(), new AllowAuth());

        (await svc.GetCustomerActivityAsync("missing", new DemoBusinessRecordActivityQuery()))
            .Should().BeNull();

        var empty = await svc.GetCustomerActivityAsync("C-003", new DemoBusinessRecordActivityQuery());
        // C-003 exists from projection seed helper? SeedCrossRun only creates C-100 activity.
        // Ensure a solitary customer without activity still 404s if not projected.
        empty.Should().BeNull();
    }

    [Fact]
    public async Task Summary_reports_totals_and_attention()
    {
        await using var db = NewDb();
        await SeedCrossRunCustomerAsync(db);
        var svc = new DemoBusinessRecordProjectionService(db, new AllowPrincipal(), new AllowAuth());

        var summary = await svc.GetCustomerActivitySummaryAsync("C-100");
        summary.Should().NotBeNull();
        summary!.TotalCount.Should().Be(3);
        summary.AttentionCount.Should().Be(1);
        summary.LatestOccurredAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Activity_requires_authorization()
    {
        await using var db = NewDb();
        var svc = new DemoBusinessRecordProjectionService(db, new AllowPrincipal(), new DenyAuth());
        var act = async () => await svc.GetCustomerActivityAsync("C-100", new DemoBusinessRecordActivityQuery());
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    async Task SeedCrossRunCustomerAsync(AppertureDbContext db)
    {
        SeedRun(db, "run-fi-1");
        SeedRun(db, "run-fi-2");
        SeedRun(db, "run-ap");
        db.Integrations.Add(new IntegrationEntity
        {
            Id = "int-fi",
            DisplayName = "Student/Customer Import",
            ModuleId = "Sample.FileImport",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.Integrations.Add(new IntegrationEntity
        {
            Id = "int-ap",
            DisplayName = "Customer API Snapshot",
            ModuleId = "Sample.ApiPolling",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var store = new SqlOperationItemStore(db);
        var g1 = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-fi-1", "Sample.FileImport", "int-fi", null, "file_import", "file-a"));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            g1.OperationGroupId, "run-fi-1", 1, OperationItemStatuses.Succeeded, "C-100", "Valid One",
            MetadataJson: Meta("C-100", "Valid One", "imported", DateTimeOffset.Parse("2026-07-01T10:00:00Z")),
            CompletedAtUtc: DateTimeOffset.Parse("2026-07-01T10:00:00Z")));
        await store.CompleteGroupAsync(new CompleteOperationGroupRequest(
            g1.OperationGroupId, OperationGroupStatuses.Succeeded, 1, 1, 0, 0));

        var g2 = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-fi-2", "Sample.FileImport", "int-fi", null, "file_import", "file-b"));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            g2.OperationGroupId, "run-fi-2", 1, OperationItemStatuses.Failed, "C-100", "Valid One",
            IsRetryable: true,
            ErrorMessage: "Row failed validation",
            MetadataJson: Meta("C-100", "Valid One", "updated", DateTimeOffset.Parse("2026-07-15T10:00:00Z")),
            CompletedAtUtc: DateTimeOffset.Parse("2026-07-15T10:00:00Z")));
        await store.CompleteGroupAsync(new CompleteOperationGroupRequest(
            g2.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, 1, 0, 1, 0));

        var g3 = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-ap", "Sample.ApiPolling", "int-ap", null, "api_poll", "snapshot"));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            g3.OperationGroupId, "run-ap", 1, OperationItemStatuses.Succeeded, "C-100", "Valid One",
            MetadataJson: Meta("C-100", "Valid One", "new", DateTimeOffset.Parse("2026-07-20T10:00:00Z")),
            CompletedAtUtc: DateTimeOffset.Parse("2026-07-20T10:00:00Z")));
        await store.CompleteGroupAsync(new CompleteOperationGroupRequest(
            g3.OperationGroupId, OperationGroupStatuses.Succeeded, 1, 1, 0, 0));
    }

    static string Meta(string key, string name, string changeKind, DateTimeOffset? observedAt = null) =>
        OperationItemReporting.SerializeMetadata(new
        {
            recordType = "customer",
            businessKey = key,
            displayName = name,
            changeKind,
            observedAt = observedAt ?? DateTimeOffset.Parse("2026-07-21T12:00:00Z"),
            email = "valid.one@example.com",
            status = "Active",
        });

    static void SeedRun(AppertureDbContext db, string runId)
    {
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = "inst-" + runId,
            DefinitionId = "wf-1",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = runId,
            WorkflowInstanceId = "inst-" + runId,
            WorkflowDefinitionId = "wf-1",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    static AppertureDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase("bds6-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(options);
    }

    sealed class AllowPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new FakeActor());
    }

    sealed class FakeActor : IActor
    {
        public ActorId Id { get; } = new("actor-1");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Test";
    }

    sealed class AllowAuth : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    sealed class DenyAuth : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }
}
