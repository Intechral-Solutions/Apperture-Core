using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.IntegrationTests;

/// <summary>
/// CTX-3B Plan 4 (Movement Audit, Task 3, Step 6a): the REQUIRED real-SQL proof for two-sided movement
/// audit. Unlike <c>ResourceMoveAuditTests</c> (Apperture.UnitTests, EF Core InMemory + a recording double),
/// every test here drives the real <see cref="ResourceOwnershipMoveService"/> over a real
/// <see cref="SqlAuditService"/> against a real, throwaway, migrated SQL Server catalog
/// (<see cref="SqlServerScratchDatabase"/>) -- so it genuinely exercises
/// <see cref="SqlAuditService.QueryAsync"/>'s <c>AuditQuery.FilterByResourceContextId</c> translation to
/// SQL, and (for <c>Audit_failure_rolls_the_movement_back_on_real_sql_server</c>) a real SQL Server
/// transaction rollback -- something EF Core InMemory cannot prove at all (see the InMemory limitation
/// documented on <c>ResourceMoveAuditTests.Audit_failure_rolls_the_movement_back</c>).
/// </summary>
/// <remarks>
/// This is Plan 4's explicitly required-not-optional test (design gate closure evidence, alongside
/// <c>AuditCorrelationIdSqlServerTests</c>): a soft-skipped green suite here is not acceptable closure
/// evidence, so <see cref="RequireFixtureAsync"/> fails the test hard rather than silently skipping when no
/// SQL Server endpoint is reachable -- mirroring <c>ResourceOwnershipMoveConcurrencySqlServerTests</c>'s own
/// <c>RequireFixtureAsync</c>.
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ResourceMoveAuditFilteringSqlServerTests
{
    [Fact]
    public async Task Source_target_and_global_audit_queries_each_return_the_right_records()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_MoveAuditFilter1_");
        await using var db = fixture.NewDb();
        var env = Env.Build(db);

        var source = await env.ResourceContexts.CreateApplicationContextAsync("AuditFilter1-Source");
        var target = await env.ResourceContexts.CreateApplicationContextAsync("AuditFilter1-Target");
        var integration = await CreateIntegrationAsync(env, source.Id);

        await env.MoveService.MoveIntegrationAsync(integration.Id, target.Id);

        // Real SqlAuditService, real AuditQuery.FilterByResourceContextId, real rows -- not a recording
        // double and not LINQ over a list. This is what proves an operator filtering Audit to the source
        // Application sees "moved out", filtering to the target sees "moved in", and the unfiltered view
        // sees both with one shared correlation id.
        var atSource = await env.Audit.QueryAsync(new AuditQuery(FilterByResourceContextId: source.Id.Value, PageSize: 10));
        var atTarget = await env.Audit.QueryAsync(new AuditQuery(FilterByResourceContextId: target.Id.Value, PageSize: 10));
        var global = await env.Audit.QueryAsync(new AuditQuery(PageSize: 10));

        atSource.Items.Should().ContainSingle().Which.Action.Should().Be(ActionConstants.IntegrationOwnershipMovedOut);
        atTarget.Items.Should().ContainSingle().Which.Action.Should().Be(ActionConstants.IntegrationOwnershipMovedIn);
        global.Items.Should().HaveCount(2);
        atSource.Items[0].CorrelationId.Value.Should().Be(atTarget.Items[0].CorrelationId.Value);
        atSource.Items[0].CorrelationId.Value.Should().NotBeNullOrEmpty();

        // Finding 1 regression guard: SqlAuditService.AppendAsync appends "Resource={record.Resource}" as
        // plain text onto Details when AuditRecord.Resource is non-null, which would corrupt this JSON
        // payload. ResourceOwnershipMoveService now passes Resource: null on every append specifically so
        // this stays valid JSON on a REAL persisted row (not merely in the in-memory AuditRecord before it
        // ever reaches SqlAuditService) -- deserializing it here is what actually proves that.
        var movedOutDetails = JsonSerializer.Deserialize<ResourceMoveAuditDetails>(atSource.Items[0].Details!);
        movedOutDetails.Should().NotBeNull("the persisted Details column must still be valid, deserializable JSON");
        movedOutDetails!.ResourceId.Should().Be(integration.Id.Value);
        movedOutDetails.SourceResourceContextId.Should().Be(source.Id.Value);
        movedOutDetails.TargetResourceContextId.Should().Be(target.Id.Value);
    }

    [Fact]
    public async Task Blocked_move_writes_one_rejected_record_at_source_and_none_at_target()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_MoveAuditFilter2_");
        await using var db = fixture.NewDb();
        var env = Env.Build(db);

        var source = await env.ResourceContexts.CreateApplicationContextAsync("AuditFilter2-Source");
        var target = await env.ResourceContexts.CreateApplicationContextAsync("AuditFilter2-Target");
        var thirdParty = await env.ResourceContexts.CreateApplicationContextAsync("AuditFilter2-ThirdParty");
        var integration = await CreateIntegrationAsync(env, source.Id);
        var connection = await CreateConnectionAsync(env, thirdParty.Id);

        // Direct repository insert (bypasses BindingService's own eligibility checks), exactly like the
        // InMemory harness's AddDirectBindingAsync: builds the cross-context binding that makes the move
        // blockable (binding_connection_unreachable) without fighting eligibility filtering.
        await env.BindingRepository.AddAsync(new Binding(
            new BindingId($"b-{Guid.NewGuid():N}"), integration.Id, integration.ModuleId, "slot-a",
            "test.contract", connection.Id, null, BindingState.Active));

        await env.MoveService.MoveIntegrationAsync(integration.Id, target.Id);

        var atSource = await env.Audit.QueryAsync(new AuditQuery(FilterByResourceContextId: source.Id.Value, PageSize: 10));
        var atTarget = await env.Audit.QueryAsync(new AuditQuery(FilterByResourceContextId: target.Id.Value, PageSize: 10));

        atSource.Items.Should().ContainSingle().Which.Action.Should().Be(ActionConstants.IntegrationOwnershipMoveRejected);
        atTarget.Items.Should().BeEmpty("a rejection is the source's event; the target never heard about it");

        (await env.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value
            .Should().Be(source.Id.Value, "a blocked move must mutate nothing");
    }

    /// <summary>
    /// The genuine rollback proof the InMemory unit test (<c>ResourceMoveAuditTests.Audit_failure_rolls_the_movement_back</c>)
    /// cannot give: a real SQL Server transaction, opened by <see cref="ResourceOwnershipMoveService.MoveIntegrationAsync"/>
    /// before the ownership gate, that never commits because the (substituted) audit sink throws after the
    /// mutation has already run inside it. The mutation (<c>SetResourceContextIdAsync</c>, an
    /// <c>ExecuteUpdateAsync</c> against the open transaction's connection) is undone when <c>tx</c> is
    /// disposed without <c>CommitAsync</c> ever being called -- proven here by reading the Integration back
    /// through a brand-new <see cref="AppertureDbContext"/>/connection after the exception, and finding it
    /// still owned by SOURCE.
    /// </summary>
    [Fact]
    public async Task Audit_failure_rolls_the_movement_back_on_real_sql_server()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_MoveAuditFilter3_");
        await using var db = fixture.NewDb();
        var env = Env.Build(db, audit: new ThrowingAuditService());

        var source = await env.ResourceContexts.CreateApplicationContextAsync("AuditFilter3-Source");
        var target = await env.ResourceContexts.CreateApplicationContextAsync("AuditFilter3-Target");
        var integration = await CreateIntegrationAsync(env, source.Id);

        var act = async () => await env.MoveService.MoveIntegrationAsync(integration.Id, target.Id);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "an audit sink failure must propagate as a hard failure, never be swallowed into a silent partial move");

        // A FRESH DbContext/connection, deliberately not env.Db: proves the rollback is visible to any
        // other observer, not merely "the same tracked instance never got the memo."
        await using var assertDb = fixture.NewDb();
        var reread = await new SqlIntegrationRepository(assertDb).GetByIdAsync(integration.Id);
        reread!.ResourceContextId.Value.Should().Be(
            source.Id.Value, "an unauditable move must roll back the ownership write, not just fail to report success");
    }

    /// <summary>
    /// Finding 3 (review round 1, Important): none of this file's original three tests ever moved an
    /// Integration with attached Schedules, so nothing exercised <c>movedScheduleIds</c> being populated,
    /// or proved "exactly two audit records regardless of N attached schedules" in the N&gt;0 case -- a
    /// future regression that appended audit rows inside
    /// <c>foreach (var schedule in validation.AttachedSchedules)</c>
    /// (<c>ResourceOwnershipMoveService.cs</c>, the schedule-mutation loop) would have broken nothing in
    /// this suite. Real SQL Server, matching this file's required-test theme.
    /// </summary>
    [Fact]
    public async Task Successful_move_with_attached_schedules_writes_exactly_two_records_and_lists_all_moved_schedule_ids()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_MoveAuditFilter4_");
        await using var db = fixture.NewDb();
        var env = Env.Build(db);

        var source = await env.ResourceContexts.CreateApplicationContextAsync("AuditFilter4-Source");
        var target = await env.ResourceContexts.CreateApplicationContextAsync("AuditFilter4-Target");
        var integration = await CreateIntegrationAsync(env, source.Id);
        var scheduleOne = await CreateAttachedScheduleAsync(env, integration.Id, source.Id);
        var scheduleTwo = await CreateAttachedScheduleAsync(env, integration.Id, source.Id);

        await env.MoveService.MoveIntegrationAsync(integration.Id, target.Id);

        // Exactly two -- not 2 + 2*schedules. This is the assertion a regression that audits inside the
        // per-schedule mutation loop would break.
        var global = await env.Audit.QueryAsync(new AuditQuery(PageSize: 10));
        global.Items.Should().HaveCount(2, "attached schedules move silently with the Integration; they never get their own audit rows");

        var movedOut = global.Items.Should().ContainSingle(r => r.Action == ActionConstants.IntegrationOwnershipMovedOut).Subject;
        var movedIn = global.Items.Should().ContainSingle(r => r.Action == ActionConstants.IntegrationOwnershipMovedIn).Subject;
        movedOut.CorrelationId.Value.Should().Be(movedIn.CorrelationId.Value);

        var details = JsonSerializer.Deserialize<ResourceMoveAuditDetails>(movedOut.Details!);
        details.Should().NotBeNull("Finding 1's fix must leave this Details column valid, deserializable JSON");
        details!.MovedScheduleIds.Should().NotBeNull();
        details.MovedScheduleIds.Should().BeEquivalentTo([scheduleOne.Id.Value, scheduleTwo.Id.Value]);

        // Both schedules must have actually moved too, not just been listed.
        (await env.ScheduleRepository.GetDefinitionAsync(scheduleOne.Id))!.ResourceContextId!.Value.Should().Be(target.Id.Value);
        (await env.ScheduleRepository.GetDefinitionAsync(scheduleTwo.Id))!.ResourceContextId!.Value.Should().Be(target.Id.Value);
    }

    // ================================================================================================
    // Fixture / environment plumbing
    // ================================================================================================

    private static async Task<SqlServerScratchDatabase> RequireFixtureAsync(string catalogPrefix)
    {
        var fixture = await SqlServerScratchDatabase.CreateAsync(catalogPrefix);
        // Plan 4's own closure evidence requirement (see the brief): a soft-skipped green suite here is
        // not acceptable evidence that the two-sided model or the rollback guarantee actually holds.
        fixture.Should().NotBeNull(
            "CTX-3B Plan 4 closure requires ResourceMoveAuditFilteringSqlServerTests to genuinely execute " +
            "against SQL Server -- a soft-skipped green suite is not acceptable evidence. " +
            SqlServerTestConfiguration.DescribeActiveConfiguration());
        return fixture!;
    }

    private static async Task<DomainIntegration> CreateIntegrationAsync(Env env, ResourceContextId contextId)
    {
        var now = DateTimeOffset.UtcNow;
        var integration = new DomainIntegration(
            new IntegrationId($"int-{Guid.NewGuid():N}"), $"Integration-{Guid.NewGuid():N}", "Sample.Mod",
            null, IntegrationState.Active, null, now, now)
        { ResourceContextId = contextId };
        await env.IntegrationRepository.AddAsync(integration);
        return integration;
    }

    private static async Task<Connection> CreateConnectionAsync(Env env, ResourceContextId contextId)
    {
        var connection = new Connection(
            new ConnectionId($"conn-{Guid.NewGuid():N}"), new ConnectorDefinitionId($"test.connector.{Guid.NewGuid():N}"),
            $"Connection-{Guid.NewGuid():N}", new Dictionary<string, string>(), new Dictionary<string, string>(),
            null, null)
        { ResourceContextId = contextId };
        await env.ConnectionRepository.AddAsync(connection);
        return connection;
    }

    /// <summary>Direct repository insert of an Integration-attached ScheduleDefinition, for Finding 3's schedules test.</summary>
    private static async Task<ScheduleDefinition> CreateAttachedScheduleAsync(Env env, IntegrationId integrationId, ResourceContextId contextId)
    {
        var schedule = new ScheduleDefinition(
            new ScheduleId($"s-{Guid.NewGuid():N}"), $"Schedule-{Guid.NewGuid():N}", "0 0 * * *", "UTC",
            Enabled: true, new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"), ExecutionPolicyRef: null,
            Version: 1, integrationId)
        { ResourceContextId = contextId };
        await env.ScheduleRepository.AddDefinitionAsync(schedule);
        return schedule;
    }

    /// <summary>
    /// One real production-service wiring over one real <see cref="AppertureDbContext"/> (SQL Server) --
    /// the minimal slice of <c>ResourceOwnershipMoveConcurrencySqlServerTests</c>'s own <c>Env</c> that this
    /// file's three tests need. <paramref name="audit"/> defaults to a real <see cref="SqlAuditService"/>
    /// (production shape); Test 3 substitutes a throwing double to force the rollback.
    /// </summary>
    private sealed class Env
    {
        public required IResourceContextService ResourceContexts { get; init; }
        public required IIntegrationRepository IntegrationRepository { get; init; }
        public required IConnectionRepository ConnectionRepository { get; init; }
        public required IBindingRepository BindingRepository { get; init; }
        public required IScheduleRepository ScheduleRepository { get; init; }
        public required IAuditService Audit { get; init; }
        public required ResourceOwnershipMoveService MoveService { get; init; }

        public static Env Build(AppertureDbContext db, IAuditService? audit = null)
        {
            var resourceContextRepository = new SqlResourceContextRepository(db);
            var resourceContexts = new ResourceContextService(resourceContextRepository, TimeProvider.System);
            var integrationRepository = new SqlIntegrationRepository(db);
            var connectionRepository = new SqlConnectionRepository(db);
            var bindingRepository = new SqlBindingRepository(db);
            var scheduleRepository = new SqlScheduleRepository(db);
            var gate = new SqlResourceOwnershipMutationGate(db);
            var auditService = audit ?? new SqlAuditService(db);

            var moveService = new ResourceOwnershipMoveService(
                new FixedPrincipalContext(),
                new AlwaysAllowAuthorizationEvaluationService(),
                integrationRepository,
                connectionRepository,
                scheduleRepository,
                bindingRepository,
                resourceContexts,
                db,
                gate,
                auditService);

            return new Env
            {
                ResourceContexts = resourceContexts,
                IntegrationRepository = integrationRepository,
                ConnectionRepository = connectionRepository,
                BindingRepository = bindingRepository,
                ScheduleRepository = scheduleRepository,
                Audit = auditService,
                MoveService = moveService,
            };
        }
    }

    /// <summary>Throws from <see cref="AppendAsync"/> so <c>Audit_failure_rolls_the_movement_back_on_real_sql_server</c> can force a real transaction rollback.</summary>
    private sealed class ThrowingAuditService : IAuditService
    {
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated audit sink failure.");

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised.");

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised.");

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised.");
    }

    private sealed class FixedPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new FixedActor());

        private sealed class FixedActor : IActor
        {
            public ActorId Id => new("move-audit-filter-test-actor");
            public ActorKind Kind => ActorKind.Human;
            public string? DisplayName => "Move Audit Filter Test Actor";
        }
    }

    private sealed class AlwaysAllowAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }
}
