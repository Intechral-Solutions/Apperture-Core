using Apperture.ControlPlane.Security;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sample.FileImport;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class OperationItemRetryServiceTests
{
    private static readonly WorkflowDefinitionId FileImportDef = FileImportModule.DefinitionId;

    [Fact]
    public async Task RequestRetry_FileImport_failed_row_creates_attempt_and_child_run()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (item, group) = await SeedFailedFileImportItemAsync(items, runId: "run-parent");
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var service = CreateService(db, items, attempts, allowAuthz: true);

        var result = await service.RequestRetryAsync(
            new OperationRunId("run-parent"),
            item.OperationItemId,
            new RequestOperationItemRetryDto("idem-1", "operator retry"));

        result.Should().NotBeNull();
        result!.Status.Should().Be(OperationItemRetryAttemptStatuses.Dispatched);
        result.ChildRunId.Should().NotBeNullOrWhiteSpace();
        result.AttemptIndex.Should().Be(1);
        result.IdempotencyKey.Should().Be("idem-1");

        var child = await db.OperationRuns.SingleAsync(r => r.Id == result.ChildRunId);
        child.RetryOfRunId.Should().Be("run-parent");

        var start = await db.WorkflowStartRequests.SingleAsync(s => s.RunId == result.ChildRunId);
        start.IdempotencyKey.Should().Be(OperationItemRetryService.BuildDispatchIdempotencyKey(item.OperationItemId, "idem-1"));
        start.ParametersJson.Should().Contain("retryMode");
        start.ParametersJson.Should().Contain("fixtureId");
        start.ParametersJson.Should().NotContain("ExternalId,Name");

        var parentAfter = await items.GetItemAsync(item.OperationItemId);
        parentAfter!.Status.Should().Be(OperationItemStatuses.Failed);
        parentAfter.ErrorCode.Should().Be(item.ErrorCode);
        parentAfter.ErrorMessage.Should().Be(item.ErrorMessage);
        parentAfter.RetryCount.Should().Be(1);
        parentAfter.LastAttemptedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RequestRetry_same_idempotency_key_returns_same_attempt_and_child_run()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (item, _) = await SeedFailedFileImportItemAsync(items, runId: "run-parent");
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var service = CreateService(db, items, attempts, allowAuthz: true);

        var first = await service.RequestRetryAsync(
            new OperationRunId("run-parent"),
            item.OperationItemId,
            new RequestOperationItemRetryDto("same-key"));
        var second = await service.RequestRetryAsync(
            new OperationRunId("run-parent"),
            item.OperationItemId,
            new RequestOperationItemRetryDto("same-key"));

        second!.OperationItemRetryAttemptId.Should().Be(first!.OperationItemRetryAttemptId);
        second.ChildRunId.Should().Be(first.ChildRunId);
        (await attempts.ListByItemAsync(item.OperationItemId)).Should().HaveCount(1);
        (await db.OperationRuns.CountAsync(r => r.RetryOfRunId == "run-parent")).Should().Be(1);
    }

    [Fact]
    public async Task RequestRetry_rejects_non_failed_item()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-parent",             FileImportModule.ModuleIdValue, "int-1", "proc", "file_import"));
        var item = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-parent", 1, OperationItemStatuses.Succeeded,
            SubjectKey: "C-1", MetadataJson: """{"fixtureId":"customers-mixed","rowNumber":1}"""));
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.Succeeded, 1, 1, 0, 0));
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var service = CreateService(db, items, attempts, allowAuthz: true);

        var act = async () => await service.RequestRetryAsync(
            new OperationRunId("run-parent"),
            item.OperationItemId,
            new RequestOperationItemRetryDto("k1"));

        var ex = await act.Should().ThrowAsync<OperationItemRetryException>();
        ex.Which.ErrorCode.Should().Be(OperationItemRetryErrorCodes.NotFailed);
        (await attempts.ListByItemAsync(item.OperationItemId)).Should().BeEmpty();
    }

    [Fact]
    public async Task RequestRetry_rejects_non_retryable_item()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-parent", FileImportModule.ModuleIdValue, "int-1", "proc", "file_import"));
        var item = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-parent", 1, OperationItemStatuses.Failed,
            IsRetryable: false,
            ErrorCode: "skipped_max_rows",
            MetadataJson: """{"fixtureId":"customers-mixed","rowNumber":1}"""));
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, 1, 0, 1, 0));
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var service = CreateService(db, items, attempts, allowAuthz: true);

        var act = async () => await service.RequestRetryAsync(
            new OperationRunId("run-parent"),
            item.OperationItemId,
            new RequestOperationItemRetryDto("k1"));

        (await act.Should().ThrowAsync<OperationItemRetryException>())
            .Which.ErrorCode.Should().Be(OperationItemRetryErrorCodes.NotRetryable);
    }

    [Fact]
    public async Task RequestRetry_missing_metadata_creates_blocked_attempt()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-parent", FileImportModule.ModuleIdValue, "int-1", "proc", "file_import"));
        var item = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-parent", 1, OperationItemStatuses.Failed,
            IsRetryable: true,
            ErrorCode: "invalid_email",
            MetadataJson: """{"rowNumber":2}"""));
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, 1, 0, 1, 0));
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var service = CreateService(db, items, attempts, allowAuthz: true);

        var result = await service.RequestRetryAsync(
            new OperationRunId("run-parent"),
            item.OperationItemId,
            new RequestOperationItemRetryDto("meta-miss"));

        result!.Status.Should().Be(OperationItemRetryAttemptStatuses.Blocked);
        result.ErrorCode.Should().Be(OperationItemRetryErrorCodes.MissingRetryMetadata);
        result.ChildRunId.Should().BeNull();
        (await db.OperationRuns.CountAsync(r => r.RetryOfRunId == "run-parent")).Should().Be(0);
    }

    [Fact]
    public async Task RequestRetry_module_without_handler_rejects()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-parent", "Other.Module", "int-1", "proc", "other_kind"));
        var item = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, "run-parent", 1, OperationItemStatuses.Failed,
            IsRetryable: true,
            MetadataJson: """{"fixtureId":"customers-mixed","rowNumber":1}"""));
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.Failed, 1, 0, 1, 0));
        SeedRun(db, "run-parent", "Other.Module.Workflow");
        await db.SaveChangesAsync();

        var service = CreateService(db, items, attempts, allowAuthz: true);

        var act = async () => await service.RequestRetryAsync(
            new OperationRunId("run-parent"),
            item.OperationItemId,
            new RequestOperationItemRetryDto("k1"));

        (await act.Should().ThrowAsync<OperationItemRetryException>())
            .Which.ErrorCode.Should().Be(OperationItemRetryErrorCodes.ModuleUnsupported);
    }

    [Fact]
    public async Task RequestRetry_unauthorized_throws()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (item, _) = await SeedFailedFileImportItemAsync(items, runId: "run-parent");
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var service = CreateService(db, items, attempts, allowAuthz: false);

        var act = async () => await service.RequestRetryAsync(
            new OperationRunId("run-parent"),
            item.OperationItemId,
            new RequestOperationItemRetryDto("k1"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RequestRetry_mismatched_run_returns_null()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (item, _) = await SeedFailedFileImportItemAsync(items, runId: "run-a");
        SeedRun(db, "run-b", FileImportDef.Value);
        await db.SaveChangesAsync();

        var service = CreateService(db, items, attempts, allowAuthz: true);

        var result = await service.RequestRetryAsync(
            new OperationRunId("run-b"),
            item.OperationItemId,
            new RequestOperationItemRetryDto("k1"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAttempts_returns_created_attempts()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (item, _) = await SeedFailedFileImportItemAsync(items, runId: "run-parent");
        SeedRun(db, "run-parent", FileImportDef.Value);
        await db.SaveChangesAsync();

        var service = CreateService(db, items, attempts, allowAuthz: true);
        await service.RequestRetryAsync(
            new OperationRunId("run-parent"),
            item.OperationItemId,
            new RequestOperationItemRetryDto("list-1"));

        var list = await service.ListAttemptsAsync(new OperationRunId("run-parent"), item.OperationItemId);
        list.Should().ContainSingle();
        list![0].Status.Should().Be(OperationItemRetryAttemptStatuses.Dispatched);
    }

    [Fact]
    public void BuildDispatchIdempotencyKey_stays_within_bound()
    {
        var longKey = new string('x', 200);
        var built = OperationItemRetryService.BuildDispatchIdempotencyKey(Guid.NewGuid().ToString("N"), longKey);
        built.Length.Should().BeLessThanOrEqualTo(OperationItemRetryBounds.DispatchIdempotencyKeyMaxLength);
        built.Should().StartWith("oir:");
    }

    private static OperationItemRetryService CreateService(
        AppertureDbContext db,
        IOperationItemStore items,
        IOperationItemRetryAttemptStore attempts,
        bool allowAuthz)
    {
        var definitionResolver = new StubDefinitionResolver(new WorkflowDefinition(
            FileImportDef,
            "Import",
            FileImportModule.ModuleIdValue,
            new[] { "Validate", "Parse", "ProcessRows", "Complete" }));
        var dispatch = new WorkflowStartDispatchService(definitionResolver, new SqlWorkflowPersistence(db), db);
        IWorkflowRunRetryAuthorizationService authz = allowAuthz ? new AllowRetryAuthz() : new DenyRetryAuthz();
        return new OperationItemRetryService(
            db,
            items,
            attempts,
            [new FileImportOperationItemRetryHandler()],
            authz,
            dispatch,
            new StubPrincipal());
    }

    private static async Task<(OperationItem item, OperationGroup group)> SeedFailedFileImportItemAsync(
        InMemoryOperationItemStore items,
        string runId)
    {
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            runId,
            FileImportModule.ModuleIdValue,
            IntegrationId: null,
            "Sample.FileImport.procedures.import",
            "file_import",
            SourceLabel: "customers-mixed"));

        var item = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId,
            runId,
            Sequence: 2,
            Status: OperationItemStatuses.Failed,
            SubjectKey: "C-bad",
            DisplayLabel: "C-bad",
            ErrorCode: "invalid_email",
            ErrorMessage: "bad email",
            IsRetryable: true,
            MetadataJson: """{"fixtureId":"customers-mixed","rowNumber":2,"errorCode":"invalid_email"}"""));

        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId,
            OperationGroupStatuses.CompletedWithErrors,
            TotalCount: 1,
            SucceededCount: 0,
            FailedCount: 1,
            SkippedCount: 0));

        return (item, group);
    }

    private static void SeedRun(AppertureDbContext db, string runId, string definitionId)
    {
        var instanceId = "i-" + runId;
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = instanceId,
            DefinitionId = definitionId,
            Status = "failed",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Version = 1,
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = instanceId,
            DefinitionId = definitionId,
            Status = "failed",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CorrelationKey = "corr-" + runId,
        });
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private sealed class StubDefinitionResolver(WorkflowDefinition definition) : IWorkflowDefinitionResolver
    {
        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowDefinition?>(
                string.Equals(id.Value, definition.Id.Value, StringComparison.Ordinal) ? definition : null);
    }

    private sealed class AllowRetryAuthz : IWorkflowRunRetryAuthorizationService
    {
        public ValueTask EnsureCanRetryRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class DenyRetryAuthz : IWorkflowRunRetryAuthorizationService
    {
        public ValueTask EnsureCanRetryRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("denied");
    }

    private sealed class StubPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new StubActor());
    }

    private sealed class StubActor : IActor
    {
        public ActorId Id { get; } = new("actor-test");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Test";
    }
}
