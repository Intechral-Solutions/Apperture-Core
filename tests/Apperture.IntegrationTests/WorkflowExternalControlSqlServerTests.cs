using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Integration tests for park/resume external control against a real SQL Server.
/// Configure SQL via <see cref="SqlServerTestConfiguration"/> (see tests/README.md).
/// Tests fail with a clear message when SQL is not configured or reachable.
/// These tests validate the unique-index race path and idempotent handling that in-memory cannot enforce.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class WorkflowExternalControlSqlServerTests
{
    [Fact]
    public async Task RequestParkAsync_ConcurrentCalls_OneInsertOneIdempotent_NoException()
    {
        var connectionString = SqlServerTestConfiguration.RequireConnectionString();

        // The acting user must exist: the recorder reads the actor's username in the transaction it records the row in.
        var principal = new MutablePrincipalContext();

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddAppertureAuditRecorder();
        services.AddSingleton<IPrincipalContext>(principal);
        await using var provider = services.BuildServiceProvider();
        string userId;

        var runId = "sql-park-" + Guid.NewGuid().ToString("N")[..8];
        var instanceId = "sql-inst-" + runId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var canConnect = await db.Database.CanConnectAsync();
            if (!canConnect)
                throw new InvalidOperationException(SqlServerTestConfiguration.DescribeConnectionFailure());

            await AppertureDatabaseMigrator.MigrateAsync(db);
            var (id, username) = await SqlTestUsers.AddAsync(db, "park");
            userId = id;
            principal.Actor = new HumanActor(new ActorId(id), username, isBreakGlassSession: false);
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

        var results = await Task.WhenAll(
            InvokeParkAsync(provider, runId, authz),
            InvokeParkAsync(provider, runId, authz));

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

            // The loser of the race committed nothing, so it recorded nothing; the winner's request and its audit row share the actor.
            (await db.WorkflowExternalControlRequests.SingleAsync(x => x.RunId == runId)).RequestedBy.Should().Be(userId);
            var audit = await db.AuditLogs.AsNoTracking().Where(a => a.TargetId == runId).ToListAsync();
            audit.Should().ContainSingle("exactly one park request committed").Which.Should().Match<AuditLogEntity>(a =>
                a.Action == AuditActionNames.WorkflowRunParkRequested && a.Actor == userId && a.ActorUserId == userId);
        }
    }

    private static async Task<WorkflowExternalControlResult?> InvokeParkAsync(
        IServiceProvider provider,
        string runId,
        IWorkflowExternalControlAuthorizationService authz)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var svc = new WorkflowExternalControlService(db, authz, scope.ServiceProvider.GetRequiredService<IAuditedUnitOfWork>());
        return await svc.RequestParkAsync(new OperationRunId(runId));
    }

    private sealed class AllowAuthz : IWorkflowExternalControlAuthorizationService
    {
        public ValueTask EnsureCanControlRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class MutablePrincipalContext : IPrincipalContext
    {
        public IActor? Actor { get; set; }

        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Actor);
    }
}
