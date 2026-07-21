using System.Text.Json;
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

public sealed class DemoBusinessRecordProjectionTests
{
    [Fact]
    public void Projector_groups_shared_keys_fileimport_only_apipolling_only_and_conflicts()
    {
        var t0 = DateTimeOffset.Parse("2026-07-21T10:00:00Z");
        var t1 = DateTimeOffset.Parse("2026-07-21T11:00:00Z");

        var projections = DemoBusinessRecordProjector.Project(
        [
            Obs("C-001", "Sample.FileImport", "int-fi", "Ada Lovelace", "ada@example.com", "Active", t0, "oi-fi-1"),
            Obs("C-001", "Sample.ApiPolling", "int-ap", "Ada Lovelace", "ada@example.com", "Active", t1, "oi-ap-1"),
            Obs("C-002", "Sample.FileImport", "int-fi", "Alan Turing", "alan@example.com", "Active", t0, "oi-fi-2"),
            Obs("C-002", "Sample.ApiPolling", "int-ap", "Alan Turing", "alan@example.com", "Active", t1, "oi-ap-2"),
            Obs("C-003", "Sample.FileImport", "int-fi", "Grace Hopper", "grace@example.com", "Inactive", t0, "oi-fi-3"),
            Obs("C-400", "Sample.ApiPolling", "int-ap", "Delta Media", "team@deltamedia.example", "Inactive", t1, "oi-ap-4"),
            Obs("C-100", "Sample.FileImport", "int-fi", "Valid One", "valid.one@example.com", "Active", t0, "oi-fi-100"),
            Obs("C-100", "Sample.ApiPolling", "int-ap", "Valid One", "conflict.c100@apipolling.example", "Inactive", t1, "oi-ap-100"),
        ]);

        projections.Should().Contain(p => p.BusinessKey == "C-001" && p.SourceCount == 2 && !p.HasConflict);
        projections.Should().Contain(p => p.BusinessKey == "C-002" && p.SourceCount == 2 && !p.HasConflict);
        projections.Should().Contain(p => p.BusinessKey == "C-003" && p.SourceCount == 1
            && p.Sources.Single().SourceKind == DemoBusinessRecordSourceKinds.FileImport);
        projections.Should().Contain(p => p.BusinessKey == "C-400" && p.SourceCount == 1
            && p.Sources.Single().SourceKind == DemoBusinessRecordSourceKinds.ApiPolling);

        var conflict = projections.Single(p => p.BusinessKey == "C-100");
        conflict.HasConflict.Should().BeTrue();
        conflict.Conflicts.Should().Contain(c => c.Field == "email");
        conflict.Conflicts.Should().Contain(c => c.Field == "status");
        conflict.Conflicts.Select(c => c.Field).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        conflict.PrimaryConflictSummary.Should().NotBeNullOrWhiteSpace();
        conflict.Sources.Should().OnlyContain(s =>
            !string.IsNullOrWhiteSpace(s.OperationItemId)
            && !string.IsNullOrWhiteSpace(s.RunId)
            && !string.IsNullOrWhiteSpace(s.SourceKindLabel));
        conflict.Sources.Should().Contain(s => s.SourceKindLabel == "File import");
        conflict.Sources.Should().Contain(s => s.SourceKindLabel == "API polling");
    }

    [Fact]
    public void Projector_skips_missing_metadata_and_keeps_latest_per_source()
    {
        var older = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
        var newer = DateTimeOffset.Parse("2026-07-21T10:00:00Z");

        var projections = DemoBusinessRecordProjector.Project(
        [
            Obs("C-001", "Sample.FileImport", "int-fi", "Old Name", "old@example.com", "Pending", older, "oi-old"),
            Obs("C-001", "Sample.FileImport", "int-fi", "New Name", "new@example.com", "Active", newer, "oi-new"),
            new DemoBusinessRecordObservationInput(
                "order", "C-999", "Sample.FileImport", "int-fi", "file_import", "oi-x", "og-x", "run-x",
                "Nope", null, null, null, newer),
        ]);

        var one = projections.Should().ContainSingle().Subject;
        one.BusinessKey.Should().Be("C-001");
        one.Sources.Should().ContainSingle();
        one.Sources[0].OperationItemId.Should().Be("oi-new");
        one.DisplayName.Should().Be("New Name");
        one.PrimaryEmail.Should().Be("new@example.com");
    }

    [Fact]
    public void TryParse_reads_bds2_metadata_and_summary_email_fallback()
    {
        var meta = OperationItemReporting.SerializeMetadata(new
        {
            recordType = "customer",
            businessKey = "C-001",
            displayName = "Ada Lovelace",
            changeKind = "imported",
            observedAt = DateTimeOffset.Parse("2026-07-21T12:00:00Z"),
            status = "Active",
        });
        var summary = OperationItemReporting.SerializeChangeSummary(new
        {
            changeKind = "imported",
            summary = "row=1; id=C-001; email=ada@example.com",
        });

        var obs = DemoBusinessRecordProjector.TryParseItemObservation(
            "oi-1", "og-1", "run-1", "Sample.FileImport", "int-1", "file_import",
            "C-001", "Ada Lovelace", meta, summary,
            DateTimeOffset.Parse("2026-07-21T12:00:00Z"),
            DateTimeOffset.Parse("2026-07-21T12:00:00Z"));

        obs.Should().NotBeNull();
        obs!.BusinessKey.Should().Be("C-001");
        obs.Email.Should().Be("ada@example.com");
        obs.Status.Should().Be("Active");
    }

    [Fact]
    public void List_query_supports_conflict_filter()
    {
        var all = DemoBusinessRecordProjector.Project(
        [
            Obs("C-001", "Sample.FileImport", "int-fi", "A", "a@x", "Active", DateTimeOffset.UtcNow, "1"),
            Obs("C-001", "Sample.ApiPolling", "int-ap", "A", "a@x", "Active", DateTimeOffset.UtcNow, "2"),
            Obs("C-100", "Sample.FileImport", "int-fi", "B", "b1@x", "Active", DateTimeOffset.UtcNow, "3"),
            Obs("C-100", "Sample.ApiPolling", "int-ap", "B", "b2@x", "Inactive", DateTimeOffset.UtcNow, "4"),
        ]);

        var conflictsOnly = DemoBusinessRecordProjector.ApplyListQuery(
            all, new DemoBusinessRecordListQuery(HasConflict: true));
        conflictsOnly.TotalCount.Should().Be(1);
        conflictsOnly.Items.Single().BusinessKey.Should().Be("C-100");
    }

    [Fact]
    public async Task Service_projects_from_operation_items_with_integration_read_auth()
    {
        await using var db = NewDb();
        SeedRun(db, "run-fi", "wf-fi");
        SeedRun(db, "run-ap", "wf-ap");
        await db.SaveChangesAsync();

        var store = new SqlOperationItemStore(db);
        var fiGroup = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-fi", "Sample.FileImport", "int-fi", "proc.import", "file_import", "customers-valid"));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            fiGroup.OperationGroupId, "run-fi", 1, OperationItemStatuses.Succeeded, "C-001", "Ada Lovelace",
            MetadataJson: Meta("C-001", "Ada Lovelace", "imported", "ada@example.com", "Active"),
            ChangeSummaryJson: """{"changeKind":"imported","summary":"row=1; id=C-001; email=ada@example.com"}""",
            CompletedAtUtc: DateTimeOffset.Parse("2026-07-21T10:00:00Z")));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            fiGroup.OperationGroupId, "run-fi", 2, OperationItemStatuses.Succeeded, "C-003", "Grace Hopper",
            MetadataJson: Meta("C-003", "Grace Hopper", "imported", "grace@example.com", "Inactive"),
            CompletedAtUtc: DateTimeOffset.Parse("2026-07-21T10:00:00Z")));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            fiGroup.OperationGroupId, "run-fi", 3, OperationItemStatuses.Succeeded, "C-100", "Valid One",
            MetadataJson: Meta("C-100", "Valid One", "imported", "valid.one@example.com", "Active"),
            CompletedAtUtc: DateTimeOffset.Parse("2026-07-21T10:00:00Z")));
        await store.CompleteGroupAsync(new CompleteOperationGroupRequest(
            fiGroup.OperationGroupId, OperationGroupStatuses.Succeeded, 3, 3, 0, 0));

        var apGroup = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-ap", "Sample.ApiPolling", "int-ap", "proc.poll", "api_poll", "snapshot-initial"));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            apGroup.OperationGroupId, "run-ap", 1, OperationItemStatuses.Succeeded, "C-001", "Ada Lovelace",
            MetadataJson: Meta("C-001", "Ada Lovelace", "new", "ada@example.com", "Active"),
            CompletedAtUtc: DateTimeOffset.Parse("2026-07-21T11:00:00Z")));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            apGroup.OperationGroupId, "run-ap", 2, OperationItemStatuses.Succeeded, "C-400", "Delta Media",
            MetadataJson: Meta("C-400", "Delta Media", "new", "team@deltamedia.example", "Inactive"),
            CompletedAtUtc: DateTimeOffset.Parse("2026-07-21T11:00:00Z")));
        await store.AppendItemAsync(new AppendOperationItemRequest(
            apGroup.OperationGroupId, "run-ap", 3, OperationItemStatuses.Succeeded, "C-100", "Valid One",
            MetadataJson: Meta("C-100", "Valid One", "new", "conflict.c100@apipolling.example", "Inactive"),
            CompletedAtUtc: DateTimeOffset.Parse("2026-07-21T11:00:00Z")));
        await store.CompleteGroupAsync(new CompleteOperationGroupRequest(
            apGroup.OperationGroupId, OperationGroupStatuses.Succeeded, 3, 3, 0, 0));

        var svc = new DemoBusinessRecordProjectionService(db, new AllowPrincipal(), new AllowAuth());
        var list = await svc.ListCustomersAsync(new DemoBusinessRecordListQuery());
        list.TotalCount.Should().Be(4);
        list.Items.Should().Contain(i => i.BusinessKey == "C-001" && i.SourceCount == 2);
        list.Items.Should().Contain(i => i.BusinessKey == "C-003" && i.SourceCount == 1);
        list.Items.Should().Contain(i => i.BusinessKey == "C-400" && i.SourceCount == 1);
        var conflict = list.Items.Single(i => i.BusinessKey == "C-100");
        conflict.HasConflict.Should().BeTrue();

        var detail = await svc.GetCustomerAsync("C-100");
        detail.Should().NotBeNull();
        detail!.Conflicts.Should().Contain(c => c.Field == "email");
        detail.Sources.Should().OnlyContain(s => s.RunId == "run-fi" || s.RunId == "run-ap");
    }

    [Fact]
    public async Task Service_requires_integration_read()
    {
        await using var db = NewDb();
        var svc = new DemoBusinessRecordProjectionService(db, new AllowPrincipal(), new DenyAuth());
        var act = async () => await svc.ListCustomersAsync(new DemoBusinessRecordListQuery());
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    static DemoBusinessRecordObservationInput Obs(
        string key,
        string moduleId,
        string integrationId,
        string displayName,
        string email,
        string status,
        DateTimeOffset at,
        string itemId) =>
        new(
            OperationItemBusinessMetadataKeys.CustomerRecordType,
            key,
            moduleId,
            integrationId,
            moduleId.Contains("ApiPolling", StringComparison.Ordinal) ? "api_poll" : "file_import",
            itemId,
            "og-" + itemId,
            "run-" + itemId,
            displayName,
            email,
            status,
            "imported",
            at);

    static string Meta(string key, string name, string changeKind, string email, string status) =>
        OperationItemReporting.SerializeMetadata(new
        {
            recordType = "customer",
            businessKey = key,
            displayName = name,
            changeKind,
            observedAt = DateTimeOffset.Parse("2026-07-21T12:00:00Z"),
            email,
            status,
        });

    static void SeedRun(AppertureDbContext db, string runId, string definitionId)
    {
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = "inst-" + runId,
            DefinitionId = definitionId,
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = runId,
            WorkflowInstanceId = "inst-" + runId,
            WorkflowDefinitionId = definitionId,
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    static AppertureDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase("bds3-" + Guid.NewGuid().ToString("N"))
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
