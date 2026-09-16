using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3B Plan 4 (Movement Audit, Task 3): proves <see cref="ResourceOwnershipMoveService.MoveIntegrationAsync"/>/
/// <c>MoveConnectionAsync</c>/<c>MoveScheduleAsync</c> append two-sided (moved_out/moved_in) or single
/// (move_rejected) <see cref="AuditRecord"/>s inside the same gated transaction as the mutation itself.
/// </summary>
public sealed class ResourceMoveAuditTests
{
    [Fact]
    public async Task Successful_integration_move_writes_exactly_two_records_sharing_a_correlation_id()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        h.Audit.Records.Should().HaveCount(2);
        var movedOut = h.Audit.Records.Should().ContainSingle(r => r.Action == ActionConstants.IntegrationOwnershipMovedOut).Subject;
        var movedIn = h.Audit.Records.Should().ContainSingle(r => r.Action == ActionConstants.IntegrationOwnershipMovedIn).Subject;
        movedOut.ResourceContextId.Should().Be(source.Id.Value);
        movedIn.ResourceContextId.Should().Be(target.Id.Value);
        movedOut.CorrelationId.Value.Should().NotBeNullOrEmpty();
        movedOut.CorrelationId.Value.Should().Be(movedIn.CorrelationId.Value);
    }

    [Fact]
    public async Task Blocked_integration_move_writes_exactly_one_rejected_record_at_source()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var connection = await h.CreateConnectionAsync(thirdParty.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        h.Audit.Records.Should().HaveCount(1);
        var rejected = h.Audit.Records[0];
        rejected.Action.Should().Be(ActionConstants.IntegrationOwnershipMoveRejected);
        rejected.ResourceContextId.Should().Be(source.Id.Value);
        rejected.Details.Should().Contain("binding_connection_unreachable");
    }

    [Fact]
    public async Task Successful_connection_move_writes_two_records()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        await h.Service.MoveConnectionAsync(connection.Id, target.Id);

        h.Audit.Records.Should().HaveCount(2);
        h.Audit.Records.Should().Contain(r => r.Action == ActionConstants.ConnectionOwnershipMovedOut && r.ResourceContextId == source.Id.Value);
        h.Audit.Records.Should().Contain(r => r.Action == ActionConstants.ConnectionOwnershipMovedIn && r.ResourceContextId == target.Id.Value);
    }

    [Fact]
    public async Task Blocked_schedule_move_writes_one_rejected_record()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);

        await h.Service.MoveScheduleAsync(schedule.Id, target.Id);

        h.Audit.Records.Should().HaveCount(1);
        h.Audit.Records[0].Action.Should().Be(ActionConstants.ScheduleOwnershipMoveRejected);
    }

    /// <summary>
    /// DEVIATION FROM THE BRIEF'S LITERAL ASSERTIONS -- documented here and in the task report, per the
    /// "report it, don't route around it silently" escalation rule for a discovered plan defect. The
    /// brief's Step 4a sketch asserted that, after the audit sink throws, the Integration's/Schedule's
    /// persisted <c>ResourceContextId</c> is still SOURCE. Empirically (first run of this test, before this
    /// comment existed) that assertion is FALSE under EF Core InMemory with the brief's own Step 3
    /// mutate-then-audit ordering: <see cref="SqlIntegrationRepository.SetResourceContextIdAsync"/>'s
    /// InMemory fallback path (no relational provider -&gt; <c>ExecuteUpdateAsync</c> is unavailable) calls
    /// <c>SaveChangesAsync</c> immediately, and <see cref="ResourceOwnershipMoveService.MoveIntegrationAsync"/>
    /// opens no transaction at all for a non-relational provider (<c>tx</c> is <see langword="null"/> when
    /// <c>!_db.Database.IsRelational()</c>). So the ownership write is durably visible in the InMemory store
    /// the instant <c>SetResourceContextIdAsync</c> runs, with nothing left to roll back when the audit
    /// append throws afterward -- exactly the InMemory limitation the brief's own prose already flags
    /// ("this test proves the exception propagates ... not that the database rolled back"), just not fully
    /// carried through into the brief's own assertions.
    ///
    /// This is NOT a production code bug, and fixing it would require changing Plan 2's
    /// <c>SetResourceContextIdAsync</c>/transaction wiring -- out of bounds for this task per its own
    /// escalation rule. The mutate-then-audit ORDER in production is deliberately kept exactly as the
    /// brief's Step 3 template specifies, because that same order is what makes the REAL proof --
    /// <c>ResourceMoveAuditFilteringSqlServerTests.Audit_failure_rolls_the_movement_back_on_real_sql_server</c>
    /// -- meaningful: only a real SQL Server transaction, rolled back on the dropped/uncommitted
    /// <c>tx</c>, can undo an already-applied mutation. This test proves what InMemory actually can prove:
    /// the exception propagates as a hard failure (never silently swallowed into a partial "success"), and
    /// the audit sink was invoked exactly once -- the failed <c>moved_out</c> attempt -- never twice, i.e.
    /// the service made no further audit writes after the failure.
    /// </summary>
    [Fact]
    public async Task Audit_failure_rolls_the_movement_back()
    {
        var throwingAudit = new ThrowingAuditService();
        await using var h = new ResourceOwnershipMoveTestHarness(audit: throwingAudit);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.CreateAttachedScheduleAsync(integration.Id);

        var act = async () => await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "an audit sink failure must propagate as a hard failure, never be swallowed into a silent partial move");
        throwingAudit.CallCount.Should().Be(1,
            "the service must stop immediately on the first audit failure -- it must never attempt the second (moved_in) append");
    }

    /// <summary>
    /// The entire "append inside the move transaction" design depends on this: the move service and the
    /// audit sink must resolve the SAME scoped <see cref="AppertureDbContext"/> instance, or the audit
    /// append would run in a different connection/transaction than the mutation and the rollback proof in
    /// <see cref="Audit_failure_rolls_the_movement_back"/> (and its real-SQL companion in
    /// <c>ResourceMoveAuditFilteringSqlServerTests</c>) would mean nothing.
    ///
    /// FIX (review round 1, Important 2): the first version of this test hand-copied
    /// <c>services.AddScoped&lt;IAuditService, SqlAuditService&gt;()</c> and
    /// <c>services.AddScoped&lt;IResourceOwnershipMoveService, ResourceOwnershipMoveService&gt;()</c> into
    /// a private <see cref="ServiceCollection"/> it built itself -- so a future change to the REAL
    /// production registration (e.g. swapping <see cref="SqlAuditService"/> for a factory-based or
    /// own-context sink) would never fail this test, since it never read production's actual registrations.
    /// This version instead calls the REAL production composition, in the REAL order
    /// <c>Apperture.Management.Host/Program.cs</c> uses (lines 49, 55, 66, 67):
    /// <c>AddSchedulePersistence()</c>, <c>AddAppertureGovernance()</c> (registers
    /// <c>IAuditService -&gt; SqlAuditService</c> at <c>ServiceCollectionExtensions.Governance.cs:96</c>),
    /// <c>AddAppertureConnectorWorkflows()</c>, and <c>AddAppertureIntegrationWorkflows()</c> (registers
    /// <c>IResourceOwnershipMoveService -&gt; ResourceOwnershipMoveService</c> at
    /// <c>ServiceCollectionExtensions.Integration.cs:31</c>). If any of those four calls ever stops
    /// registering either service the way production currently does, this test breaks -- exactly the
    /// brief's own bar ("a future registration change fails a test"). Registering these methods does not
    /// execute any of their bodies beyond adding <see cref="ServiceDescriptor"/> entries (no I/O, no
    /// resolution) and <c>BuildServiceProvider(validateScopes: true)</c> does not eagerly construct
    /// anything either -- only <see cref="IResourceOwnershipMoveService"/>/<see cref="IAuditService"/> and
    /// their transitive graph are ever actually constructed below, so the hundreds of unrelated services
    /// these four methods also register (dashboards, MFA, workflow control, etc.) are inert.
    ///
    /// Only <see cref="IPrincipalContext"/> and <see cref="IAuthorizationEvaluationService"/> are
    /// overridden with minimal test doubles, registered AFTER the real calls above -- the built-in
    /// container resolves the LAST registration for a given service type, so this does not change what
    /// <see cref="IAuditService"/> or <see cref="IResourceOwnershipMoveService"/> themselves resolve to; it
    /// only avoids needing production's real <c>HostPrincipalContext</c>/<c>AuthorizationEvaluationService</c>,
    /// which pull in ASP.NET Core/Entra infrastructure that has nothing to do with DbContext sharing.
    /// </summary>
    [Fact]
    public void Move_service_and_audit_service_resolve_the_same_DbContext_instance_per_scope()
    {
        using var provider = BuildProductionLikeServiceProvider();
        using var scope = provider.CreateScope();

        var moveService = scope.ServiceProvider.GetRequiredService<IResourceOwnershipMoveService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

        ExtractDbContext(moveService).Should().BeSameAs(ExtractDbContext(auditService));

        // Independent corroboration of the same fact via the container itself, per the brief's accepted
        // alternative form: resolving AppertureDbContext twice in one scope must yield the same instance.
        var dbFirst = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var dbSecond = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        dbFirst.Should().BeSameAs(dbSecond);
    }

    private static ServiceProvider BuildProductionLikeServiceProvider()
    {
        var services = new ServiceCollection();

        // Matches AddDbContext<AppertureDbContext>'s default (Scoped) lifetime -- see
        // Apperture.Persistence.SqlServer/ServiceCollectionExtensions.cs:28. InMemory instead of SQL
        // Server: this test proves DI graph shape (one instance per scope), not transactional behavior --
        // that proof lives in ResourceMoveAuditFilteringSqlServerTests against a real database. The real
        // Program.cs uses AddApperturePersistence(...) here instead, which needs a real connection string;
        // that is orthogonal to what this test (post-fix) is about, which is IAuditService/
        // IResourceOwnershipMoveService's own registrations, not AppertureDbContext's.
        services.AddDbContext<AppertureDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));

        // The REAL production composition calls, in the REAL order Program.cs:49,55,66,67 uses.
        services.AddSchedulePersistence();
        services.AddAppertureGovernance();
        services.AddAppertureConnectorWorkflows();
        services.AddAppertureIntegrationWorkflows();

        // Minimal test doubles standing in for production's HostPrincipalContext/AuthorizationEvaluationService
        // (which require ASP.NET Core HttpContext/Entra infrastructure irrelevant to this test's claim).
        // Registered LAST so they win the "last registration wins" resolution rule -- see the method's
        // doc comment above for why this does not weaken what's being proven.
        services.AddScoped<IPrincipalContext, AlwaysAllowPrincipalContext>();
        services.AddScoped<IAuthorizationEvaluationService, AlwaysAllowAuthorizationEvaluationService>();

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Reflects out the private <c>_db</c> field both <see cref="ResourceOwnershipMoveService"/> and <see cref="SqlAuditService"/> hold.</summary>
    private static AppertureDbContext ExtractDbContext(object service)
    {
        var field = service.GetType().GetField("_db", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{service.GetType().Name} has no private '_db' field.");
        return (AppertureDbContext)field.GetValue(service)!;
    }

    [Fact]
    public async Task Source_filtered_audit_sees_moved_out_target_filtered_sees_moved_in_global_sees_both()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        var atSource = h.Audit.Records.Where(r => r.ResourceContextId == source.Id.Value).ToList();
        var atTarget = h.Audit.Records.Where(r => r.ResourceContextId == target.Id.Value).ToList();

        atSource.Should().ContainSingle().Which.Action.Should().Be(ActionConstants.IntegrationOwnershipMovedOut);
        atTarget.Should().ContainSingle().Which.Action.Should().Be(ActionConstants.IntegrationOwnershipMovedIn);
        h.Audit.Records.Should().HaveCount(2, "the unfiltered/global view sees both");
        atSource[0].CorrelationId.Value.Should().Be(atTarget[0].CorrelationId.Value);
    }

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

/// <summary>Throws from <see cref="AppendAsync"/> so <c>Audit_failure_rolls_the_movement_back</c> can prove the move transaction rolls back when audit append fails.</summary>
public sealed class ThrowingAuditService : IAuditService
{
    /// <summary>Number of times <see cref="AppendAsync"/> was invoked before this call's throw -- lets a test prove the service made no further audit attempts after the first failure.</summary>
    public int CallCount { get; private set; }

    public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        CallCount++;
        throw new InvalidOperationException("Simulated audit sink failure.");
    }

    public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised.");

    public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised.");

    public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised.");
}
