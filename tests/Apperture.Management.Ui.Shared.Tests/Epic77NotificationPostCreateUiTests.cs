using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic77NotificationPostCreateUiTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    [Fact]
    public void Integration_create_navigates_with_post_create_query()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationNotificationDefinitionCreate.razor");
        content.Should().Contain("NotificationPostCreateNavigation.IntegrationNotificationsAfterCreate");
    }

    [Fact]
    public void Config_create_navigates_to_detail_with_post_create_query()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDefinitionCreate.razor");
        content.Should().Contain("NotificationPostCreateNavigation.DefinitionDetailAfterCreate");
    }

    [Fact]
    public void Integration_notifications_shows_post_create_subscription_prompt()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationNotifications.razor");
        content.Should().Contain("NotificationDefinitionPostCreatePrompt");
        ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/Notifications/NotificationDefinitionPostCreatePrompt.razor")
            .Should().Contain("Add recipients so this notification can deliver");
        content.Should().Contain("NotificationPostCreateNavigation.CreatedDefinitionIdQuery");
        content.Should().Contain("id=\"explicit-subscriptions\"");
    }

    [Fact]
    public void Config_detail_links_to_integration_notifications_when_integration_scoped()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDefinitionDetail.razor");
        content.Should().Contain("PostCreateIntegrationNotificationsHref");
        ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/Notifications/NotificationDefinitionPostCreatePrompt.razor")
            .Should().Contain("Open integration notifications");
    }

    [Fact]
    public void Add_subscription_refreshes_recipient_preview()
    {
        var integration = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationNotifications.razor");
        var detail = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDefinitionDetail.razor");

        integration.Should().Contain("AddSubscriptionAsync");
        integration.Should().Contain("await ResolveRecipientsAsync();");
        detail.Should().Contain("AddSubscriptionAsync");
        detail.Should().Contain("await ResolveRecipientsAsync();");
    }

    [Fact]
    public void Empty_state_shows_no_explicit_subscriptions_yet()
    {
        var integration = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationNotifications.razor");
        var detail = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDefinitionDetail.razor");

        integration.Should().Contain("No explicit subscriptions yet");
        detail.Should().Contain("No explicit subscriptions yet");
    }

    [Fact]
    public void Read_only_users_do_not_see_add_subscription_on_post_create_prompt()
    {
        var prompt = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/Notifications/NotificationDefinitionPostCreatePrompt.razor");
        prompt.Should().Contain("@if (CanManageSubscriptions)");
        prompt.Should().Contain("Add subscription");
        prompt.Should().Contain("Preview recipients");
    }

    [Fact]
    public void Recipient_resolution_display_labels_implicit_and_explicit()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/Notifications/NotificationRecipientResolutionDisplay.razor");
        content.Should().Contain("Implicit (attention routing compatibility)");
        content.Should().Contain("Explicit subscriptions and team membership");
        content.Should().Contain("NotificationRecipientSourceLabels");
    }

    [Fact]
    public void Post_create_navigation_builds_expected_urls()
    {
        var integrationUrl = NotificationPostCreateNavigation.IntegrationNotificationsAfterCreate("int-1", "def-1");
        integrationUrl.Should().Contain("/integrations/int-1/notifications?");
        integrationUrl.Should().Contain("createdDefinitionId=def-1");

        var detailUrl = NotificationPostCreateNavigation.DefinitionDetailAfterCreate("def-1", "int-1");
        detailUrl.Should().Contain("/config/notifications/definitions/def-1");
        detailUrl.Should().Contain("createdDefinitionId=def-1");
        detailUrl.Should().Contain("integrationId=int-1");
    }

    [Fact]
    public void Recipient_source_labels_distinguish_implicit_attention_routing()
    {
        NotificationRecipientSourceLabels.IsImplicitAttentionRouting("attentionRoutingIntegrationOverride").Should().BeTrue();
        NotificationRecipientSourceLabels.IsImplicitAttentionRouting("directSubscription").Should().BeFalse();
        NotificationRecipientSourceLabels.Label("directSubscription").Should().Contain("Explicit");
    }
}
