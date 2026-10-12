using System.Text.Json;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications;

/// <summary>
/// AUD-1.4 slice 6: the administrator subscription writers as audited units, over the real service and stores on EF InMemory with
/// a recording unit (M7 at service level; the transactional guarantees, the locks and the races are proven against SQL Server in
/// <c>NotificationSubscriptionSqlServerTests</c>). A subscription is identified by its recipient's id; an address never reaches an
/// audit row, including the address typed to create a managed contact.
/// </summary>
public sealed class NotificationSubscriptionLifecycleAuditTests
{
    private const string AddressSentinel = "private.person@example.com";

    private static string Everything(RecordingAuditedUnitOfWork unit) =>
        JsonSerializer.Serialize(unit.Staged.Select(e => new { e.Action, e.Target, e.OutcomeCode, e.Fields, e.Related }));

    private static async Task<(string IntegrationId, string DefinitionId)> ArrangeAsync(NotificationDefinitionTestHarness h)
    {
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.CreateUserDefinitionAsync(integration.Id.Value);
        return (integration.Id.Value, definition.Id.Value);
    }

    private static NotificationSubscriptionAddRequest ByRef(string integrationId, string user = "user-1", string mode = NotificationDeliveryModes.Immediate) =>
        new(integrationId, NotificationRecipientKinds.Principal, user, null, false, mode);

    [Fact]
    public async Task An_addition_stages_one_added_row_naming_the_recipient_by_id_in_the_integrations_context()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);

        var added = await h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId, "user-7"));

        h.AuditUnit.Actions.Should().Equal(AuditActionNames.NotificationSubscriptionAdded);
        var evt = h.AuditUnit.Single(AuditActionNames.NotificationSubscriptionAdded);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.NotificationSubscription, added.Id.Value));
        evt.Fields["definitionId"].Should().Be(definitionId);
        evt.Fields["recipientKind"].Should().Be(NotificationRecipientKinds.Principal);
        evt.Fields["recipientRef"].Should().Be("user-7");
        evt.Fields["deliveryMode"].Should().Be(NotificationDeliveryModes.Immediate);
        evt.Fields.Should().NotContainKey("managedContactCreated");
        evt.Related.ResourceContextId.Should().NotBeNull();
        added.CreatedByActorId.Should().Be(h.AuditUnit.CurrentPrincipalActor.RecordedValue);
    }

    [Fact]
    public async Task A_new_managed_contact_is_created_in_the_same_unit_and_its_address_is_never_recorded()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);

        var added = await h.Subscriptions.AddRecipientAsync(
            definitionId,
            new NotificationSubscriptionAddRequest(integrationId, null, null, $"Private Person <{AddressSentinel}>", true));

        h.AuditUnit.Actions.Should().BeEquivalentTo(AuditActionNames.NotificationSubscriptionAdded, AuditActionNames.ManagedContactCreated);
        var contact = h.AuditUnit.Single(AuditActionNames.ManagedContactCreated);
        contact.Fields["via"].Should().Be("subscription");
        contact.Fields["hasEmail"].Should().Be(true);
        var sub = h.AuditUnit.Single(AuditActionNames.NotificationSubscriptionAdded);
        sub.Fields["managedContactCreated"].Should().Be(true);
        sub.Fields["recipientKind"].Should().Be(NotificationRecipientKinds.ManagedContact);
        sub.Fields["recipientRef"].Should().Be(contact.Target.Id, "the subscription names the contact the unit created");
        added.RecipientRef.Should().Be(contact.Target.Id);
        Everything(h.AuditUnit).Should().NotContain(AddressSentinel).And.NotContain("Private Person");
    }

    [Fact]
    public async Task An_existing_managed_contact_is_reused_without_a_contact_row()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);
        var existing = await h.Contacts.CreateCustomAsync("Known", AddressSentinel, true, DateTimeOffset.UtcNow);

        await h.Subscriptions.AddRecipientAsync(
            definitionId, new NotificationSubscriptionAddRequest(integrationId, null, null, AddressSentinel, false));

        h.AuditUnit.Actions.Should().Equal(AuditActionNames.NotificationSubscriptionAdded);
        h.AuditUnit.Single(AuditActionNames.NotificationSubscriptionAdded).Fields["recipientRef"].Should().Be(existing);
        (await h.Db.ManagedContacts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_refused_addition_leaves_no_contact_behind_in_the_unit()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);
        await h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId, "user-1"));
        h.AuditUnit.Operations.Clear();

        var duplicate = () => h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId, "user-1")).AsTask();
        var missing = () => h.Subscriptions.AddRecipientAsync(
            definitionId, new NotificationSubscriptionAddRequest(integrationId, null, null, "no.such.contact@example.com", false)).AsTask();
        var badKind = () => h.Subscriptions.AddRecipientAsync(
            definitionId, new NotificationSubscriptionAddRequest(integrationId, "nobody", "x", null, false)).AsTask();

        await duplicate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already subscribed*");
        await missing.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No managed contact found*");
        await badKind.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Unsupported recipient kind*");
        h.AuditUnit.Actions.Should().BeEmpty();
        (await h.Db.NotificationSubscriptions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_unknown_definition_or_integration_is_refused_and_stages_nothing()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);

        var noDefinition = () => h.Subscriptions.AddRecipientAsync("ndef_missing", ByRef(integrationId)).AsTask();
        var wrongIntegration = () => h.Subscriptions.AddRecipientAsync(definitionId, ByRef("int_other")).AsTask();

        await noDefinition.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        await wrongIntegration.Should().ThrowAsync<InvalidOperationException>().WithMessage("*matching integration id*");
        h.AuditUnit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_removal_stages_one_removed_row_and_a_second_removal_is_absent()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);
        var added = await h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId, "user-3"));
        h.AuditUnit.Operations.Clear();

        (await h.Subscriptions.RemoveAsync(definitionId, added.Id.Value)).Should().BeTrue();
        (await h.Subscriptions.RemoveAsync(definitionId, added.Id.Value)).Should().BeFalse();

        h.AuditUnit.Actions.Should().Equal(AuditActionNames.NotificationSubscriptionRemoved);
        var evt = h.AuditUnit.Single(AuditActionNames.NotificationSubscriptionRemoved);
        evt.Target.Id.Should().Be(added.Id.Value, "a removed target keeps its id");
        evt.Fields["recipientRef"].Should().Be("user-3");
        (await h.Db.NotificationSubscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_subscription_of_another_definition_is_absent_for_remove_and_patch()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);
        var other = await h.CreateUserDefinitionAsync(integrationId);
        var added = await h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId));
        h.AuditUnit.Operations.Clear();

        (await h.Subscriptions.RemoveAsync(other.Id.Value, added.Id.Value)).Should().BeFalse();
        (await h.Subscriptions.PatchAsync(other.Id.Value, added.Id.Value, new NotificationSubscriptionPatchRequest(false, null))).Should().BeNull();

        h.AuditUnit.Operations.Should().BeEmpty();
        (await h.Db.NotificationSubscriptions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_patch_names_its_change_and_one_that_changes_nothing_records_nothing()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);
        var added = await h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId));
        h.AuditUnit.Operations.Clear();

        await h.Subscriptions.PatchAsync(definitionId, added.Id.Value, new NotificationSubscriptionPatchRequest(false, null));
        await h.Subscriptions.PatchAsync(definitionId, added.Id.Value, new NotificationSubscriptionPatchRequest(false, null));
        await h.Subscriptions.PatchAsync(definitionId, added.Id.Value, new NotificationSubscriptionPatchRequest(null, NotificationDeliveryModes.Muted));
        await h.Subscriptions.PatchAsync(definitionId, added.Id.Value, new NotificationSubscriptionPatchRequest(null, NotificationDeliveryModes.Muted));
        await h.Subscriptions.PatchAsync(definitionId, added.Id.Value, new NotificationSubscriptionPatchRequest(null, null));

        h.AuditUnit.Actions.Should().Equal(AuditActionNames.NotificationSubscriptionUpdated, AuditActionNames.NotificationSubscriptionUpdated);
        var rows = h.AuditUnit.Staged.ToList();
        rows[0].Fields["change"].Should().Be("disabled");
        rows[1].Fields["change"].Should().Be("delivery_mode");
        rows[1].Fields["deliveryMode"].Should().Be(NotificationDeliveryModes.Muted);
        h.AuditUnit.Operations.Count(o => o.NoOp).Should().Be(3);
    }

    [Fact]
    public async Task A_patch_with_both_fields_applies_only_the_enabled_flag_as_before()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);
        var added = await h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId));
        h.AuditUnit.Operations.Clear();

        var patched = await h.Subscriptions.PatchAsync(
            definitionId, added.Id.Value, new NotificationSubscriptionPatchRequest(false, NotificationDeliveryModes.Muted));

        patched!.IsEnabled.Should().BeFalse();
        patched.DeliveryMode.Should().Be(NotificationDeliveryModes.Immediate);
        h.AuditUnit.Single(AuditActionNames.NotificationSubscriptionUpdated).Fields["change"].Should().Be("disabled");
    }

    [Fact]
    public async Task An_unsupported_delivery_mode_is_refused()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);
        var added = await h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId));

        var act = () => h.Subscriptions.PatchAsync(definitionId, added.Id.Value, new NotificationSubscriptionPatchRequest(null, "whenever")).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Unsupported delivery mode*");
    }

    [Fact]
    public async Task A_user_opt_out_targets_the_opt_out_row_names_the_user_by_id_and_is_idempotent()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (_, definitionId) = await ArrangeAsync(h);

        var first = await h.Subscriptions.SetUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-5", null));
        var second = await h.Subscriptions.SetUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-5", null));

        second.Id.Should().Be(first.Id);
        h.AuditUnit.Actions.Should().Equal(AuditActionNames.NotificationSubscriptionUserOptOutSet);
        var evt = h.AuditUnit.Single(AuditActionNames.NotificationSubscriptionUserOptOutSet);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.NotificationSubscription, first.Id));
        evt.Fields["userId"].Should().Be("user-5");
        h.AuditUnit.Operations.Count(o => o.NoOp).Should().Be(1);
        (await h.Db.NotificationSubscriptionUserOverrides.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Removing_a_user_opt_out_stages_one_row_and_removing_it_again_is_false_and_records_nothing()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (_, definitionId) = await ArrangeAsync(h);
        var set = await h.Subscriptions.SetUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-5", "team-1"));
        h.AuditUnit.Operations.Clear();

        (await h.Subscriptions.RemoveUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-5", "team-1"))).Should().BeTrue();
        (await h.Subscriptions.RemoveUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-5", "team-1"))).Should().BeFalse();

        h.AuditUnit.Actions.Should().Equal(AuditActionNames.NotificationSubscriptionUserOptOutRemoved);
        var evt = h.AuditUnit.Single(AuditActionNames.NotificationSubscriptionUserOptOutRemoved);
        evt.Target.Id.Should().Be(set.Id, "the removed opt-out keeps its id");
        evt.Fields["operationalTeamId"].Should().Be("team-1");
    }

    [Fact]
    public async Task A_blank_user_id_is_refused_before_a_unit_opens()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (_, definitionId) = await ArrangeAsync(h);

        await FluentActions.Awaiting(() => h.Subscriptions.SetUserOptOutAsync(definitionId, new NotificationUserOptOutRequest(" ", null)).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => h.Subscriptions.RemoveUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("", null)).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();

        h.AuditUnit.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task The_management_check_is_made_again_once_the_integration_is_locked()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);
        var before = h.IntegrationUpdateChecks;

        await h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId));

        (h.IntegrationUpdateChecks - before).Should().Be(2, "once before the unit and once inside it, against the integration as locked");
    }

    [Fact]
    public async Task An_unsupported_delivery_mode_on_add_is_a_refusal_before_the_unit_opens_and_not_an_audit_schema_error()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);

        var act = () => h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId, "user-1", mode: "whenever")).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Unsupported delivery mode*");
        h.AuditUnit.Operations.Should().BeEmpty();
        (await h.Db.NotificationSubscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_address_typed_as_a_recipient_reference_is_refused_so_it_can_never_be_recorded_as_one()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);

        var act = () => h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId, AddressSentinel)).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*id, not an email address*");
        h.AuditUnit.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_subscription_writer_and_the_definition_delete_check_management_again_under_the_lock()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);
        var sub = await h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId, "user-keep"));
        var other = await h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId, "user-other"));
        await h.Subscriptions.SetUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-out", null));

        int Checks(Func<Task> operation)
        {
            var before = h.IntegrationUpdateChecks;
            operation().GetAwaiter().GetResult();
            return h.IntegrationUpdateChecks - before;
        }

        Checks(async () => await h.Subscriptions.PatchAsync(definitionId, sub.Id.Value, new NotificationSubscriptionPatchRequest(false, null))).Should().Be(2, "patch");
        Checks(async () => await h.Subscriptions.RemoveAsync(definitionId, other.Id.Value)).Should().Be(2, "remove");
        Checks(async () => await h.Subscriptions.SetUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-new", null))).Should().Be(2, "opt-out set");
        Checks(async () => await h.Subscriptions.RemoveUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-out", null))).Should().Be(2, "opt-out removed");
        Checks(async () => await h.Definitions.DeleteAsync(definitionId)).Should().Be(2, "definition delete");
    }

    [Fact]
    public async Task A_lost_acknowledgement_returns_the_committed_result()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);
        h.AuditUnit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var added = await h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId, "user-8"));
        var removed = await h.Subscriptions.RemoveAsync(definitionId, added.Id.Value);
        var optOut = await h.Subscriptions.SetUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-9", null));
        var optOutRemoved = await h.Subscriptions.RemoveUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-9", null));

        added.RecipientRef.Should().Be("user-8");
        removed.Should().BeTrue("a verified commit reports the removal, never 'not found'");
        optOut.UserId.Should().Be("user-9");
        optOutRemoved.Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_outcome_reaches_the_caller_as_unknown()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);
        h.AuditUnit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        var act = () => h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId)).AsTask();

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
    }

    [Fact]
    public async Task An_unauthorized_request_stages_nothing_and_changes_nothing()
    {
        using var h = new NotificationDefinitionTestHarness(denyIntegrationUpdate: true);
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.CreateUserDefinitionAsync(integration.Id.Value);
        await h.AddSubscriptionAsync(definition.Id.Value, integration.Id.Value);
        var existing = await h.Db.NotificationSubscriptions.AsNoTracking().SingleAsync();

        await FluentActions.Awaiting(() => h.Subscriptions.AddRecipientAsync(definition.Id.Value, ByRef(integration.Id.Value)).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => h.Subscriptions.RemoveAsync(definition.Id.Value, existing.Id).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => h.Subscriptions.PatchAsync(definition.Id.Value, existing.Id, new NotificationSubscriptionPatchRequest(false, null)).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => h.Subscriptions.SetUserOptOutAsync(definition.Id.Value, new NotificationUserOptOutRequest("u", null)).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();

        h.AuditUnit.Operations.Should().BeEmpty();
        (await h.Db.NotificationSubscriptions.CountAsync()).Should().Be(1);
        (await h.Db.NotificationSubscriptionUserOverrides.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Deleting_the_definition_removes_its_subscriptions_and_a_later_removal_is_absent()
    {
        using var h = new NotificationDefinitionTestHarness();
        var (integrationId, definitionId) = await ArrangeAsync(h);
        var added = await h.Subscriptions.AddRecipientAsync(definitionId, ByRef(integrationId));
        await h.Definitions.DeleteAsync(definitionId);
        h.AuditUnit.Operations.Clear();

        var act = () => h.Subscriptions.RemoveAsync(definitionId, added.Id.Value).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        h.AuditUnit.Actions.Should().BeEmpty();
    }
}
