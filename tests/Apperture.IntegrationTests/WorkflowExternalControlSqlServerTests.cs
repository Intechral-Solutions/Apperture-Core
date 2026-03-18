using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Integration tests for park/resume external control against a real SQL Server (e.g. Docker on port 1433).
/// Require MSSQL_SA_PASSWORD (from environment or repo-root .env). Tests fail with a clear message when SQL is not configured or reachable.
/// These tests validate the unique-index race path and idempotent handling that in-memory cannot enforce.
/// </summary>
public sealed class WorkflowExternalControlSqlServerTests
{
    [Fact]
    public async Task RequestParkAsync_ConcurrentCalls_OneInsertOneIdempotent_NoException()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
        {
            throw new InvalidOperationException(
                "SQL Server not configured for integration tests. Set MSSQL_SA_PASSWORD (e.g. in repo-root .env) and ensure SQL Server is running on localhost:1433.");
        }

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        await using var provider = services.BuildServiceProvider();

        var runId = "sql-park-" + Guid.NewGuid().ToString("N")[..8];
        var instanceId = "sql-inst-" + runId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var canConnect = await db.Database.CanConnectAsync();
            if (!canConnect)
            {
                throw new InvalidOperationException(
                    "SQL Server not reachable at localhost:1433. Is the server running and is MSSQL_SA_PASSWORD correct?");
            }

            await db.Database.MigrateAsync();
            db.WorkflowInstances.Add(new WorkflowInstanceEntity
            {
                Id = instanceId,
                DefinitionId = "Sample.HelloWorld.HelloWorld",
                Status = WorkflowLifecycleStatusNames.Running,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Version = 1,
                CurrentStepName = "DoWork"
            });
            db.OperationRuns.Add(new OperationRunEntity
            {
                Id = runId,
                WorkflowInstanceId = instanceId,
                DefinitionId = "Sample.HelloWorld.HelloWorld",
                Status = WorkflowLifecycleStatusNames.Running,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            });
            await db.SaveChangesAsync();
        }

        var authz = new AllowAuthz();
        var principal = new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false));

        var results = await Task.WhenAll(
            InvokeParkAsync(provider, runId, authz, principal),
            InvokeParkAsync(provider, runId, authz, principal));

        results[0].Should().NotBeNull();
        results[1].Should().NotBeNull();
        results[0]!.ControlKind.Should().Be(WorkflowExternalControlKindNames.Park);
        results[1]!.ControlKind.Should().Be(WorkflowExternalControlKindNames.Park);
        results[0]!.Status.Should().Be(WorkflowExternalControlStatusNames.Queued);
        results[1]!.Status.Should().Be(WorkflowExternalControlStatusNames.Queued);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var controlCount = await db.WorkflowExternalControlRequests.CountAsync(x => x.RunId == runId);
            var eventCount = await db.WorkflowLifecycleEvents.CountAsync(x => x.RunId == runId && x.EventType == WorkflowLifecycleEventNames.ParkRequested);
            controlCount.Should().Be(1, "exactly one queued park request row (unique index)");
            eventCount.Should().Be(1, "exactly one park_requested lifecycle event");
        }
    }

    private static async Task<WorkflowExternalControlResult?> InvokeParkAsync(
        IServiceProvider provider,
        string runId,
        IWorkflowExternalControlAuthorizationService authz,
        IPrincipalContext principal)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var svc = new WorkflowExternalControlService(db, authz, principal);
        return await svc.RequestParkAsync(new OperationRunId(runId));
    }

    private sealed class AllowAuthz : IWorkflowExternalControlAuthorizationService
    {
        public ValueTask EnsureCanControlRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        private readonly IActor? _actor;

        public StubPrincipalContext(IActor? actor) => _actor = actor;

        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_actor);
    }
}
