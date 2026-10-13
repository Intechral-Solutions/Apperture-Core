using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// CTX-3B Plan 6a, Task 3: <see cref="IAttentionRoutingManagementService.ClearIntegrationRoutingAsync"/>
/// removes an Integration's attention-routing override for one condition, reverting it to the instance
/// default. This is the remediation path that makes the Integration hard-delete "has attention-routing
/// overrides" blocker (added separately, Task 4) actually clearable.
/// </summary>
public sealed class IntegrationAttentionRoutingClearTests
{
    [Fact]
    public async Task Clearing_an_override_removes_the_row()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing, h.SampleCommand());

        var cleared = await h.Routing.ClearIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing);

        cleared.Should().BeTrue();
        (await h.Db.IntegrationAttentionRoutings.CountAsync(r => r.IntegrationId == integration.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Clearing_an_override_falls_back_to_the_instance_default()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing, h.SampleCommand());
        await h.Routing.ClearIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing);

        var effective = await h.Routing.GetEffectiveRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing);

        effective.Should().NotBeNull("clearing an override reverts to the instance default, it does not remove routing entirely");
    }

    [Fact]
    public async Task Clearing_a_condition_leaves_other_conditions_overrides_intact()
    {
        // The operation is per (IntegrationId, ConditionKey), matching the unique index the schema already
        // has (IX_IntegrationAttentionRoutings_IntegrationId_ConditionKey).
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        const string otherConditionKey = "other_condition";
        var otherCommand = new AttentionRoutingUpsertCommand(
            IsEnabled: false,
            SendResolvedNotification: false,
            SuppressRemindersWhenAcknowledged: false,
            SuppressEscalationWhenAcknowledged: false,
            EscalationAfter: TimeSpan.FromMinutes(45),
            ReminderInterval: TimeSpan.FromMinutes(15),
            PrimaryOperationalTeamId: null,
            EscalationOperationalTeamId: null);

        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing, h.SampleCommand());
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, otherConditionKey, otherCommand);

        var cleared = await h.Routing.ClearIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing);

        cleared.Should().BeTrue();
        var remaining = await h.Db.IntegrationAttentionRoutings
            .SingleOrDefaultAsync(r => r.IntegrationId == integration.Id.Value);
        remaining.Should().NotBeNull();
        remaining!.ConditionKey.Should().Be(otherConditionKey);
        remaining.IsEnabled.Should().Be(otherCommand.IsEnabled);
        remaining.SendResolvedNotification.Should().Be(otherCommand.SendResolvedNotification);
        remaining.SuppressRemindersWhenAcknowledged.Should().Be(otherCommand.SuppressRemindersWhenAcknowledged);
        remaining.SuppressEscalationWhenAcknowledged.Should().Be(otherCommand.SuppressEscalationWhenAcknowledged);
        remaining.EscalationAfterTicks.Should().Be(otherCommand.EscalationAfter!.Value.Ticks);
        remaining.ReminderIntervalTicks.Should().Be(otherCommand.ReminderInterval!.Value.Ticks);
    }

    [Fact]
    public async Task Clearing_when_no_override_exists_returns_false_and_changes_nothing()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();

        var cleared = await h.Routing.ClearIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing);

        cleared.Should().BeFalse();
        (await h.Db.IntegrationAttentionRoutings.CountAsync(r => r.IntegrationId == integration.Id.Value)).Should().Be(0);
    }

    // ---- the upsert under the integration row lock (AUD-1.4 slice 3) ----

    [Fact]
    public async Task Upserting_for_an_unknown_integration_is_refused_and_stores_nothing()
    {
        using var h = new AttentionRoutingTestHarness();

        var act = () => h.Routing.UpsertIntegrationRoutingAsync(
            new IntegrationId("int_does_not_exist"), AttentionConditionKeys.ExpectedRunMissing, h.SampleCommand()).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*int_does_not_exist was not found*");
        (await h.Db.IntegrationAttentionRoutings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Upserting_twice_updates_the_one_override_and_keeps_a_single_row()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var changed = new AttentionRoutingUpsertCommand(
            IsEnabled: false,
            SendResolvedNotification: false,
            SuppressRemindersWhenAcknowledged: false,
            SuppressEscalationWhenAcknowledged: false,
            EscalationAfter: TimeSpan.FromMinutes(90),
            ReminderInterval: TimeSpan.FromMinutes(20),
            PrimaryOperationalTeamId: null,
            EscalationOperationalTeamId: null);

        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing, h.SampleCommand());
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing, changed);

        var row = await h.Db.IntegrationAttentionRoutings.AsNoTracking().SingleAsync(r => r.IntegrationId == integration.Id.Value);
        row.IsEnabled.Should().BeFalse();
        row.EscalationAfterTicks.Should().Be(TimeSpan.FromMinutes(90).Ticks);
    }

    [Fact]
    public async Task The_callers_authorization_runs_again_once_the_integration_is_locked_and_a_refusal_stores_nothing()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var reauthorized = 0;

        var act = () => h.Routing.UpsertIntegrationRoutingAsync(
            integration.Id,
            AttentionConditionKeys.ExpectedRunMissing,
            h.SampleCommand(),
            reauthorize: _ =>
            {
                reauthorized++;
                throw new UnauthorizedAccessException("moved");
            }).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        reauthorized.Should().Be(1);
        (await h.Db.IntegrationAttentionRoutings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_team_is_refused_and_stores_nothing()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var command = h.SampleCommand() with { PrimaryOperationalTeamId = "team-gone" };

        var act = () => h.Routing.UpsertIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing, command).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*team-gone*");
        (await h.Db.IntegrationAttentionRoutings.CountAsync()).Should().Be(0);
    }

    // ---- the clear under the integration row lock (attention-routing authorization remediation) ----

    [Fact]
    public async Task Clearing_runs_the_callers_authorization_once_the_integration_is_locked_and_a_refusal_removes_nothing()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing, h.SampleCommand());
        var reauthorized = 0;

        var act = () => h.Routing.ClearIntegrationRoutingAsync(
            integration.Id,
            AttentionConditionKeys.ExpectedRunMissing,
            reauthorize: _ =>
            {
                reauthorized++;
                throw new UnauthorizedAccessException("moved");
            }).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        reauthorized.Should().Be(1);
        (await h.Db.IntegrationAttentionRoutings.AsNoTracking().CountAsync(r => r.IntegrationId == integration.Id.Value))
            .Should().Be(1, "a refused clear removes nothing");
    }

    [Fact]
    public async Task Clearing_with_no_override_still_runs_the_callers_authorization_and_stays_a_no_op()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var reauthorized = 0;

        var cleared = await h.Routing.ClearIntegrationRoutingAsync(
            integration.Id,
            AttentionConditionKeys.ExpectedRunMissing,
            reauthorize: _ =>
            {
                reauthorized++;
                return ValueTask.CompletedTask;
            });

        cleared.Should().BeFalse();
        reauthorized.Should().Be(1, "who may clear is decided even when there is nothing to clear");
    }

    [Fact]
    public async Task Clearing_with_a_passing_authorization_removes_the_override_as_before()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing, h.SampleCommand());

        var cleared = await h.Routing.ClearIntegrationRoutingAsync(
            integration.Id, AttentionConditionKeys.ExpectedRunMissing, reauthorize: _ => ValueTask.CompletedTask);

        cleared.Should().BeTrue();
        (await h.Db.IntegrationAttentionRoutings.CountAsync(r => r.IntegrationId == integration.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Clearing_for_an_unknown_integration_does_not_run_the_callers_authorization()
    {
        using var h = new AttentionRoutingTestHarness();
        var reauthorized = 0;

        var act = () => h.Routing.ClearIntegrationRoutingAsync(
            new IntegrationId("int_does_not_exist"),
            AttentionConditionKeys.ExpectedRunMissing,
            reauthorize: _ =>
            {
                reauthorized++;
                return ValueTask.CompletedTask;
            }).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        reauthorized.Should().Be(0, "there is no integration to authorize against");
    }

    [Fact]
    public async Task Clearing_for_an_unknown_integration_throws_like_upsert_does()
    {
        using var h = new AttentionRoutingTestHarness();
        var unknown = new IntegrationId("int_does_not_exist");

        var act = () => h.Routing.ClearIntegrationRoutingAsync(unknown, AttentionConditionKeys.ExpectedRunMissing).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{unknown.Value} was not found*");
    }
}

/// <summary>
/// Wires a real <see cref="AttentionRoutingManagementService"/> over the real
/// <see cref="SqlIntegrationRepository"/> and <see cref="SqlAttentionRecipientResolver"/> (EF Core
/// InMemory <see cref="AppertureDbContext"/>). Narrowly scoped to routing behavior -- no authorization
/// scaffolding here; that is Task 4's shared IntegrationLifecycleTestHarness.
/// </summary>
public sealed class AttentionRoutingTestHarness : IDisposable
{
    private int _nextSequence;

    public AttentionRoutingTestHarness()
    {
        Db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);

        var integrations = new SqlIntegrationRepository(Db);
        var resolver = new SqlAttentionRecipientResolver(Db, new OperationalTeamRecipientExpander(Db));
        Audit = new Apperture.UnitTests.Audit.RecordingAuditedUnitOfWork();
        Routing = new AttentionRoutingManagementService(Db, integrations, resolver, Audit);
    }

    public AppertureDbContext Db { get; }

    /// <summary>The audited unit the routing service runs in: keeps what each operation staged.</summary>
    internal Apperture.UnitTests.Audit.RecordingAuditedUnitOfWork Audit { get; }

    public IAttentionRoutingManagementService Routing { get; }

    public async Task<DomainIntegration> CreateIntegrationAsync(CancellationToken cancellationToken = default)
    {
        var integration = new DomainIntegration(
            new IntegrationId($"int_{NextSequence()}"),
            "Sample Integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

        await new SqlIntegrationRepository(Db).AddAsync(integration, cancellationToken).ConfigureAwait(false);
        return integration;
    }

    public AttentionRoutingUpsertCommand SampleCommand() =>
        new(
            IsEnabled: true,
            SendResolvedNotification: true,
            SuppressRemindersWhenAcknowledged: false,
            SuppressEscalationWhenAcknowledged: false,
            EscalationAfter: TimeSpan.FromMinutes(30),
            ReminderInterval: TimeSpan.FromMinutes(10),
            PrimaryOperationalTeamId: null,
            EscalationOperationalTeamId: null);

    private int NextSequence() => System.Threading.Interlocked.Increment(ref _nextSequence);

    public void Dispose() => Db.Dispose();
}
