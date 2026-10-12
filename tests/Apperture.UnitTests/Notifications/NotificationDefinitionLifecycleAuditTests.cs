using System.Text.Json;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications;

/// <summary>
/// AUD-1.4 slice 6: the notification definition writers as audited units, over the real service and store on EF InMemory with a
/// recording unit (M7 at service level; the transactional guarantees, the locks and the races are proven against SQL Server in
/// <c>NotificationDefinitionSqlServerTests</c>). A definition's rule and template are free text and are never recorded.
/// </summary>
public sealed class NotificationDefinitionLifecycleAuditTests
{
    private const string TemplateSentinel = "SECRET-TEMPLATE-TEXT-do-not-record";
    private const string RuleJson = """{"schemaVersion":"apperture.notification.rule/v1","trigger":{"eventTypes":["platform.schedule.expected_run_missing"]}}""";
    private const string TemplateJson = """{"schemaVersion":"apperture.notification.template/v1"}""";

    private static string Everything(RecordingAuditedUnitOfWork unit) =>
        JsonSerializer.Serialize(unit.Staged.Select(e => new { e.Action, e.Target, e.OutcomeCode, e.Fields, e.Related }));

    private static NotificationDefinitionCreateRequest NewRequest(string integrationId, string name = "Missed run alert") =>
        new(name, "Describes the alert", integrationId, RuleJson, TemplateJson, IsEnabled: true, DefaultSeverity: "warning");

    [Fact]
    public async Task A_creation_stages_exactly_one_created_row_in_the_integrations_context()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();

        var created = await h.Definitions.CreateAsync(NewRequest(integration.Id.Value));

        h.AuditUnit.Actions.Should().Equal(AuditActionNames.NotificationDefinitionCreated);
        var evt = h.AuditUnit.Single(AuditActionNames.NotificationDefinitionCreated);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.NotificationDefinition, created.Id.Value));
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        evt.Related.ResourceContextId.Should().Be(integration.ResourceContextId.Value);
        evt.Fields["name"].Should().Be("Missed run alert");
        evt.Fields["integrationId"].Should().Be(integration.Id.Value);
        evt.Fields["enabled"].Should().Be(true);
        evt.Fields["primaryEventType"].Should().Be("platform.schedule.expected_run_missing");
        h.AuditUnit.Operations.Single().Operation.Actor.IsCurrentPrincipal.Should().BeTrue();
    }

    [Fact]
    public async Task A_creation_records_the_actor_the_unit_recorded_as_the_creator()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();

        var created = await h.Definitions.CreateAsync(NewRequest(integration.Id.Value));

        created.CreatedByActorId.Should().Be(h.AuditUnit.CurrentPrincipalActor.RecordedValue);
    }

    [Fact]
    public async Task A_duplicate_name_or_a_missing_integration_is_refused_and_stages_nothing()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Definitions.CreateAsync(NewRequest(integration.Id.Value));
        h.AuditUnit.Operations.Clear();

        var duplicate = () => h.Definitions.CreateAsync(NewRequest(integration.Id.Value)).AsTask();
        var missing = () => h.Definitions.CreateAsync(NewRequest("int_missing", "Another name")).AsTask();

        await duplicate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*display name already exists*");
        await missing.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        h.AuditUnit.Actions.Should().BeEmpty();
        (await h.Db.NotificationDefinitions.CountAsync(d => d.Origin == NotificationDefinitionOrigins.UserCreated)).Should().Be(1);
    }

    [Fact]
    public async Task An_update_stages_one_updated_row_naming_the_changed_parts_and_never_their_content()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var created = await h.Definitions.CreateAsync(NewRequest(integration.Id.Value));
        h.AuditUnit.Operations.Clear();

        var updated = await h.Definitions.UpdateAsync(
            created.Id.Value,
            new NotificationDefinitionUpdateRequest(
                "Renamed alert", null, null, $$"""{"schemaVersion":"apperture.notification.template/v1","body":"{{TemplateSentinel}}"}""", null, null));

        updated!.DisplayName.Should().Be("Renamed alert");
        h.AuditUnit.Actions.Should().Equal(AuditActionNames.NotificationDefinitionUpdated);
        var evt = h.AuditUnit.Single(AuditActionNames.NotificationDefinitionUpdated);
        evt.Fields["changedFields"].Should().BeEquivalentTo(new[] { "displayName", "template" });
        evt.Fields["name"].Should().Be("Renamed alert", "the name snapshot is the new name");
        Everything(h.AuditUnit).Should().NotContain(TemplateSentinel);
    }

    [Fact]
    public async Task An_update_that_changes_nothing_declares_a_no_op_and_leaves_the_row_alone()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var created = await h.Definitions.CreateAsync(NewRequest(integration.Id.Value));
        h.AuditUnit.Operations.Clear();
        var before = (await h.Db.NotificationDefinitions.AsNoTracking().SingleAsync(d => d.Id == created.Id.Value)).UpdatedAtUtc;

        var same = await h.Definitions.UpdateAsync(
            created.Id.Value,
            new NotificationDefinitionUpdateRequest(created.DisplayName, created.Description, created.RuleJson, created.TemplateJson, created.IsEnabled, created.DefaultSeverity));
        var empty = await h.Definitions.UpdateAsync(created.Id.Value, new NotificationDefinitionUpdateRequest(null, null, null, null, null, null));

        same.Should().NotBeNull();
        empty.Should().NotBeNull();
        h.AuditUnit.Actions.Should().BeEmpty();
        h.AuditUnit.Operations.Should().HaveCount(2).And.OnlyContain(o => o.NoOp);
        (await h.Db.NotificationDefinitions.AsNoTracking().SingleAsync(d => d.Id == created.Id.Value)).UpdatedAtUtc.Should().Be(before);
    }

    [Fact]
    public async Task A_rename_onto_another_definitions_name_is_a_refusal_and_not_a_database_error()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Definitions.CreateAsync(NewRequest(integration.Id.Value, "First"));
        var second = await h.Definitions.CreateAsync(NewRequest(integration.Id.Value, "Second"));
        h.AuditUnit.Operations.Clear();

        var act = () => h.Definitions.UpdateAsync(
            second.Id.Value, new NotificationDefinitionUpdateRequest("First", null, null, null, null, null)).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*display name already exists*");
        h.AuditUnit.Actions.Should().BeEmpty();
        (await h.Db.NotificationDefinitions.AsNoTracking().SingleAsync(d => d.Id == second.Id.Value)).DisplayName.Should().Be("Second");
    }

    [Fact]
    public async Task A_built_in_definitions_rule_and_template_stay_immutable_and_it_cannot_be_deleted()
    {
        using var h = new NotificationDefinitionTestHarness();
        var builtIn = await h.SeedBuiltInDefinitionAsync(NotificationDefinitionOrigins.PlatformBuiltIn);

        var rule = () => h.Definitions.UpdateAsync(builtIn.Id, new NotificationDefinitionUpdateRequest(null, null, RuleJson, null, null, null)).AsTask();
        var delete = () => h.Definitions.DeleteAsync(builtIn.Id).AsTask();

        await rule.Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable*");
        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be deleted*");
        h.AuditUnit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_built_in_definitions_mutable_fields_can_still_change_and_are_audited()
    {
        using var h = new NotificationDefinitionTestHarness();
        var builtIn = await h.SeedBuiltInDefinitionAsync(NotificationDefinitionOrigins.PlatformBuiltIn);

        await h.Definitions.UpdateAsync(builtIn.Id, new NotificationDefinitionUpdateRequest(null, null, null, null, null, "critical", true));

        var evt = h.AuditUnit.Single(AuditActionNames.NotificationDefinitionUpdated);
        evt.Fields["changedFields"].Should().BeEquivalentTo(new[] { "severity", "toastDefault" });
        evt.Fields.Should().NotContainKey("integrationId", "a platform definition has no integration");
    }

    [Fact]
    public async Task A_missing_definition_is_absent_for_update_enable_and_delete_and_stages_nothing()
    {
        using var h = new NotificationDefinitionTestHarness();

        (await h.Definitions.UpdateAsync("ndef_missing", new NotificationDefinitionUpdateRequest("x", null, null, null, null, null))).Should().BeNull();
        (await h.Definitions.SetEnabledAsync("ndef_missing", false)).Should().BeNull();
        (await h.Definitions.DeleteAsync("ndef_missing")).Should().BeFalse();

        h.AuditUnit.Operations.Should().BeEmpty("nothing was read in a unit for a definition that was never there");
    }

    [Fact]
    public async Task Enabling_and_disabling_stage_their_own_rows_and_setting_the_current_state_records_nothing()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var created = await h.Definitions.CreateAsync(NewRequest(integration.Id.Value));
        h.AuditUnit.Operations.Clear();

        await h.Definitions.SetEnabledAsync(created.Id.Value, true);
        await h.Definitions.SetEnabledAsync(created.Id.Value, false);
        await h.Definitions.SetEnabledAsync(created.Id.Value, false);
        await h.Definitions.SetEnabledAsync(created.Id.Value, true);

        h.AuditUnit.Actions.Should().Equal(AuditActionNames.NotificationDefinitionDisabled, AuditActionNames.NotificationDefinitionEnabled);
        h.AuditUnit.Operations.Count(o => o.NoOp).Should().Be(2);
    }

    [Fact]
    public async Task A_deletion_stages_one_row_that_counts_what_went_with_the_definition()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var created = await h.Definitions.CreateAsync(NewRequest(integration.Id.Value));
        await h.AddSubscriptionAsync(created.Id.Value, integration.Id.Value);
        await h.AddSubscriptionAsync(created.Id.Value, integration.Id.Value);
        await h.AddUserOverrideAsync(created.Id.Value, "user-9");
        await h.AddUserPreferenceAsync(created.Id.Value, "user-9");
        h.AuditUnit.Operations.Clear();

        (await h.Definitions.DeleteAsync(created.Id.Value)).Should().BeTrue();

        var evt = h.AuditUnit.Single(AuditActionNames.NotificationDefinitionDeleted);
        evt.Fields["subscriptionsRemoved"].Should().Be(2);
        evt.Fields["userOptOutsRemoved"].Should().Be(1);
        evt.Fields["userPreferencesRemoved"].Should().Be(1);
        evt.Fields["name"].Should().Be("Missed run alert", "a deleted target keeps its name snapshot");
        (await h.Db.NotificationDefinitions.CountAsync()).Should().Be(0);
        (await h.Db.NotificationSubscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_management_check_is_made_again_once_the_integration_is_locked()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var created = await h.Definitions.CreateAsync(NewRequest(integration.Id.Value));
        var checksBefore = h.IntegrationUpdateChecks;

        await h.Definitions.UpdateAsync(created.Id.Value, new NotificationDefinitionUpdateRequest("Other", null, null, null, null, null));
        var update = h.IntegrationUpdateChecks - checksBefore;
        await h.Definitions.SetEnabledAsync(created.Id.Value, false);
        var enable = h.IntegrationUpdateChecks - checksBefore - update;

        update.Should().Be(2, "once before the unit and once inside it, against the integration as locked");
        enable.Should().Be(2);
    }

    [Fact]
    public async Task A_lost_acknowledgement_returns_the_committed_result_without_running_the_attempt_again()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        h.AuditUnit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var created = await h.Definitions.CreateAsync(NewRequest(integration.Id.Value));
        var deleted = await h.Definitions.DeleteAsync(created.Id.Value);

        created.DisplayName.Should().Be("Missed run alert");
        deleted.Should().BeTrue("a verified commit reports the deletion, never 'not found'");
        h.AuditUnit.Actions.Should().Equal(AuditActionNames.NotificationDefinitionCreated, AuditActionNames.NotificationDefinitionDeleted);
    }

    [Fact]
    public async Task An_unknown_outcome_reaches_the_caller_as_unknown()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        h.AuditUnit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        var act = () => h.Definitions.CreateAsync(NewRequest(integration.Id.Value)).AsTask();

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
    }

    [Fact]
    public async Task An_unauthorized_request_stages_nothing_and_changes_nothing()
    {
        using var h = new NotificationDefinitionTestHarness(denyIntegrationUpdate: true);
        var integration = await h.CreateIntegrationAsync();
        var seeded = await h.CreateUserDefinitionAsync(integration.Id.Value);

        await FluentActions.Awaiting(() => h.Definitions.CreateAsync(NewRequest(integration.Id.Value)).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => h.Definitions.UpdateAsync(seeded.Id.Value, new NotificationDefinitionUpdateRequest("x", null, null, null, null, null)).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => h.Definitions.SetEnabledAsync(seeded.Id.Value, false).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => h.Definitions.DeleteAsync(seeded.Id.Value).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();

        h.AuditUnit.Operations.Should().BeEmpty();
        (await h.Db.NotificationDefinitions.AsNoTracking().SingleAsync(d => d.Id == seeded.Id.Value)).DisplayName.Should().Be(seeded.DisplayName);
    }
}
