using System;
using System.Globalization;
using Apperture.Core.Application.ControlFacade;
using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Workflow;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Trust;
using Apperture.ModuleSdk.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class UnifiedControlFacadeTests
{
    [Theory]
    [InlineData("schedule:sc-1", true, "sc-1")]
    [InlineData("foo schedule:sc-1 bar", true, "sc-1")]
    [InlineData("foo,schedule:sc-1;bar", true, "sc-1")]
    [InlineData("(schedule:sc-1)", true, "sc-1")]
    [InlineData("key=schedule:sc-1", true, "sc-1")]
    [InlineData("myschedule:sc-1", false, null)]
    [InlineData("schedules:sc-1", false, null)]
    [InlineData("schedule:", false, null)]
    [InlineData("", false, null)]
    [InlineData(null, false, null)]
    [InlineData("Schedule:sc-1", false, null)]
    public void TryParseScheduleId_ProtectsAgainstEdgeCases(string? correlationKey, bool expectedOk, string? expectedScheduleId)
    {
        var ok = ControlFacade.TryParseScheduleId(correlationKey, out var id);
        ok.Should().Be(expectedOk);
        if (expectedOk)
            id.Should().Be(expectedScheduleId);
    }

    [Theory]
    [InlineData("requested", ControlFacade.NormalizedRequested)]
    [InlineData("running", ControlFacade.NormalizedRunning)]
    [InlineData("parked", ControlFacade.NormalizedParked)]
    [InlineData("completed", ControlFacade.NormalizedSucceeded)]
    [InlineData("failed", ControlFacade.NormalizedFailed)]
    [InlineData("cancelled", ControlFacade.NormalizedCancelled)]
    public void NormalizeRunStatus_MapsWorkflowStates(string raw, string expected)
    {
        ControlFacade.NormalizeRunStatus(raw).Should().Be(expected);
    }

    [Fact]
    public async Task ActionProjection_ResumeNotEligible_WhenNotParked()
    {
        var projection = NewProjection(allowCancel: true, allowExternalControl: true, allowRetry: true);

        var run = new RunSummaryDto(
            RunId: "r1",
            ExecutionInstanceId: "i1",
            ExecutionDefinitionId: "wf-1",
            Status: WorkflowLifecycleStatusNames.Running,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow");

        var actions = await projection.ProjectExecutionRunActionsAsync(run, hasQueuedPark: false, hasQueuedResume: false);

        var resume = actions.Single(a => a.ActionKind == ControlFacade.ActionKindResume);
        resume.AvailabilityState.Should().Be(ControlFacade.ActionAvailabilityNotEligible);
        resume.ReasonCode.Should().Be(ControlFacade.ReasonStateNotEligible);
    }

    [Fact]
    public async Task ActionProjection_ResumeAlreadyQueued_WhenQueued()
    {
        var projection = NewProjection(allowCancel: true, allowExternalControl: false, allowRetry: true);

        var run = new RunSummaryDto(
            RunId: "r1",
            ExecutionInstanceId: "i1",
            ExecutionDefinitionId: "wf-1",
            Status: WorkflowLifecycleStatusNames.Parked,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow");

        var actions = await projection.ProjectExecutionRunActionsAsync(run, hasQueuedPark: false, hasQueuedResume: true);

        var resume = actions.Single(a => a.ActionKind == ControlFacade.ActionKindResume);
        resume.AvailabilityState.Should().Be(ControlFacade.ActionAvailabilityAlreadyQueued);
        resume.ReasonCode.Should().Be(ControlFacade.ReasonAlreadyQueued);
    }

    [Fact]
    public async Task ActionProjection_CancelNotEligible_WhenTerminal()
    {
        var projection = NewProjection(allowCancel: true, allowExternalControl: true, allowRetry: true);

        var run = new RunSummaryDto(
            RunId: "r1",
            ExecutionInstanceId: "i1",
            ExecutionDefinitionId: "wf-1",
            Status: WorkflowLifecycleStatusNames.Completed,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow");

        var actions = await projection.ProjectExecutionRunActionsAsync(run, hasQueuedPark: false, hasQueuedResume: false);

        var cancel = actions.Single(a => a.ActionKind == ControlFacade.ActionKindCancel);
        cancel.AvailabilityState.Should().Be(ControlFacade.ActionAvailabilityNotEligible);
        cancel.ReasonCode.Should().Be(ControlFacade.ReasonStateNotEligible);
    }

    [Fact]
    public async Task ActionProjection_CancelAvailable_WhenRunning()
    {
        var projection = NewProjection(allowCancel: true, allowExternalControl: true, allowRetry: true);

        var run = new RunSummaryDto(
            RunId: "r1",
            ExecutionInstanceId: "i1",
            ExecutionDefinitionId: "wf-1",
            Status: WorkflowLifecycleStatusNames.Running,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow");

        var actions = await projection.ProjectExecutionRunActionsAsync(run, hasQueuedPark: false, hasQueuedResume: false);
        var cancel = actions.Single(a => a.ActionKind == ControlFacade.ActionKindCancel);
        cancel.AvailabilityState.Should().Be(ControlFacade.ActionAvailabilityAvailable);
        cancel.ReasonCode.Should().BeNull();
    }

    [Fact]
    public async Task ActionProjection_ParkNotEligible_WhenTerminal()
    {
        var projection = NewProjection(allowCancel: true, allowExternalControl: true, allowRetry: true);

        var run = new RunSummaryDto(
            RunId: "r1",
            ExecutionInstanceId: "i1",
            ExecutionDefinitionId: "wf-1",
            Status: WorkflowLifecycleStatusNames.Completed,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow");

        var actions = await projection.ProjectExecutionRunActionsAsync(run, hasQueuedPark: false, hasQueuedResume: false);
        var park = actions.Single(a => a.ActionKind == ControlFacade.ActionKindPark);
        park.AvailabilityState.Should().Be(ControlFacade.ActionAvailabilityNotEligible);
        park.ReasonCode.Should().Be(ControlFacade.ReasonStateNotEligible);
    }

    [Fact]
    public async Task ActionProjection_ParkAlreadyQueued_WhenQueued()
    {
        var projection = NewProjection(allowCancel: true, allowExternalControl: false, allowRetry: true);

        var run = new RunSummaryDto(
            RunId: "r1",
            ExecutionInstanceId: "i1",
            ExecutionDefinitionId: "wf-1",
            Status: WorkflowLifecycleStatusNames.Running,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow");

        var actions = await projection.ProjectExecutionRunActionsAsync(run, hasQueuedPark: true, hasQueuedResume: false);
        var park = actions.Single(a => a.ActionKind == ControlFacade.ActionKindPark);
        park.AvailabilityState.Should().Be(ControlFacade.ActionAvailabilityAlreadyQueued);
        park.ReasonCode.Should().Be(ControlFacade.ReasonAlreadyQueued);
    }

    [Fact]
    public async Task ActionProjection_ParkAvailable_WhenEligible()
    {
        var projection = NewProjection(allowCancel: true, allowExternalControl: true, allowRetry: false);

        var run = new RunSummaryDto(
            RunId: "r1",
            ExecutionInstanceId: "i1",
            ExecutionDefinitionId: "wf-1",
            Status: WorkflowLifecycleStatusNames.Running,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow");

        var actions = await projection.ProjectExecutionRunActionsAsync(run, hasQueuedPark: false, hasQueuedResume: false);
        var park = actions.Single(a => a.ActionKind == ControlFacade.ActionKindPark);
        park.AvailabilityState.Should().Be(ControlFacade.ActionAvailabilityAvailable);
        park.ReasonCode.Should().BeNull();
    }

    [Fact]
    public async Task ActionProjection_ResumeAvailable_WhenParkedAndNotQueued()
    {
        var projection = NewProjection(allowCancel: true, allowExternalControl: true, allowRetry: false);

        var run = new RunSummaryDto(
            RunId: "r1",
            ExecutionInstanceId: "i1",
            ExecutionDefinitionId: "wf-1",
            Status: WorkflowLifecycleStatusNames.Parked,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow");

        var actions = await projection.ProjectExecutionRunActionsAsync(run, hasQueuedPark: false, hasQueuedResume: false);
        var resume = actions.Single(a => a.ActionKind == ControlFacade.ActionKindResume);
        resume.AvailabilityState.Should().Be(ControlFacade.ActionAvailabilityAvailable);
        resume.ReasonCode.Should().BeNull();
    }

    [Fact]
    public async Task ActionProjection_RetryAvailable_WhenFailedAndAuthorized()
    {
        var projection = NewProjection(allowCancel: true, allowExternalControl: true, allowRetry: true);

        var run = new RunSummaryDto(
            RunId: "r1",
            ExecutionInstanceId: "i1",
            ExecutionDefinitionId: "wf-1",
            Status: WorkflowLifecycleStatusNames.Failed,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow");

        var actions = await projection.ProjectExecutionRunActionsAsync(run, hasQueuedPark: false, hasQueuedResume: false);
        var retry = actions.Single(a => a.ActionKind == ControlFacade.ActionKindRetry);
        retry.AvailabilityState.Should().Be(ControlFacade.ActionAvailabilityAvailable);
        retry.ReasonCode.Should().BeNull();
    }

    [Fact]
    public async Task ControlActivityQuery_SetsScheduleTarget_WhenCorrelationHasScheduleToken()
    {
        var db = NewDb();
        var runQuery = new StubRunQueryService(new[]
        {
            new RunSummaryDto(
                RunId: "run-1",
                ExecutionInstanceId: "i1",
                ExecutionDefinitionId: "wf-1",
                Status: WorkflowLifecycleStatusNames.Failed,
                CreatedAt: new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero),
                CompletedAt: new DateTimeOffset(2026, 3, 20, 10, 1, 0, TimeSpan.Zero),
                CorrelationKey: "schedule:sc-1",
                RetryOfRunId: null,
                ExecutionKind: "workflow")
        });

        var service = new ControlActivityQueryService(
            runs: runQuery,
            runtimeIdentity: new StubRuntimeIdentityService(Array.Empty<Apperture.Core.Security.Trust.RuntimeIdentity>()),
            runtimeControl: new StubRuntimeControlService(),
            actions: new StubExecutionActionProjection(),
            authorization: new AllowAuthorizationEvaluationService(),
            principal: new StubPrincipal(),
            environment: new StubEnvironment("env-1"),
            db: db);

        var items = await service.ListRecentActivityAsync(createdFromUtc: DateTimeOffset.UtcNow.AddHours(-1), take: 5);
        items.Should().NotBeEmpty();

        var item = items.Single();
        item.ActivitySource.Should().Be(ControlFacade.ActivitySourceScheduled);
        item.TargetKind.Should().Be(ControlFacade.TargetKindSchedule);
        item.TargetId.Should().Be("sc-1");
        item.CurrentNormalizedStatus.Should().Be(ControlFacade.NormalizedFailed);
        item.Outcome.Should().Be("failed");
    }

    [Theory]
    [InlineData(RuntimeControlStatusNames.Queued, ControlFacade.NormalizedRequested, "in_progress", "Runtime shutdown request is queued.")]
    [InlineData(RuntimeControlStatusNames.Applied, ControlFacade.NormalizedSucceeded, "succeeded", "Runtime shutdown applied.")]
    [InlineData(RuntimeControlStatusNames.Rejected, ControlFacade.NormalizedFailed, "failed", "Runtime shutdown request failed.")]
    [InlineData(RuntimeControlStatusNames.Failed, ControlFacade.NormalizedFailed, "failed", "Runtime shutdown request failed.")]
    public async Task ControlActivityQuery_RuntimeShutdownNormalization_MapsStatuses(
        string runtimeControlStatus,
        string expectedNormalizedStatus,
        string expectedOutcome,
        string expectedOutcomeSummary)
    {
        var runtimeId = "rt-1";
        var runtime = new Apperture.Core.Security.Trust.RuntimeIdentity(
            Id: runtimeId,
            DisplayName: "Runtime 1",
            CredentialType: "Test",
            PublicKeyOrThumbprint: null,
            ManagedIdentityPrincipalId: null,
            DeclaredScopes: Array.Empty<string>(),
            RegisteredAt: DateTimeOffset.UtcNow.AddHours(-1));

        var request = new Apperture.ControlPlane.Security.RuntimeControlRequestResult(
            Id: 42,
            RuntimeId: runtimeId,
            ControlKind: RuntimeControlKindNames.Shutdown,
            Status: runtimeControlStatus,
            RequestedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            RequestedBy: "operator",
            AppliedAt: runtimeControlStatus == RuntimeControlStatusNames.Queued ? null : DateTimeOffset.UtcNow.AddMinutes(-5),
            AppliedBy: runtimeControlStatus == RuntimeControlStatusNames.Queued ? null : "worker",
            LastError: runtimeControlStatus == RuntimeControlStatusNames.Failed ? "boom" : null);

        var db = NewDb();
        var service = new ControlActivityQueryService(
            runs: new StubRunQueryService(Array.Empty<RunSummaryDto>()),
            runtimeIdentity: new StubRuntimeIdentityService(new[] { runtime }),
            runtimeControl: new StubRuntimeControlServiceWithRequests(new[] { request }),
            actions: new StubExecutionActionProjection(),
            authorization: new AllowAuthorizationEvaluationService(),
            principal: new StubPrincipal(),
            environment: new StubEnvironment("env-1"),
            db: db);

        var items = await service.ListRecentActivityAsync(createdFromUtc: DateTimeOffset.UtcNow.AddHours(-1), take: 10);
        items.Should().NotBeEmpty();

        var item = items.Single();
        item.TargetKind.Should().Be(ControlFacade.TargetKindRuntimeInstance);
        item.TargetId.Should().Be(runtimeId);
        item.CurrentNormalizedStatus.Should().Be(expectedNormalizedStatus);
        item.Outcome.Should().Be(expectedOutcome);
        item.OutcomeSummary.Should().Be(expectedOutcomeSummary);
    }

    [Fact]
    public async Task ControlAttentionQuery_UsesScheduleTarget_ForFailedExecution()
    {
        var db = NewDb();
        var failedRuns = new[]
        {
            new RunSummaryDto(
                RunId: "run-1",
                ExecutionInstanceId: "i1",
                ExecutionDefinitionId: "wf-1",
                Status: WorkflowLifecycleStatusNames.Failed,
                CreatedAt: new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero),
                CompletedAt: null,
                CorrelationKey: "schedule:sc-1",
                RetryOfRunId: null,
                ExecutionKind: "workflow")
        };

        var service = new ControlAttentionQueryService(
            runs: new StubRunQueryService(failedRuns),
            authorization: new AllowAuthorizationEvaluationService(),
            principal: new StubPrincipal(),
            environment: new StubEnvironment("env-1"),
            db: db,
            workflowRunReadAuthz: new AllowWorkflowReadAuthz());

        var items = await service.ListAttentionAsync(createdFromUtc: DateTimeOffset.UtcNow.AddHours(-1), take: 10);
        items.Should().ContainSingle();

        var item = items.Single();
        item.AttentionKind.Should().Be("executionFailed");
        item.TargetKind.Should().Be(ControlFacade.TargetKindSchedule);
        item.TargetId.Should().Be("sc-1");
    }

    [Fact]
    public async Task ControlAttentionQuery_WorkflowControlRejected_UsesRunId_ForDrilldown()
    {
        var db = NewDb();

        db.WorkflowExternalControlRequests.Add(new WorkflowExternalControlRequestEntity
        {
            Id = 1,
            RunId = "run-99",
            WorkflowInstanceId = "i-wf-1",
            WorkflowDefinitionId = "wf-1",
            ControlKind = WorkflowExternalControlKindNames.Park,
            Status = WorkflowExternalControlStatusNames.Rejected,
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            RequestedBy = "operator",
            AppliedAt = null,
            AppliedBy = null,
            LastError = "rejected"
        });
        await db.SaveChangesAsync();

        var service = new ControlAttentionQueryService(
            runs: new StubRunQueryService(Array.Empty<RunSummaryDto>()),
            authorization: new AllowAuthorizationEvaluationService(),
            principal: new StubPrincipal(),
            environment: new StubEnvironment("env-1"),
            db: db,
            workflowRunReadAuthz: new AllowWorkflowReadAuthz());

        var items = await service.ListAttentionAsync(createdFromUtc: DateTimeOffset.UtcNow.AddHours(-1), take: 10);

        var rejected = items.Single(i => i.AttentionKind == "workflowControlRejected");
        rejected.TargetKind.Should().Be(ControlFacade.TargetKindWorkflow);
        rejected.TargetId.Should().Be("wf-1");
        // Drill-down: open run detail page.
        rejected.RelatedActivityId.Should().Be("run-99");
    }

    [Fact]
    public async Task ControlAttentionQuery_WorkflowControlRejected_SkipsUnauthorizedDefinitions()
    {
        var db = NewDb();

        db.WorkflowExternalControlRequests.Add(new WorkflowExternalControlRequestEntity
        {
            Id = 1,
            RunId = "run-99",
            WorkflowInstanceId = "i-wf-1",
            WorkflowDefinitionId = "wf-1",
            ControlKind = WorkflowExternalControlKindNames.Park,
            Status = WorkflowExternalControlStatusNames.Rejected,
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            RequestedBy = "operator",
            AppliedAt = null,
            AppliedBy = null,
            LastError = "rejected"
        });
        await db.SaveChangesAsync();

        var service = new ControlAttentionQueryService(
            runs: new StubRunQueryService(Array.Empty<RunSummaryDto>()),
            authorization: new AllowAuthorizationEvaluationService(),
            principal: new StubPrincipal(),
            environment: new StubEnvironment("env-1"),
            db: db,
            workflowRunReadAuthz: new DenyWorkflowReadAuthz());

        var items = await service.ListAttentionAsync(createdFromUtc: DateTimeOffset.UtcNow.AddHours(-1), take: 10);
        items.Any(i => i.AttentionKind == "workflowControlRejected").Should().BeFalse();
    }

    [Fact]
    public async Task ControlCenterQueryService_EmitsTargetDetailLinks_AndOmitsScheduleTargets()
    {
        var operation = new Apperture.ControlPlane.Security.OperationDefinitionDto(
            OperationDefinitionId: "op-1",
            ModuleId: "mod-1",
            InvariantName: "op.invariant",
            DisplayName: "Op 1",
            Description: "desc",
            Category: "Command",
            CanStart: true,
            InputSchema: null);

        var module = new Apperture.ModuleSdk.Abstractions.ModuleManifest(
            Id: "mod-1",
            Name: "Module 1",
            Version: "1.0.0",
            Description: "Module");

        var workflow = new Apperture.RuntimePlane.Abstractions.WorkflowDefinition(
            Id: new Apperture.RuntimePlane.Abstractions.WorkflowDefinitionId("wf-1"),
            Name: "Workflow 1",
            ModuleId: "mod-1",
            StepNames: Array.Empty<string>(),
            Category: Apperture.RuntimePlane.Abstractions.WorkflowCategory.Command,
            RequiredPermissions: null);

        var runtime = new Apperture.Core.Security.Trust.RuntimeIdentity(
            Id: "rt-1",
            DisplayName: "Runtime 1",
            CredentialType: "Test",
            PublicKeyOrThumbprint: null,
            ManagedIdentityPrincipalId: null,
            DeclaredScopes: Array.Empty<string>(),
            RegisteredAt: DateTimeOffset.UtcNow.AddHours(-1));

        var service = new ControlCenterQueryService(
            operations: new StubOperationDefinitionQueryService(new[] { operation }),
            workflowListing: new StubModuleWorkflowListingService(new[] { module }, new Dictionary<string, IReadOnlyList<Apperture.RuntimePlane.Abstractions.WorkflowDefinition>>
            {
                ["mod-1"] = new[] { workflow }
            }),
            runtimeIdentity: new StubRuntimeIdentityService(new[] { runtime }),
            authorization: new AllowAuthorizationEvaluationService(),
            principal: new StubPrincipal(),
            environment: new StubEnvironment("env-1"),
            activity: new StubControlActivityQueryService(),
            attention: new StubControlAttentionQueryService(),
            actions: new StubExecutionActionProjection());

        var dto = await service.GetControlCenterAsync(DateTimeOffset.UtcNow.AddHours(-1), takeRecentActivity: 5, takeAttention: 5);

        dto.Targets.Any(t => t.TargetKind == ControlFacade.TargetKindSchedule).Should().BeFalse();

        var opTarget = dto.Targets.Single(t => t.TargetKind == ControlFacade.TargetKindOperation);
        opTarget.DetailLink.Should().Be($"/operations/start?operationDefinitionId={Uri.EscapeDataString(operation.OperationDefinitionId)}");

        var wfTarget = dto.Targets.Single(t => t.TargetKind == ControlFacade.TargetKindWorkflow);
        wfTarget.DetailLink.Should().Be($"/workflows/start?moduleId={Uri.EscapeDataString(workflow.ModuleId)}&workflowId={Uri.EscapeDataString(workflow.Id.Value)}");

        var rtTarget = dto.Targets.Single(t => t.TargetKind == ControlFacade.TargetKindRuntimeInstance);
        rtTarget.DetailLink.Should().Be($"/runtimes/{Uri.EscapeDataString(runtime.Id)}/controls/shutdown/requests");
    }

    [Fact]
    public async Task ControlCenterQueryService_RespectsRuntimeReadAuthorization()
    {
        var runtime = new Apperture.Core.Security.Trust.RuntimeIdentity(
            Id: "rt-1",
            DisplayName: "Runtime 1",
            CredentialType: "Test",
            PublicKeyOrThumbprint: null,
            ManagedIdentityPrincipalId: null,
            DeclaredScopes: Array.Empty<string>(),
            RegisteredAt: DateTimeOffset.UtcNow.AddHours(-1));

        var service = new ControlCenterQueryService(
            operations: new StubOperationDefinitionQueryService(Array.Empty<Apperture.ControlPlane.Security.OperationDefinitionDto>()),
            workflowListing: new StubModuleWorkflowListingService(
                modules: Array.Empty<Apperture.ModuleSdk.Abstractions.ModuleManifest>(),
                workflowsByModule: new Dictionary<string, IReadOnlyList<Apperture.RuntimePlane.Abstractions.WorkflowDefinition>>()),
            runtimeIdentity: new StubRuntimeIdentityService(new[] { runtime }),
            authorization: new DenyAuthorizationEvaluationService(),
            principal: new StubPrincipal(),
            environment: new StubEnvironment("env-1"),
            activity: new StubControlActivityQueryService(),
            attention: new StubControlAttentionQueryService(),
            actions: new StubExecutionActionProjection());

        var dto = await service.GetControlCenterAsync(DateTimeOffset.UtcNow.AddHours(-1), takeRecentActivity: 5, takeAttention: 5);
        dto.Targets.Any(t => t.TargetKind == ControlFacade.TargetKindRuntimeInstance).Should().BeFalse();
    }

    private static Apperture.ControlPlane.Security.ControlActionAvailabilityProjection NewProjection(bool allowCancel, bool allowExternalControl, bool allowRetry)
    {
        return new Apperture.ControlPlane.Security.ControlActionAvailabilityProjection(
            principal: new StubPrincipal(),
            authorization: new AllowAuthorizationEvaluationService(),
            environment: new StubEnvironment("env-1"),
            operationCancelAuthz: new StubOperationRunCancelAuthz(allowCancel),
            workflowCancelAuthz: new StubWorkflowRunCancelAuthz(allowCancel),
            workflowRetryAuthz: new StubWorkflowRunRetryAuthz(allowRetry),
            workflowExternalControlAuthz: new StubWorkflowExternalControlAuthz(allowExternalControl),
            workflowStartAuthz: new StubWorkflowStartAuthz(allow: true));
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private sealed class StubExecutionActionProjection : IControlActionAvailabilityProjection
    {
        public ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectExecutionRunActionsAsync(
            RunSummaryDto run,
            bool hasQueuedPark,
            bool hasQueuedResume,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlActionAvailabilityDto>>(Array.Empty<ControlActionAvailabilityDto>());

        public ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectOperationDefinitionActionsAsync(
            OperationDefinitionDto operation,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlActionAvailabilityDto>>(Array.Empty<ControlActionAvailabilityDto>());

        public ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectWorkflowDefinitionActionsAsync(
            WorkflowDefinition workflow,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlActionAvailabilityDto>>(Array.Empty<ControlActionAvailabilityDto>());

        public ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectRuntimeInstanceActionsAsync(
            Apperture.Core.Security.Trust.RuntimeIdentity runtime,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlActionAvailabilityDto>>(Array.Empty<ControlActionAvailabilityDto>());
    }

    private sealed class StubRunQueryService : IRunQueryService
    {
        private readonly IReadOnlyList<RunSummaryDto> _runs;

        public StubRunQueryService(IReadOnlyList<RunSummaryDto> runs) => _runs = runs;

        public ValueTask<IReadOnlyList<RunSummaryDto>> ListRunsAsync(RunQueryFilter? filter = null, CancellationToken cancellationToken = default)
        {
            if (filter?.Status is null)
                return ValueTask.FromResult(_runs);

            var wanted = filter.Status;
            var list = _runs.Where(r => string.Equals(r.Status, wanted, StringComparison.Ordinal)).ToList();
            return ValueTask.FromResult<IReadOnlyList<RunSummaryDto>>(list);
        }

        public ValueTask<RunDetailDto?> GetRunAsync(OperationRunId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RunDetailDto?>(null);
    }

    private sealed class StubRuntimeControlService : IRuntimeControlService
    {
        public ValueTask<RuntimeControlRequestResult?> RequestShutdownAsync(string runtimeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimeControlRequestResult?>(null);

        public ValueTask<IReadOnlyList<RuntimeControlRequestResult>> GetShutdownRequestsAsync(string runtimeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<RuntimeControlRequestResult>>(Array.Empty<RuntimeControlRequestResult>());
    }

    private sealed class StubRuntimeControlServiceWithRequests : IRuntimeControlService
    {
        private readonly IReadOnlyList<RuntimeControlRequestResult> _requests;

        public StubRuntimeControlServiceWithRequests(IReadOnlyList<RuntimeControlRequestResult> requests) => _requests = requests;

        public ValueTask<RuntimeControlRequestResult?> RequestShutdownAsync(string runtimeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimeControlRequestResult?>(null);

        public ValueTask<IReadOnlyList<RuntimeControlRequestResult>> GetShutdownRequestsAsync(string runtimeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<RuntimeControlRequestResult>>(_requests);
    }

    private sealed class StubRuntimeIdentityService : IRuntimeIdentityService
    {
        private readonly IReadOnlyList<Apperture.Core.Security.Trust.RuntimeIdentity> _runtimes;

        public StubRuntimeIdentityService(IReadOnlyList<Apperture.Core.Security.Trust.RuntimeIdentity> runtimes) => _runtimes = runtimes;

        public ValueTask<Apperture.Core.Security.Trust.RuntimeIdentity?> GetByIdAsync(string runtimeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Apperture.Core.Security.Trust.RuntimeIdentity?>(null);

        public ValueTask<IReadOnlyList<Apperture.Core.Security.Trust.RuntimeIdentity>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_runtimes);

        public ValueTask<Apperture.Core.Security.Trust.RuntimeTrustValidationResult> ValidateAsync(string runtimeId, string presentedCredential, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<Apperture.Core.Security.Trust.EnrollmentResult> RequestCredentialRolloverAsync(Apperture.Core.Security.Trust.RuntimeCredentialRolloverRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class AllowAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class DenyAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    private sealed class StubPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new StubActor());
    }

    private sealed class StubActor : IActor
    {
        public ActorId Id => new("actor-1");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Test";
    }

    private sealed class StubEnvironment : IEnvironmentContext
    {
        public StubEnvironment(string environmentId) => EnvironmentId = environmentId;
        public string EnvironmentId { get; }
    }

    private sealed class AllowWorkflowReadAuthz : IWorkflowRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class DenyWorkflowReadAuthz : IWorkflowRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException();
    }

    private sealed class StubOperationRunCancelAuthz : IOperationRunCancelAuthorizationService
    {
        private readonly bool _allow;

        public StubOperationRunCancelAuthz(bool allow) => _allow = allow;

        public ValueTask EnsureCanCancelRunAsync(OperationId operationId, CancellationToken cancellationToken = default)
        {
            if (!_allow) throw new UnauthorizedAccessException();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubWorkflowRunCancelAuthz : IWorkflowRunCancelAuthorizationService
    {
        private readonly bool _allow;

        public StubWorkflowRunCancelAuthz(bool allow) => _allow = allow;

        public ValueTask EnsureCanCancelRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            if (!_allow) throw new UnauthorizedAccessException();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubWorkflowRunRetryAuthz : IWorkflowRunRetryAuthorizationService
    {
        private readonly bool _allow;

        public StubWorkflowRunRetryAuthz(bool allow) => _allow = allow;

        public ValueTask EnsureCanRetryRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            if (!_allow) throw new UnauthorizedAccessException();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubWorkflowExternalControlAuthz : IWorkflowExternalControlAuthorizationService
    {
        private readonly bool _allow;
        public StubWorkflowExternalControlAuthz(bool allow) => _allow = allow;

        public ValueTask EnsureCanControlRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            if (!_allow) throw new UnauthorizedAccessException();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubWorkflowStartAuthz : IWorkflowStartAuthorizationService
    {
        private readonly bool _allow;
        public StubWorkflowStartAuthz(bool allow) => _allow = allow;

        public ValueTask EnsureCanStartWorkflowAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            if (!_allow) throw new UnauthorizedAccessException();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubOperationDefinitionQueryService : IOperationDefinitionQueryService
    {
        private readonly IReadOnlyList<OperationDefinitionDto> _operations;

        public StubOperationDefinitionQueryService(IReadOnlyList<OperationDefinitionDto> operations) => _operations = operations;

        public ValueTask<IReadOnlyList<OperationDefinitionDto>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_operations);

        public ValueTask<OperationDefinitionDto?> GetAsync(OperationId operationDefinitionId, CancellationToken cancellationToken = default)
        {
            var match = _operations.FirstOrDefault(x => string.Equals(x.OperationDefinitionId, operationDefinitionId.Value, StringComparison.Ordinal));
            return ValueTask.FromResult<OperationDefinitionDto?>(match);
        }
    }

    private sealed class StubModuleWorkflowListingService : IModuleWorkflowListingService
    {
        private readonly IReadOnlyList<ModuleCatalogEntry> _modules;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<Apperture.RuntimePlane.Abstractions.WorkflowDefinition>> _workflowsByModule;

        public StubModuleWorkflowListingService(
            IReadOnlyList<Apperture.ModuleSdk.Abstractions.ModuleManifest> modules,
            IReadOnlyDictionary<string, IReadOnlyList<Apperture.RuntimePlane.Abstractions.WorkflowDefinition>> workflowsByModule)
        {
            _modules = modules
                .Select(m => new ModuleCatalogEntry(
                    new ModuleDescriptor(m.Id, m.Name, m.Version, m.Description, null, null, []),
                    ModuleLifecycleState.InstalledEnabled,
                    Installed: true,
                    Enabled: true,
                    EffectivelyActive: true,
                    ValidationIssues: []))
                .ToList();
            _workflowsByModule = workflowsByModule;
        }

        public ValueTask<IReadOnlyList<ModuleCatalogEntry>> GetModulesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_modules);

        public ValueTask<ModuleCatalogEntry?> GetModuleAsync(string moduleId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ModuleCatalogEntry?>(_modules.FirstOrDefault(x => x.Descriptor.ModuleId == moduleId));

        public ValueTask<IReadOnlyList<Apperture.RuntimePlane.Abstractions.WorkflowDefinition>> GetWorkflowsForModuleAsync(string moduleId, CancellationToken cancellationToken = default)
        {
            _workflowsByModule.TryGetValue(moduleId, out var workflows);
            return ValueTask.FromResult<IReadOnlyList<Apperture.RuntimePlane.Abstractions.WorkflowDefinition>>(workflows ?? Array.Empty<Apperture.RuntimePlane.Abstractions.WorkflowDefinition>());
        }

        public ValueTask<ModuleLifecycleTransitionResult> InstallModuleAsync(string moduleId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ModuleLifecycleTransitionResult(true, moduleId, "install", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate, []));

        public ValueTask<ModuleLifecycleTransitionResult> EnableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ModuleLifecycleTransitionResult(true, moduleId, "enable", ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart, []));

        public ValueTask<ModuleLifecycleTransitionResult> DisableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ModuleLifecycleTransitionResult(true, moduleId, "disable", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.PartialUntilRestart, []));

        public ValueTask<ModuleLifecycleTransitionResult> UninstallModuleAsync(string moduleId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ModuleLifecycleTransitionResult(true, moduleId, "uninstall", ModuleLifecycleState.Discovered, ModuleLifecycleEffect.RequiresRestart, []));
    }

    private sealed class StubControlActivityQueryService : IControlActivityQueryService
    {
        public ValueTask<IReadOnlyList<ControlActivitySummaryDto>> ListRecentActivityAsync(DateTimeOffset createdFromUtc, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlActivitySummaryDto>>(Array.Empty<ControlActivitySummaryDto>());
    }

    private sealed class StubControlAttentionQueryService : IControlAttentionQueryService
    {
        public ValueTask<IReadOnlyList<ControlAttentionItemDto>> ListAttentionAsync(DateTimeOffset createdFromUtc, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlAttentionItemDto>>(Array.Empty<ControlAttentionItemDto>());
    }
}

