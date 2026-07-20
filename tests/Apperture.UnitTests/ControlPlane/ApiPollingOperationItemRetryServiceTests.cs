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
using Sample.ApiPolling;
using Sample.FileImport;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class ApiPollingOperationItemRetryServiceTests
{
    private static readonly WorkflowDefinitionId ApiPollingDef = ApiPollingModule.DefinitionId;

    [Fact]
    public async Task RequestRetry_ApiPolling_failed_record_creates_attempt_and_child_run()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (item, _) = await SeedFailedApiPollingItemAsync(items, runId: "run-ap-parent");
        SeedRun(db, "run-ap-parent", ApiPollingDef.Value);
        await db.SaveChangesAsync();

        var service = CreateService(db, items, attempts);

        var result = await service.RequestRetryAsync(
            new OperationRunId("run-ap-parent"),
            item.OperationItemId,
            new RequestOperationItemRetryDto("idem-ap-1", "operator retry"));

        result.Should().NotBeNull();
        result!.Status.Should().Be(OperationItemRetryAttemptStatuses.Dispatched);
        result.ChildRunId.Should().NotBeNullOrWhiteSpace();

        var start = await db.WorkflowStartRequests.SingleAsync(s => s.RunId == result.ChildRunId);
        start.ParametersJson.Should().Contain("retryMode");
        start.ParametersJson.Should().Contain("retrySequence");
        start.ParametersJson.Should().Contain("snapshot-errors");
        start.ParametersJson.Should().NotContain("\"records\"");

        var parentAfter = await items.GetItemAsync(item.OperationItemId);
        parentAfter!.Status.Should().Be(OperationItemStatuses.Failed);
        parentAfter.ErrorCode.Should().Be(item.ErrorCode);
        parentAfter.RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task RequestRetry_ApiPolling_same_idempotency_key_returns_same_attempt()
    {
        var db = NewDb();
        var items = new InMemoryOperationItemStore();
        var attempts = new InMemoryOperationItemRetryAttemptStore();
        var (item, _) = await SeedFailedApiPollingItemAsync(items, runId: "run-ap-idem");
        SeedRun(db, "run-ap-idem", ApiPollingDef.Value);
        await db.SaveChangesAsync();

        var service = CreateService(db, items, attempts);
        var first = await service.RequestRetryAsync(
            new OperationRunId("run-ap-idem"),
            item.OperationItemId,
            new RequestOperationItemRetryDto("same-ap"));
        var second = await service.RequestRetryAsync(
            new OperationRunId("run-ap-idem"),
            item.OperationItemId,
            new RequestOperationItemRetryDto("same-ap"));

        second!.OperationItemRetryAttemptId.Should().Be(first!.OperationItemRetryAttemptId);
        second.ChildRunId.Should().Be(first.ChildRunId);
    }

    private static OperationItemRetryService CreateService(
        AppertureDbContext db,
        IOperationItemStore items,
        IOperationItemRetryAttemptStore attempts)
    {
        var definitionResolver = new StubDefinitionResolver(new WorkflowDefinition(
            ApiPollingDef,
            "Poll",
            ApiPollingModule.ModuleIdValue,
            new[] { "Validate", "Poll", "Compare", "ProcessChanges", "Complete" }));
        var dispatch = new WorkflowStartDispatchService(definitionResolver, new SqlWorkflowPersistence(db), db);
        var reconcile = new OperationItemRetryReconciliationService(db, items, attempts);
        return new OperationItemRetryService(
            db,
            items,
            attempts,
            reconcile,
            [new ApiPollingOperationItemRetryHandler(), new FileImportOperationItemRetryHandler()],
            new AllowRetryAuthz(),
            dispatch,
            new StubPrincipal());
    }

    private static async Task<(OperationItem item, OperationGroup group)> SeedFailedApiPollingItemAsync(
        InMemoryOperationItemStore items,
        string runId)
    {
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(
            runId,
            ApiPollingModule.ModuleIdValue,
            IntegrationId: null,
            "Sample.ApiPolling.procedures.poll",
            ApiPollingOperationItemization.OperationKind,
            SourceLabel: "snapshot-errors"));

        var item = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId,
            runId,
            Sequence: 4,
            Status: OperationItemStatuses.Failed,
            SubjectKey: "A-610",
            DisplayLabel: "Bad Email Co",
            ErrorCode: "invalid_email",
            ErrorMessage: "Invalid email",
            IsRetryable: true,
            MetadataJson: """{"snapshotId":"snapshot-errors","sequence":4,"includeDeleted":true,"changeKind":"failed","externalId":"A-610","errorCode":"invalid_email"}"""));

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
