using Apperture.ControlPlane.Security;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Workflow;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Proves the Runs "blocked" status facet against a real SQL Server, not just generated SQL
/// text. <c>[</c> is a SQL Server <c>LIKE</c> metacharacter and <c>RunFailureCapture.
/// DeferredWorkBlockedPrefix</c> ("[Blocked] ") starts with one, so EF Core's translation of
/// <c>FailureSummary.StartsWith(...)</c> into <c>LIKE ... ESCAPE '\'</c> must actually work
/// against the real provider for the derived facet in <see cref="RunQueryService"/> to be sound.
/// NAV-1 stabilization (independent-review finding F-07).
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class RunsBlockedFacetSqlServerTests
{
    [Fact]
    public async Task ListRunsAsync_StatusBlocked_MatchesOnlyTheBlockedRun_AgainstRealSqlServer()
    {
        var connectionString = SqlServerTestConfiguration.RequireConnectionString();

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        await using var provider = services.BuildServiceProvider();

        var blockedRunId = "rb-" + Guid.NewGuid().ToString("N")[..8];
        var plainFailedRunId = "rf-" + Guid.NewGuid().ToString("N")[..8];
        var completedRunId = "rc-" + Guid.NewGuid().ToString("N")[..8];

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
            {
                throw new InvalidOperationException(
                    SqlServerTestConfiguration.DescribeConnectionFailure());
            }

            await AppertureDatabaseMigrator.MigrateAsync(db);

            foreach (var (runId, status, failureSummary) in new[]
            {
                (blockedRunId, WorkflowLifecycleStatusNames.Failed, RunFailureCapture.DeferredWorkBlockedPrefix + "Workflow definition is not effectively active"),
                (plainFailedRunId, WorkflowLifecycleStatusNames.Failed, "Timed out calling downstream API."),
                (completedRunId, WorkflowLifecycleStatusNames.Completed, (string?)null)
            })
            {
                var instanceId = "wi-" + runId;
                db.WorkflowInstances.Add(new WorkflowInstanceEntity
                {
                    Id = instanceId,
                    DefinitionId = "Sample.HelloWorld.HelloWorld",
                    Status = status,
                    CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                    UpdatedAt = DateTimeOffset.UtcNow,
                    CorrelationKey = null,
                    CurrentStepName = null,
                    Version = 0
                });
                db.OperationRuns.Add(new OperationRunEntity
                {
                    Id = runId,
                    WorkflowInstanceId = instanceId,
                    DefinitionId = "Sample.HelloWorld.HelloWorld",
                    Status = status,
                    CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                    CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                    FailureSummary = failureSummary
                });
                db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
                {
                    RunId = runId,
                    WorkflowInstanceId = instanceId,
                    WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                    Status = WorkflowLifecycleStatusNames.Requested,
                    CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
                });
            }

            await db.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var service = new RunQueryService(
                db,
                new AllowAllOperationRegistry(),
                new RunKindResolver(db),
                new AllowAllWorkflowReadAuthz(),
                new AllowAllOperationReadAuthz(),
                new StaticWorkflowDefinitionResolver(),
                new EmptyCompositionSnapshot());

            // "blocked" is the derived Runs facet token (RunQueryService.ListRunsAsync), not a
            // persisted lifecycle status.
            var result = await service.ListRunsAsync(new RunQueryFilter(Status: "blocked"));

            result.Should().ContainSingle(x => x.RunId == blockedRunId);
        }
    }

    private sealed class StaticWorkflowDefinitionResolver : IWorkflowDefinitionResolver
    {
        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowDefinition?>(new WorkflowDefinition(id, "test", "Sample.HelloWorld", ["step"]));
    }

    private sealed class EmptyCompositionSnapshot : IRuntimeModuleCompositionQueryService
    {
        public ValueTask<HostEffectiveModuleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HostEffectiveModuleSnapshot(
                DateTimeOffset.UtcNow,
                [
                    new RuntimeCompositionModuleEntry(
                        "Sample.HelloWorld", "InstalledEnabled", true, true, true, [], true, false, null)
                ],
                ["Sample.HelloWorld"],
                [],
                [],
                []));
    }

    private sealed class AllowAllWorkflowReadAuthz : IWorkflowRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class AllowAllOperationReadAuthz : IOperationRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class AllowAllOperationRegistry : IOperationRegistryService
    {
        public ValueTask<IReadOnlyList<OperationDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(Array.Empty<OperationDefinition>());

        public ValueTask<OperationDefinition?> GetOperationAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<OperationDefinition?>(new OperationDefinition(operationId, operationId.Value, "Sample.HelloWorld", OperationCategory.Command, null, Array.Empty<Apperture.Shared.Kernel.PermissionDescriptor>()));

        public ValueTask<IReadOnlyList<OperationDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(Array.Empty<OperationDefinition>());

        public ValueTask RegisterAsync(OperationDefinition definition, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
