using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Integration;
using Apperture.Persistence.SqlServer;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// AUD-1.4 slice 7: the three attention routing writers run as audited units. These tests run the real service over EF Core
/// InMemory with the recording unit, which proves which rows a writer decides to stage and what it leaves unchanged. The
/// transaction, the lock and the commit guarantees are proven against SQL Server in <c>AttentionRoutingSqlServerTests</c>.
/// </summary>
public sealed class AttentionRoutingAuditedUnitTests
{
    private const string Key = AttentionConditionKeys.ExpectedRunMissing;
    private const string TeamSentinel = "team_sentinel_not_for_audit";

    // ---- the instance default ----

    [Fact]
    public async Task Changing_the_default_updates_the_row_and_stages_one_row_naming_only_the_changed_fields()
    {
        using var h = new AttentionRoutingTestHarness();
        await SeedDefaultAsync(h);
        await SeedTeamAsync(h, TeamSentinel);
        var command = Default() with
        {
            IsEnabled = false,
            ReminderInterval = TimeSpan.FromSeconds(91234),
            PrimaryOperationalTeamId = TeamSentinel,
        };

        await h.Routing.UpsertInstanceDefaultRoutingAsync(Key, command);

        var row = await h.Db.InstanceAttentionRoutings.AsNoTracking().SingleAsync();
        row.IsEnabled.Should().BeFalse();
        row.ReminderIntervalTicks.Should().Be(TimeSpan.FromSeconds(91234).Ticks);
        row.PrimaryOperationalTeamId.Should().Be(TeamSentinel);

        var staged = h.Audit.Single(AuditActionNames.AttentionRoutingDefaultUpdated);
        staged.Target.Should().Be(new AuditTarget(AuditTargetTypes.Instance, AuditEvents.InstanceTargetId));
        staged.Fields["conditionKey"].Should().Be("expected_run_missing");
        staged.Fields["changedFields"].Should().BeEquivalentTo(new[] { "enabled", "reminderInterval", "primaryTeam" });
        DetailsOf(staged).Should().NotContain(TeamSentinel).And.NotContain("91234").And.NotContain(TimeSpan.FromSeconds(91234).Ticks.ToString());
        h.Audit.Operations.Should().ContainSingle().Which.Operation.Actor.IsCurrentPrincipal.Should().BeTrue();
        h.Audit.Operations[0].Operation.PrimaryAction.Should().Be(AuditActionNames.AttentionRoutingDefaultUpdated);
    }

    [Fact]
    public async Task Setting_the_default_to_what_it_already_is_changes_nothing_and_records_nothing()
    {
        using var h = new AttentionRoutingTestHarness();
        await SeedDefaultAsync(h);
        var before = await h.Db.InstanceAttentionRoutings.AsNoTracking().SingleAsync();

        await h.Routing.UpsertInstanceDefaultRoutingAsync(Key, Default());

        h.Audit.Actions.Should().BeEmpty();
        h.Audit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
        (await h.Db.InstanceAttentionRoutings.AsNoTracking().SingleAsync()).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task A_blank_team_id_and_a_padded_one_are_compared_as_they_are_stored()
    {
        using var h = new AttentionRoutingTestHarness();
        await SeedTeamAsync(h, "team_a");
        await SeedDefaultAsync(h, primaryTeam: "team_a");

        // Padding and a blank escalation team are the stored values: no change.
        await h.Routing.UpsertInstanceDefaultRoutingAsync(Key, Default() with { PrimaryOperationalTeamId = "  team_a ", EscalationOperationalTeamId = "   " });

        h.Audit.Actions.Should().BeEmpty();
        (await h.Db.InstanceAttentionRoutings.AsNoTracking().SingleAsync()).PrimaryOperationalTeamId.Should().Be("team_a");

        // Dropping the primary team is a change of that field only.
        await h.Routing.UpsertInstanceDefaultRoutingAsync(Key, Default() with { PrimaryOperationalTeamId = "" });

        h.Audit.Single(AuditActionNames.AttentionRoutingDefaultUpdated).Fields["changedFields"].Should().BeEquivalentTo(new[] { "primaryTeam" });
        (await h.Db.InstanceAttentionRoutings.AsNoTracking().SingleAsync()).PrimaryOperationalTeamId.Should().BeNull();
    }

    [Fact]
    public async Task A_condition_with_no_default_row_is_refused_and_records_nothing()
    {
        using var h = new AttentionRoutingTestHarness();
        await SeedDefaultAsync(h);

        var act = () => h.Routing.UpsertInstanceDefaultRoutingAsync("no_such_condition", Default() with { IsEnabled = false }).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().NotBeOfType<AuditInvariantException>();
        h.Audit.Actions.Should().BeEmpty();
        (await h.Db.InstanceAttentionRoutings.AsNoTracking().SingleAsync()).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_team_on_the_default_is_refused_inside_the_unit_and_changes_nothing()
    {
        using var h = new AttentionRoutingTestHarness();
        await SeedDefaultAsync(h);

        var act = () => h.Routing.UpsertInstanceDefaultRoutingAsync(Key, Default() with { PrimaryOperationalTeamId = "team-gone" }).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*team-gone*");
        h.Audit.Actions.Should().BeEmpty();
        (await h.Db.InstanceAttentionRoutings.AsNoTracking().SingleAsync()).PrimaryOperationalTeamId.Should().BeNull();
    }

    [Fact]
    public async Task A_default_for_a_condition_the_platform_does_not_define_is_recorded_as_other()
    {
        using var h = new AttentionRoutingTestHarness();
        await SeedDefaultAsync(h, conditionKey: "customer supplied text");

        await h.Routing.UpsertInstanceDefaultRoutingAsync("customer supplied text", Default() with { SendResolvedNotification = false });

        var staged = h.Audit.Single(AuditActionNames.AttentionRoutingDefaultUpdated);
        staged.Fields["conditionKey"].Should().Be("other");
        DetailsOf(staged).Should().NotContain("customer supplied text");
    }

    // ---- an integration override: create and update ----

    [Fact]
    public async Task Creating_an_override_inserts_the_row_and_stages_created_with_the_integration_name_and_context()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await SeedTeamAsync(h, TeamSentinel);

        await h.Routing.UpsertIntegrationRoutingAsync(
            integration.Id, Key, h.SampleCommand() with { PrimaryOperationalTeamId = TeamSentinel, ReminderInterval = TimeSpan.FromSeconds(91234) });

        (await h.Db.IntegrationAttentionRoutings.AsNoTracking().SingleAsync()).PrimaryOperationalTeamId.Should().Be(TeamSentinel);
        var operation = h.Audit.Operations.Should().ContainSingle().Subject;
        operation.Operation.PrimaryAction.Should().Be(AuditActionNames.AttentionRoutingOverrideCreated);
        operation.Operation.AlternatePrimaryActions.Should().BeEquivalentTo(AuditActionNames.AttentionRoutingOverrideUpsertActions);
        operation.Operation.Actor.IsCurrentPrincipal.Should().BeTrue();

        var staged = h.Audit.Single(AuditActionNames.AttentionRoutingOverrideCreated);
        staged.Target.Should().Be(new AuditTarget(AuditTargetTypes.Integration, integration.Id.Value));
        staged.Fields["name"].Should().Be(integration.DisplayName);
        staged.Fields["conditionKey"].Should().Be("expected_run_missing");
        staged.Fields.Keys.Should().BeEquivalentTo("name", "conditionKey");
        staged.Related.ResourceContextId.Should().Be(integration.ResourceContextId.Value);
        DetailsOf(staged).Should().NotContain(TeamSentinel).And.NotContain("91234");
    }

    [Fact]
    public async Task Changing_an_override_stages_updated_naming_only_the_fields_that_differ()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, Key, h.SampleCommand());
        h.Audit.Operations.Clear();

        await h.Routing.UpsertIntegrationRoutingAsync(
            integration.Id, Key, h.SampleCommand() with { SuppressEscalationWhenAcknowledged = true, EscalationAfter = null });

        var row = await h.Db.IntegrationAttentionRoutings.AsNoTracking().SingleAsync();
        row.SuppressEscalationWhenAcknowledged.Should().BeTrue();
        row.EscalationAfterTicks.Should().BeNull();
        row.ReminderIntervalTicks.Should().Be(TimeSpan.FromMinutes(10).Ticks, "a field the command did not change keeps its value");

        var staged = h.Audit.Single(AuditActionNames.AttentionRoutingOverrideUpdated);
        staged.Fields["changedFields"].Should().BeEquivalentTo(new[] { "suppressEscalationWhenAcknowledged", "escalationAfter" });
        staged.Fields["name"].Should().Be(integration.DisplayName);
        staged.Related.ResourceContextId.Should().Be(integration.ResourceContextId.Value);
        h.Audit.Operations.Should().ContainSingle("the update is one unit");
    }

    [Fact]
    public async Task Writing_an_override_that_already_holds_those_values_changes_nothing_and_records_nothing()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, Key, h.SampleCommand());
        var before = await h.Db.IntegrationAttentionRoutings.AsNoTracking().SingleAsync();
        h.Audit.Operations.Clear();

        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, Key, h.SampleCommand());

        h.Audit.Actions.Should().BeEmpty();
        h.Audit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
        (await h.Db.IntegrationAttentionRoutings.AsNoTracking().SingleAsync()).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task An_override_for_a_condition_the_platform_does_not_define_is_recorded_as_other()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();

        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, "customer supplied text", h.SampleCommand());
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, "customer supplied text", h.SampleCommand() with { IsEnabled = false });
        await h.Routing.ClearIntegrationRoutingAsync(integration.Id, "customer supplied text");

        h.Audit.Staged.Should().HaveCount(3);
        h.Audit.Staged.Should().OnlyContain(e => (string)e.Fields["conditionKey"] == "other");
        h.Audit.Staged.Select(DetailsOf).Should().OnlyContain(d => !d.Contains("customer supplied text"));
    }

    [Fact]
    public async Task The_callers_authorization_runs_after_the_integration_lock_and_before_anything_is_staged_or_written()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var sawStaged = -1;

        await h.Routing.UpsertIntegrationRoutingAsync(
            integration.Id, Key, h.SampleCommand(),
            reauthorize: _ =>
            {
                sawStaged = h.Audit.Operations.Sum(o => o.Staged.Count);
                return ValueTask.CompletedTask;
            });

        sawStaged.Should().Be(0);
        h.Audit.Operations.Should().ContainSingle("the re-check ran inside the one unit");
    }

    [Fact]
    public async Task A_refusal_under_the_lock_stages_nothing_for_every_writer()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, Key, h.SampleCommand());
        h.Audit.Operations.Clear();
        var before = await h.Db.IntegrationAttentionRoutings.AsNoTracking().SingleAsync();
        static ValueTask Refuse(CancellationToken _) => throw new UnauthorizedAccessException("moved");

        var update = () => h.Routing.UpsertIntegrationRoutingAsync(integration.Id, Key, h.SampleCommand() with { IsEnabled = false }, reauthorize: Refuse).AsTask();
        var clear = () => h.Routing.ClearIntegrationRoutingAsync(integration.Id, Key, reauthorize: Refuse).AsTask();

        await update.Should().ThrowAsync<UnauthorizedAccessException>();
        await clear.Should().ThrowAsync<UnauthorizedAccessException>();
        h.Audit.Actions.Should().BeEmpty();
        (await h.Db.IntegrationAttentionRoutings.AsNoTracking().SingleAsync()).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task An_unknown_integration_is_refused_before_the_callers_check_and_records_nothing()
    {
        using var h = new AttentionRoutingTestHarness();
        var unknown = new IntegrationId("int_does_not_exist");

        var upsert = () => h.Routing.UpsertIntegrationRoutingAsync(unknown, Key, h.SampleCommand()).AsTask();
        var clear = () => h.Routing.ClearIntegrationRoutingAsync(unknown, Key).AsTask();

        (await upsert.Should().ThrowAsync<InvalidOperationException>()).Which.Should().NotBeOfType<AuditInvariantException>();
        (await clear.Should().ThrowAsync<InvalidOperationException>()).Which.Should().NotBeOfType<AuditInvariantException>();
        h.Audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_team_on_an_override_is_refused_inside_the_unit_and_records_nothing()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();

        var act = () => h.Routing.UpsertIntegrationRoutingAsync(integration.Id, Key, h.SampleCommand() with { EscalationOperationalTeamId = "team-gone" }).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*team-gone*");
        h.Audit.Actions.Should().BeEmpty();
        (await h.Db.IntegrationAttentionRoutings.CountAsync()).Should().Be(0);
    }

    // ---- an integration override: clear ----

    [Fact]
    public async Task Clearing_an_override_removes_the_row_and_stages_cleared()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, Key, h.SampleCommand());
        h.Audit.Operations.Clear();

        var cleared = await h.Routing.ClearIntegrationRoutingAsync(integration.Id, Key);

        cleared.Should().BeTrue();
        (await h.Db.IntegrationAttentionRoutings.CountAsync()).Should().Be(0);
        var operation = h.Audit.Operations.Should().ContainSingle().Subject;
        operation.Operation.PrimaryAction.Should().Be(AuditActionNames.AttentionRoutingOverrideCleared);
        var staged = h.Audit.Single(AuditActionNames.AttentionRoutingOverrideCleared);
        staged.Target.Should().Be(new AuditTarget(AuditTargetTypes.Integration, integration.Id.Value));
        staged.Fields["name"].Should().Be(integration.DisplayName);
        staged.Related.ResourceContextId.Should().Be(integration.ResourceContextId.Value);
    }

    [Fact]
    public async Task Clearing_an_override_that_is_not_there_returns_false_and_records_nothing()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();

        var cleared = await h.Routing.ClearIntegrationRoutingAsync(integration.Id, Key);

        cleared.Should().BeFalse();
        h.Audit.Actions.Should().BeEmpty();
        h.Audit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
    }

    // ---- the acknowledgement of the commit ----

    [Fact]
    public async Task A_cleared_override_whose_acknowledgement_was_lost_is_reported_as_cleared()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, Key, h.SampleCommand());
        h.Audit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var cleared = await h.Routing.ClearIntegrationRoutingAsync(integration.Id, Key);

        cleared.Should().BeTrue("a verified commit is the removal having happened");
    }

    [Fact]
    public async Task A_lost_acknowledgement_of_an_upsert_or_default_change_is_a_success()
    {
        using var h = new AttentionRoutingTestHarness();
        await SeedDefaultAsync(h);
        var integration = await h.CreateIntegrationAsync();
        h.Audit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, Key, h.SampleCommand());
        await h.Routing.UpsertInstanceDefaultRoutingAsync(Key, Default() with { IsEnabled = false });

        h.Audit.Actions.Should().Equal(AuditActionNames.AttentionRoutingOverrideCreated, AuditActionNames.AttentionRoutingDefaultUpdated);
    }

    [Fact]
    public async Task An_unknown_outcome_reaches_the_caller_for_every_writer()
    {
        using var h = new AttentionRoutingTestHarness();
        await SeedDefaultAsync(h);
        var integration = await h.CreateIntegrationAsync();
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, Key, h.SampleCommand());
        h.Audit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        await ((Func<Task>)(() => h.Routing.UpsertInstanceDefaultRoutingAsync(Key, Default() with { IsEnabled = false }).AsTask()))
            .Should().ThrowAsync<AuditOutcomeUnknownException>();
        await ((Func<Task>)(() => h.Routing.UpsertIntegrationRoutingAsync(integration.Id, Key, h.SampleCommand() with { IsEnabled = false }).AsTask()))
            .Should().ThrowAsync<AuditOutcomeUnknownException>();
        await ((Func<Task>)(() => h.Routing.ClearIntegrationRoutingAsync(integration.Id, Key).AsTask()))
            .Should().ThrowAsync<AuditOutcomeUnknownException>();
    }

    // ---- no unit, no write ----

    [Fact]
    public async Task Without_the_audited_unit_no_writer_runs()
    {
        await using var db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var routing = new AttentionRoutingManagementService(db, new SqlIntegrationRepository(db), null!);
        var command = Default();

        await ((Func<Task>)(() => routing.UpsertInstanceDefaultRoutingAsync(Key, command).AsTask())).Should().ThrowAsync<AuditInvariantException>();
        await ((Func<Task>)(() => routing.UpsertIntegrationRoutingAsync(new IntegrationId("int_1"), Key, command).AsTask())).Should().ThrowAsync<AuditInvariantException>();
        await ((Func<Task>)(() => routing.ClearIntegrationRoutingAsync(new IntegrationId("int_1"), Key).AsTask())).Should().ThrowAsync<AuditInvariantException>();
    }

    // ---- helpers ----

    private static AttentionRoutingUpsertCommand Default() =>
        new(
            IsEnabled: true,
            SendResolvedNotification: true,
            SuppressRemindersWhenAcknowledged: true,
            SuppressEscalationWhenAcknowledged: true,
            EscalationAfter: TimeSpan.FromMinutes(20),
            ReminderInterval: TimeSpan.FromMinutes(5),
            PrimaryOperationalTeamId: null,
            EscalationOperationalTeamId: null);

    private static async Task SeedDefaultAsync(AttentionRoutingTestHarness h, string conditionKey = Key, string? primaryTeam = null)
    {
        h.Db.InstanceAttentionRoutings.Add(new InstanceAttentionRoutingEntity
        {
            Id = InstanceAttentionRoutingDefaults.RowId,
            ConditionKey = conditionKey,
            IsEnabled = true,
            SendResolvedNotification = true,
            SuppressRemindersWhenAcknowledged = true,
            SuppressEscalationWhenAcknowledged = true,
            EscalationAfterTicks = TimeSpan.FromMinutes(20).Ticks,
            ReminderIntervalTicks = TimeSpan.FromMinutes(5).Ticks,
            PrimaryOperationalTeamId = primaryTeam,
        });
        await h.Db.SaveChangesAsync();
    }

    private static async Task SeedTeamAsync(AttentionRoutingTestHarness h, string id)
    {
        h.Db.OperationalTeams.Add(new OperationalTeamEntity { Id = id, Name = "Sample Team", IsEnabled = true });
        await h.Db.SaveChangesAsync();
    }

    private static string DetailsOf(AuditEvent auditEvent) => System.Text.Json.JsonSerializer.Serialize(auditEvent.Fields);
}
