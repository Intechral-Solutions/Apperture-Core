using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Persistence.SqlServer;
using Apperture.UnitTests.PlatformSettings;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// Design gate §13 CTX-3B step 7: the confirmed referential-lifecycle gap. Extends the existing
/// Binding/Schedule delete-block pattern to every current-state dependent the inventory
/// (<c>ctx3b-integration-delete-referential-inventory.md</c>) decided to block on, proves the one decided
/// cascade cleanup (PlatformSettingValues) both removes the row AND disposes of its secret, and proves
/// every blocker it adds is actually clearable by an operator.
/// </summary>
public sealed class IntegrationDeleteReferentialLifecycleTests
{
    [Fact]
    public async Task Delete_blocked_by_integration_scoped_notification_definition()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.AddUserNotificationDefinitionAsync(integration.Id.Value);

        var act = async () => await h.Integrations.DeleteIntegrationAsync(integration.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*notification definition*");
    }

    [Fact]
    public async Task Delete_blocked_by_an_integration_scoped_subscription_under_a_platform_scoped_definition()
    {
        // The case the NotificationDefinition blocker does NOT cover, and the actual reason subscriptions
        // get a blocker of their own: NotificationSubscriptionEntity carries its own nullable
        // IntegrationId independently of NotificationDefinitionId, so a subscription can be scoped to an
        // Integration while its definition is Platform-scoped and carries no IntegrationId at all.
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.AddPlatformScopedDefinitionAsync("nd-platform");
        await h.AddSubscriptionAsync("nd-platform", integrationId: integration.Id.Value);

        var act = async () => await h.Integrations.DeleteIntegrationAsync(integration.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*subscription*");
    }

    [Fact]
    public async Task Delete_blocked_by_attention_routing_override()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.AddAttentionRoutingAsync(integration.Id.Value);

        var act = async () => await h.Integrations.DeleteIntegrationAsync(integration.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*attention routing*");
    }

    [Fact]
    public async Task Every_blocker_this_plan_adds_can_be_cleared_by_an_operator_operation()
    {
        // The reachability invariant, end to end. This is the test that stops CTX-3B from repeating
        // CTX-1's unreachable-archive trap (design gate F4): set up all three new blockers at once, clear
        // each through the real operator-facing service, then delete successfully.
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.AddUserNotificationDefinitionAsync(integration.Id.Value);
        await h.AddPlatformScopedDefinitionAsync("nd-platform");
        var subscription = await h.AddSubscriptionAsync("nd-platform", integrationId: integration.Id.Value);
        await h.AddAttentionRoutingAsync(integration.Id.Value);

        var blockedFirst = async () => await h.Integrations.DeleteIntegrationAsync(integration.Id);
        await blockedFirst.Should().ThrowAsync<InvalidOperationException>();

        // Clear each blocker the way an operator would -- through the real services, not by touching rows.
        await h.Definitions.DeleteAsync(definition.Id.Value);                                // Task 2
        await h.Subscriptions.RemoveAsync(new NotificationSubscriptionId(subscription.Id));  // pre-existing
        await h.Routing.ClearIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing); // Task 3

        await h.Integrations.DeleteIntegrationAsync(integration.Id);

        (await h.Db.Integrations.CountAsync(i => i.Id == integration.Id.Value)).Should().Be(0,
            "every blocker CTX-3B adds must have an operator-accessible remediation path");
    }

    [Fact]
    public async Task No_undeletable_definition_can_ever_block_integration_delete()
    {
        // Guards the coincidence the inventory relies on: the definitions that block Integration delete
        // (IntegrationId set) and the definitions Task 2's operation accepts (Origin == UserCreated) are
        // the same set today, because CreateUserDefinitionAsync sets both together. If a future seed
        // carries an IntegrationId with a built-in Origin, this test fails rather than silently
        // recreating an unreachable blocker.
        //
        // Deviation from the plan's literal snippet, which seeded a built-in definition carrying an
        // IntegrationId and then asserted no such row existed -- a test that can only ever fail, and that
        // would have proven the opposite of its own stated intent. Realized here as the invariant the
        // comment actually describes: seed every real built-in definition seed, then assert that no
        // definition carrying an IntegrationId at all has a built-in Origin. The user-created definition
        // keeps the assertion non-vacuous (FluentAssertions' OnlyContain rejects an empty collection, but
        // an empty result would prove nothing about seeds either way).
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.AddUserNotificationDefinitionAsync(integration.Id.Value);
        await h.SeedAllBuiltInDefinitionsAsync();

        var blocking = await h.Db.NotificationDefinitions
            .Where(d => d.IntegrationId != null)
            .Select(d => d.Origin)
            .ToListAsync();

        blocking.Should().OnlyContain(o => o == NotificationDefinitionOrigins.UserCreated,
            "a built-in definition carrying an IntegrationId would block Integration delete with no way to clear it");
    }

    [Fact]
    public async Task Delete_leaves_historical_operation_run_rows_untouched_with_the_dangling_id()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        h.Db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "run-1", WorkflowInstanceId = "wi-1", DefinitionId = "def-1",
            Status = "Completed", CreatedAt = DateTimeOffset.UtcNow, IntegrationId = integration.Id.Value,
            ResourceContextId = integration.ResourceContextId.Value
        });
        await h.Db.SaveChangesAsync();

        await h.Integrations.DeleteIntegrationAsync(integration.Id);

        var run = await h.Db.OperationRuns.AsNoTracking().SingleAsync(r => r.Id == "run-1");
        run.IntegrationId.Should().Be(integration.Id.Value,
            "the historical snapshot must survive, dangling, exactly as CTX-2 established");
        (await h.Db.Integrations.CountAsync(i => i.Id == integration.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Delete_still_succeeds_with_no_dependents_at_all()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();

        await h.Integrations.DeleteIntegrationAsync(integration.Id);

        (await h.Db.Integrations.CountAsync(i => i.Id == integration.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Delete_writes_exactly_one_integration_deleted_audit_record()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();

        await h.Integrations.DeleteIntegrationAsync(integration.Id);

        // AUD-1.4 slice 3: a staged W1 row of the delete's own unit, not a legacy record appended after it.
        h.DeleteAudit.Actions.Should().Equal(AuditActionNames.IntegrationDeleted);
        var deleted = h.DeleteAudit.Single(AuditActionNames.IntegrationDeleted);
        deleted.Related.ResourceContextId.Should().Be(integration.ResourceContextId.Value,
            "the delete is attributable to the Context that owned the Integration");
        deleted.Target.Should().Be(new AuditTarget(AuditTargetTypes.Integration, integration.Id.Value));
        deleted.Fields["name"].Should().Be(integration.DisplayName);
        deleted.Fields["settingsRemoved"].Should().Be(0);
        deleted.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        h.Audit.Records.Should().NotContain(r => r.Action == ActionConstants.IntegrationDeleted, "the legacy append is gone");
    }

    [Fact]
    public async Task A_blocked_delete_writes_no_audit_record()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.AddAttentionRoutingAsync(integration.Id.Value);

        var act = async () => await h.Integrations.DeleteIntegrationAsync(integration.Id);
        var thrown = await act.Should().ThrowAsync<IntegrationDeleteBlockedException>();
        thrown.Which.Blocker.Should().Be(IntegrationDeleteBlocker.AttentionRoutings);

        h.Audit.Records.Should().BeEmpty("nothing was deleted, so nothing is recorded as deleted");
        h.DeleteAudit.Actions.Should().BeEmpty("a refused delete commits no row");
        (await h.Db.Integrations.CountAsync(i => i.Id == integration.Id.Value)).Should().Be(1);
    }

    [Fact]
    public async Task A_missing_integration_is_a_typed_not_found_and_records_nothing()
    {
        using var h = new IntegrationLifecycleTestHarness();

        var act = async () => await h.Integrations.DeleteIntegrationAsync(new IntegrationId("int_missing"));

        await act.Should().ThrowAsync<IntegrationNotFoundException>();
        h.DeleteAudit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task Removing_a_sensitive_setting_on_delete_records_its_secret_retirement_under_the_delete()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Settings.SetAsync(integration.Id.Value, h.SensitiveSettingKey,
            new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = "hunter2" },
            rowVersionBase64: null);
        await h.Settings.SetAsync(integration.Id.Value, h.SettingKey,
            new PlatformSettingUpdatePayload { ScalarValue = "x" }, rowVersionBase64: null);

        await h.Integrations.DeleteIntegrationAsync(integration.Id);

        h.DeleteAudit.Actions.Should().BeEquivalentTo(
            [AuditActionNames.IntegrationDeleted, AuditActionNames.IntegrationSettingSecretRetired],
            "one primary row and one Secrets-category row; no per-key reset rows");
        h.DeleteAudit.Single(AuditActionNames.IntegrationDeleted).Fields["settingsRemoved"].Should().Be(2);
        var retired = h.DeleteAudit.Single(AuditActionNames.IntegrationSettingSecretRetired);
        retired.Fields["reason"].Should().Be("integration_deleted");
        retired.Fields["key"].Should().Be(h.SensitiveSettingKey);
    }

    [Fact]
    public async Task The_delete_without_its_audited_unit_refuses_rather_than_delete_unaudited()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var unaudited = h.WithoutDeleteCollaborators();

        var act = async () => await unaudited.DeleteIntegrationAsync(integration.Id);

        await act.Should().ThrowAsync<AuditInvariantException>();
        (await h.Db.Integrations.CountAsync(i => i.Id == integration.Id.Value)).Should().Be(1);
    }

    [Fact]
    public async Task Delete_succeeds_and_cleans_up_integration_scoped_platform_setting_values()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Settings.SetAsync(integration.Id.Value, h.SettingKey,
            new PlatformSettingUpdatePayload { ScalarValue = "x" }, rowVersionBase64: null);

        await h.Integrations.DeleteIntegrationAsync(integration.Id);

        (await h.Db.PlatformSettingValues.CountAsync(
            v => v.ScopeKind == PlatformSettingScopeConstants.IntegrationKind && v.ScopeId == integration.Id.Value))
            .Should().Be(0);
        (await h.Db.Integrations.CountAsync(i => i.Id == integration.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Delete_disables_the_backing_secret_of_a_sensitive_integration_setting()
    {
        // The assertion that matters. Deleting the row alone would leave an ACTIVE secret in ISecretStore
        // with nothing pointing at it, forever. PlatformSettingDefinitionRegistry is explicit that a
        // sensitive setting "retains only an opaque reference", so row count reaching zero proves nothing
        // about the secret.
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Settings.SetAsync(integration.Id.Value, h.SensitiveSettingKey,
            new PlatformSettingUpdatePayload
            {
                SecretOperation = PlatformSettingSecretOperations.Replace,
                SecretValue = "hunter2"
            },
            rowVersionBase64: null);
        var secretId = h.LastProvisionedSecretId;

        await h.Integrations.DeleteIntegrationAsync(integration.Id);

        var descriptor = await h.SecretStore.GetAsync(secretId);
        descriptor!.State.Should().Be(SecretState.Disabled,
            "deleting the row without disposing of the owned secret would silently orphan it");
    }

    [Fact]
    public async Task A_blocked_delete_leaves_setting_values_and_their_secrets_intact()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Settings.SetAsync(integration.Id.Value, h.SensitiveSettingKey,
            new PlatformSettingUpdatePayload
            {
                SecretOperation = PlatformSettingSecretOperations.Replace,
                SecretValue = "hunter2"
            },
            rowVersionBase64: null);
        var secretId = h.LastProvisionedSecretId;
        await h.AddAttentionRoutingAsync(integration.Id.Value);

        var act = async () => await h.Integrations.DeleteIntegrationAsync(integration.Id);
        await act.Should().ThrowAsync<InvalidOperationException>();

        (await h.SecretStore.GetAsync(secretId))!.State.Should().NotBe(SecretState.Disabled,
            "cleanup runs only after every blocker has passed -- a refused delete destroys nothing");
        (await h.Db.PlatformSettingValues.CountAsync(v => v.ScopeId == integration.Id.Value)).Should().BeGreaterThan(0);
    }
}
