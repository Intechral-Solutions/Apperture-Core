using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications;

/// <summary>
/// CTX-3B Plan 6a Task 2. Integration hard-delete blocks on Integration-scoped NotificationDefinitions
/// (an intentional policy: they are independently meaningful operator configuration). This operation is
/// what makes that blocker clearable, so the blocker does not repeat CTX-1's unreachable-archive trap
/// (design gate F4).
/// </summary>
public sealed class NotificationDefinitionDeleteTests
{
    [Fact]
    public async Task User_created_integration_scoped_definition_can_be_deleted()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.CreateUserDefinitionAsync(integration.Id.Value);

        await h.Definitions.DeleteAsync(definition.Id.Value);

        (await h.Db.NotificationDefinitions.CountAsync(d => d.Id == definition.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Platform_built_in_definition_cannot_be_deleted()
    {
        using var h = new NotificationDefinitionTestHarness();
        var seeded = await h.SeedBuiltInDefinitionAsync(NotificationDefinitionOrigins.PlatformBuiltIn);

        var act = async () => await h.Definitions.DeleteAsync(seeded.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*built-in*");
        (await h.Db.NotificationDefinitions.CountAsync(d => d.Id == seeded.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Module_built_in_definition_cannot_be_deleted()
    {
        using var h = new NotificationDefinitionTestHarness();
        var seeded = await h.SeedBuiltInDefinitionAsync(NotificationDefinitionOrigins.ModuleBuiltIn);

        var act = async () => await h.Definitions.DeleteAsync(seeded.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Deleting_a_definition_removes_its_subscriptions_and_user_overrides()
    {
        // Subordinate current-state data, per the inventory's dependency graph: a subscription and a user
        // override are meaningless once their definition is gone, and nothing else can reach them.
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.CreateUserDefinitionAsync(integration.Id.Value);
        await h.AddSubscriptionAsync(definition.Id.Value, integration.Id.Value);
        await h.AddUserOverrideAsync(definition.Id.Value, userId: "user-1");

        await h.Definitions.DeleteAsync(definition.Id.Value);

        (await h.Db.NotificationSubscriptions.CountAsync(s => s.NotificationDefinitionId == definition.Id.Value)).Should().Be(0);
        (await h.Db.NotificationSubscriptionUserOverrides.CountAsync(o => o.NotificationDefinitionId == definition.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Deleting_a_definition_preserves_historical_notification_instances()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.CreateUserDefinitionAsync(integration.Id.Value);
        await h.AddNotificationInstanceAsync(definition.Id.Value, integration.Id.Value);

        await h.Definitions.DeleteAsync(definition.Id.Value);

        var instance = await h.Db.NotificationInstances.AsNoTracking()
            .SingleAsync(i => i.NotificationDefinitionId == definition.Id.Value);
        instance.NotificationDefinitionId.Should().Be(definition.Id.Value,
            "history records what happened; it keeps its dangling reference exactly as CTX-2 established");
    }

    [Fact]
    public async Task Deleting_a_definition_preserves_pre_existing_audit_rows_and_appends_one()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.CreateUserDefinitionAsync(integration.Id.Value);
        var auditBefore = h.Audit.Records.ToList();

        await h.Definitions.DeleteAsync(definition.Id.Value);

        h.Audit.Records.Take(auditBefore.Count).Should().BeEquivalentTo(auditBefore, "audit is append-only");
        h.Audit.Records.Should().HaveCount(auditBefore.Count + 1);
    }

    [Fact]
    public async Task Deleting_a_definition_requires_notification_manage_and_integration_update()
    {
        using var h = new NotificationDefinitionTestHarness(denyIntegrationUpdate: true);
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.CreateUserDefinitionAsync(integration.Id.Value);

        var act = async () => await h.Definitions.DeleteAsync(definition.Id.Value);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "deletion reuses RequireDefinitionAccessAsync(manage: true) -- the same gate as create and update, no new permission");
    }

    [Fact]
    public async Task Deleting_a_missing_definition_is_a_no_op_result_not_a_crash()
    {
        using var h = new NotificationDefinitionTestHarness();

        var deleted = await h.Definitions.DeleteAsync("does-not-exist");

        deleted.Should().BeFalse();
    }
}
