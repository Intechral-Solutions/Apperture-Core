using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications;

/// <summary>
/// CMP-X: the built-in Notification Definition platform-defaults gap was a UI-only limitation --
/// <see cref="NotificationDefinitionService.UpdateAsync"/> and <see cref="SqlNotificationDefinitionStore.UpdateAsync"/>
/// already accept <c>DefaultSeverity</c>/<c>ToastEnabledByDefault</c> changes for
/// <see cref="NotificationDefinitionOrigins.PlatformBuiltIn"/>/<see cref="NotificationDefinitionOrigins.ModuleBuiltIn"/>
/// rows under the existing <c>notification.definition.manage</c> gate. These tests pin that pre-existing
/// server behaviour as a regression guard for the CMP-X UI change, which adds no server-side code.
/// </summary>
public sealed class NotificationDefinitionBuiltInPlatformDefaultsTests
{
    [Fact]
    public async Task Platform_built_in_default_severity_and_toast_default_are_editable_under_manage()
    {
        using var h = new NotificationDefinitionTestHarness();
        var seeded = await h.SeedBuiltInDefinitionAsync(NotificationDefinitionOrigins.PlatformBuiltIn);

        var updated = await h.Definitions.UpdateAsync(
            seeded.Id,
            new NotificationDefinitionUpdateRequest(
                DisplayName: null,
                Description: null,
                RuleJson: null,
                TemplateJson: null,
                IsEnabled: null,
                DefaultSeverity: "critical",
                ToastEnabledByDefault: true));

        updated.Should().NotBeNull();
        updated!.DefaultSeverity.Should().Be("critical");
        updated.ToastEnabledByDefault.Should().BeTrue();

        var fetched = await h.Definitions.GetAsync(seeded.Id);
        fetched!.DefaultSeverity.Should().Be("critical");
        fetched.ToastEnabledByDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Module_built_in_default_severity_and_toast_default_are_editable_under_manage()
    {
        using var h = new NotificationDefinitionTestHarness();
        var seeded = await h.SeedBuiltInDefinitionAsync(NotificationDefinitionOrigins.ModuleBuiltIn);

        var updated = await h.Definitions.UpdateAsync(
            seeded.Id,
            new NotificationDefinitionUpdateRequest(
                DisplayName: null,
                Description: null,
                RuleJson: null,
                TemplateJson: null,
                IsEnabled: null,
                DefaultSeverity: "info",
                ToastEnabledByDefault: false));

        updated.Should().NotBeNull();
        updated!.DefaultSeverity.Should().Be("info");
        updated.ToastEnabledByDefault.Should().BeFalse();
    }

    [Theory]
    [InlineData(NotificationDefinitionOrigins.PlatformBuiltIn)]
    [InlineData(NotificationDefinitionOrigins.ModuleBuiltIn)]
    public async Task Built_in_rule_and_template_remain_immutable_through_the_same_update_path(string origin)
    {
        using var h = new NotificationDefinitionTestHarness();
        var seeded = await h.SeedBuiltInDefinitionAsync(origin);

        var ruleAct = async () => await h.Definitions.UpdateAsync(
            seeded.Id,
            new NotificationDefinitionUpdateRequest(null, null, "{}", null, null, null, null));
        await ruleAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable*");

        var templateAct = async () => await h.Definitions.UpdateAsync(
            seeded.Id,
            new NotificationDefinitionUpdateRequest(null, null, null, "{}", null, null, null));
        await templateAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable*");
    }

    [Fact]
    public async Task Built_in_delete_remains_refused_alongside_the_editable_defaults()
    {
        using var h = new NotificationDefinitionTestHarness();
        var seeded = await h.SeedBuiltInDefinitionAsync(NotificationDefinitionOrigins.PlatformBuiltIn);

        var act = async () => await h.Definitions.DeleteAsync(seeded.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*built-in*");
    }

    [Fact]
    public async Task Caller_without_notification_definition_manage_cannot_update_built_in_platform_defaults()
    {
        using var h = new NotificationDefinitionTestHarness(denyInstanceManage: true);
        var seeded = await h.SeedBuiltInDefinitionAsync(NotificationDefinitionOrigins.PlatformBuiltIn);

        var act = async () => await h.Definitions.UpdateAsync(
            seeded.Id,
            new NotificationDefinitionUpdateRequest(null, null, null, null, null, "critical", true));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task User_created_definition_keeps_its_existing_editing_behavior()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.CreateUserDefinitionAsync(integration.Id.Value);

        var updated = await h.Definitions.UpdateAsync(
            definition.Id.Value,
            new NotificationDefinitionUpdateRequest(null, null, null, null, null, "error", true));

        updated!.DefaultSeverity.Should().Be("error");
        updated.ToastEnabledByDefault.Should().BeTrue();
        updated.Origin.Should().Be(NotificationDefinitionOrigins.UserCreated);
    }

    [Fact]
    public async Task Updating_toast_default_does_not_create_or_change_personal_notification_user_preference_rows()
    {
        using var h = new NotificationDefinitionTestHarness();
        var seeded = await h.SeedBuiltInDefinitionAsync(NotificationDefinitionOrigins.PlatformBuiltIn);
        await h.AddUserPreferenceAsync(seeded.Id, userId: "user-1", channel: NotificationChannels.Toast);
        var before = await h.Db.NotificationUserPreferences
            .Where(p => p.NotificationDefinitionId == seeded.Id)
            .AsNoTracking()
            .ToListAsync();

        await h.Definitions.UpdateAsync(
            seeded.Id,
            new NotificationDefinitionUpdateRequest(null, null, null, null, null, null, ToastEnabledByDefault: true));

        var after = await h.Db.NotificationUserPreferences
            .Where(p => p.NotificationDefinitionId == seeded.Id)
            .AsNoTracking()
            .ToListAsync();

        after.Should().HaveCount(1);
        after.Single().Id.Should().Be(before.Single().Id);
        after.Single().IsEnabled.Should().Be(before.Single().IsEnabled,
            "the definition-level ToastEnabledByDefault update must never write to a personal preference row");
    }
}
