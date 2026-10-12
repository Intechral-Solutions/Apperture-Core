using System.Reflection;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// AUD-1.4 slice 5 (over CTX-3B Plan 4): <see cref="ResourceOwnershipMoveService.MoveIntegrationAsync"/>, <c>MoveConnectionAsync</c>
/// and <c>MoveScheduleAsync</c> stage the two-sided pair (<c>moved_out</c>, <c>moved_in</c>) in the same audited unit as the
/// mutation, and a blocked move stages nothing, declares itself a no-op, and is recorded after the unit as one best-effort
/// <c>move_rejected</c> row. The transactional guarantees (rollback with the dependent updates, ambiguous commits, isolation modes)
/// are proven against SQL Server in <c>OwnershipMoveSqlServerTests</c>.
/// </summary>
public sealed class ResourceMoveAuditTests
{
    [Fact]
    public async Task Successful_integration_move_stages_the_pair_sharing_the_operations_event_id_as_correlation()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        h.MoveUnit.Actions.Should().Equal(AuditActionNames.IntegrationMovedOut, AuditActionNames.IntegrationMovedIn);
        var movedOut = h.MoveUnit.Single(AuditActionNames.IntegrationMovedOut);
        var movedIn = h.MoveUnit.Single(AuditActionNames.IntegrationMovedIn);
        movedOut.Target.Should().Be(new AuditTarget(AuditTargetTypes.Integration, integration.Id.Value));
        movedOut.Related.ResourceContextId.Should().Be(source.Id.Value);
        movedIn.Related.ResourceContextId.Should().Be(target.Id.Value);
        movedOut.Related.CorrelationId.Should().NotBeNullOrEmpty().And.Be(movedIn.Related.CorrelationId);
        movedOut.Related.CorrelationId.Should().Be(h.MoveUnit.Operations.Single().OperationEventId);
        movedOut.Fields.Should().BeEquivalentTo(movedIn.Fields);
        h.AuditWriter.Attempts.Should().BeEmpty("a committed move writes no rejection");
    }

    [Fact]
    public async Task Blocked_integration_move_stages_nothing_declares_a_no_op_and_records_one_rejection_at_the_source()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var connection = await h.CreateConnectionAsync(thirdParty.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        var preview = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        h.MoveUnit.Actions.Should().BeEmpty();
        h.MoveUnit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
        var rejected = h.AuditWriter.Attempts.Should().ContainSingle().Subject;
        rejected.Action.Should().Be(AuditActionNames.IntegrationMoveRejected);
        rejected.OutcomeCode.Should().Be(AuditOutcomeCodes.Rejected);
        rejected.Related.ResourceContextId.Should().Be(source.Id.Value);
        rejected.Fields["blockers"].Should().BeEquivalentTo(new[] { "binding_connection_unreachable" });
        h.AuditWriter.Actors.Should().ContainSingle().Which.IsCurrentPrincipal.Should().BeTrue();
    }

    [Fact]
    public async Task Successful_connection_move_stages_the_pair()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        await h.Service.MoveConnectionAsync(connection.Id, target.Id);

        h.MoveUnit.Staged.Should().HaveCount(2);
        h.MoveUnit.Staged.Should().Contain(e => e.Action == AuditActionNames.ConnectionMovedOut && e.Related.ResourceContextId == source.Id.Value);
        h.MoveUnit.Staged.Should().Contain(e => e.Action == AuditActionNames.ConnectionMovedIn && e.Related.ResourceContextId == target.Id.Value);
        h.MoveUnit.Staged.Should().OnlyContain(e => !e.Fields.ContainsKey("schedulesMoved"));
    }

    [Fact]
    public async Task Blocked_schedule_move_records_one_rejection_and_stages_nothing()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);

        await h.Service.MoveScheduleAsync(schedule.Id, target.Id);

        h.MoveUnit.Actions.Should().BeEmpty();
        h.AuditWriter.Attempts.Should().ContainSingle().Which.Action.Should().Be(AuditActionNames.ScheduleMoveRejected);
    }

    [Fact]
    public async Task An_integration_move_counts_its_attached_schedules_and_lists_them()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var one = await h.CreateAttachedScheduleAsync(integration.Id);
        var two = await h.CreateAttachedScheduleAsync(integration.Id);

        await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        var movedOut = h.MoveUnit.Single(AuditActionNames.IntegrationMovedOut);
        movedOut.Fields["schedulesMoved"].Should().Be(2);
        ((IReadOnlyList<string>)movedOut.Fields["scheduleIds"]!).Should().BeEquivalentTo([one.Id.Value, two.Id.Value]);
        h.MoveUnit.Staged.Should().HaveCount(2, "attached schedules move with the integration and never get rows of their own");
    }

    [Fact]
    public async Task Source_filtered_events_see_moved_out_target_filtered_see_moved_in_global_sees_both()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        var atSource = h.MoveUnit.Staged.Where(e => e.Related.ResourceContextId == source.Id.Value).ToList();
        var atTarget = h.MoveUnit.Staged.Where(e => e.Related.ResourceContextId == target.Id.Value).ToList();
        atSource.Should().ContainSingle().Which.Action.Should().Be(AuditActionNames.IntegrationMovedOut);
        atTarget.Should().ContainSingle().Which.Action.Should().Be(AuditActionNames.IntegrationMovedIn);
        h.MoveUnit.Staged.Should().HaveCount(2, "the unfiltered view sees both");
    }

    [Fact]
    public async Task A_lost_acknowledgement_returns_a_committed_result_built_from_the_pair_and_runs_the_attempt_once()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);
        h.MoveUnit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var preview = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Source.Id.Should().Be(source.Id.Value);
        preview.Target.Id.Should().Be(target.Id.Value);
        preview.Effects.Should().ContainSingle(e => e.Code == ResourceMoveEffectCodes.SchedulesMoveWithIntegration)
            .Which.RelatedResourceIds.Should().BeEquivalentTo([schedule.Id.Value]);
        h.MoveUnit.Operations.Should().ContainSingle();
    }

    [Fact]
    public async Task An_unknown_outcome_is_reported_as_unknown_for_every_family()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var connection = await h.CreateConnectionAsync(source.Id);
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);
        h.MoveUnit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        await FluentActions.Awaiting(() => h.Service.MoveIntegrationAsync(integration.Id, target.Id).AsTask()).Should().ThrowAsync<AuditOutcomeUnknownException>();
        await FluentActions.Awaiting(() => h.Service.MoveConnectionAsync(connection.Id, target.Id).AsTask()).Should().ThrowAsync<AuditOutcomeUnknownException>();
        await FluentActions.Awaiting(() => h.Service.MoveScheduleAsync(schedule.Id, target.Id).AsTask()).Should().ThrowAsync<AuditOutcomeUnknownException>();
    }

    [Fact]
    public async Task Every_move_refuses_to_run_without_the_audited_unit()
    {
        await using var h = new ResourceOwnershipMoveTestHarness(withoutAuditedUnit: true);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        await FluentActions.Awaiting(() => h.Service.MoveIntegrationAsync(integration.Id, target.Id).AsTask()).Should().ThrowAsync<AuditInvariantException>();
        await FluentActions.Awaiting(() => h.Service.MoveConnectionAsync(new Apperture.Core.Domain.Connector.ConnectionId("x"), target.Id).AsTask()).Should().ThrowAsync<AuditInvariantException>();
        await FluentActions.Awaiting(() => h.Service.MoveScheduleAsync(new Apperture.Core.Domain.Scheduling.ScheduleId("x"), target.Id).AsTask()).Should().ThrowAsync<AuditInvariantException>();
    }

    /// <summary>
    /// The unit, the repositories and the gate must be the request's one scoped <see cref="AppertureDbContext"/>, or the ownership
    /// change, its dependent updates and the audit rows would not share a transaction. This calls the REAL production composition,
    /// in the order <c>Apperture.Management.Host/Program.cs</c> uses, and follows each participant's own dependency to the context
    /// it actually holds. The rejection writer is the recorder's single-row writer on a context of its own (a rejection is its own
    /// short transaction after the unit, never part of it).
    /// </summary>
    [Fact]
    public void The_move_service_unit_repositories_and_gate_share_the_request_context_and_the_rejection_writer_has_its_own()
    {
        using var provider = BuildProductionLikeServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var move = scope.ServiceProvider.GetRequiredService<IResourceOwnershipMoveService>()
            .Should().BeOfType<ResourceOwnershipMoveService>().Subject;

        Dependency(move, "_auditedUnitOfWork").Should().BeOfType<SqlAuditedUnitOfWork>("the unit is registered, so no move refuses to run");
        Context(Dependency(move, "_auditedUnitOfWork")).Should().BeSameAs(context);
        Context(Dependency(move, "_integrationRepository")).Should().BeSameAs(context);
        Context(Dependency(move, "_connectionRepository")).Should().BeSameAs(context);
        Context(Dependency(move, "_scheduleRepository")).Should().BeSameAs(context);
        Context(Dependency(move, "_ownershipGate")).Should().BeSameAs(context, "the gate requires the unit's transaction to be current");

        var writer = Dependency(move, "_auditEvents").Should().BeOfType<SqlAuditEventWriter>().Subject;
        writer.GetType().GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic).Should().BeNull();
        writer.GetType().GetField("_contextOptions", BindingFlags.Instance | BindingFlags.NonPublic).Should().NotBeNull();

        scope.ServiceProvider.GetRequiredService<AppertureDbContext>().Should().BeSameAs(context);
    }

    private static ServiceProvider BuildProductionLikeServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();

        // No connection is opened: this proves the shape of the DI graph, not transactional behaviour.
        services.AddApperturePersistence(_ => "Server=unused;Database=unused;TrustServerCertificate=True");
        services.AddAppertureAuditRecorder();
        services.AddSchedulePersistence();
        services.AddAppertureGovernance();
        services.AddAppertureConnectorWorkflows();
        services.AddAppertureIntegrationWorkflows();

        // Stand-ins for the host's principal context and authorization (ASP.NET Core and Entra infrastructure irrelevant to the claim).
        services.AddScoped<IPrincipalContext, AlwaysAllowPrincipalContext>();
        services.AddScoped<IAuthorizationEvaluationService, AlwaysAllowAuthorizationEvaluationService>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static object Dependency(object service, string field) =>
        service.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(service)
            ?? throw new InvalidOperationException($"{service.GetType().Name} holds no '{field}'.");

    private static AppertureDbContext Context(object service) => (AppertureDbContext)Dependency(service, "_db");

    private sealed class AlwaysAllowPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new FixedActor());

        private sealed class FixedActor : IActor
        {
            public ActorId Id => new("di-shape-test-actor");
            public ActorKind Kind => ActorKind.Human;
            public string? DisplayName => "DI Shape Test Actor";
        }
    }

    private sealed class AlwaysAllowAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }
}
