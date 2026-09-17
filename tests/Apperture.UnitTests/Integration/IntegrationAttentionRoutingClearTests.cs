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
        Routing = new AttentionRoutingManagementService(Db, integrations, resolver);
    }

    public AppertureDbContext Db { get; }

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
