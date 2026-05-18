using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic77NotificationWorkflowUiTests
{
    private static string ReadRepoFile(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return File.ReadAllText(Path.Combine(dir, relativePath));
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    [Fact]
    public void Definition_detail_has_inline_subscription_management()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDefinitionDetail.razor");
        content.Should().Contain("Explicit subscriptions");
        content.Should().Contain("AddSubscriptionAsync");
        content.Should().Contain("PatchSubscriptionDeliveryModeAsync");
        content.Should().Contain("RemoveSubscriptionAsync");
        content.Should().Contain("NotificationDeliveryModeOptions.All");
    }

    [Fact]
    public void Read_only_users_see_preview_without_add_controls()
    {
        var detail = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDefinitionDetail.razor");
        detail.Should().Contain("@if (_canManage)");
        detail.Should().Contain("ResolveRecipientsAsync");
        detail.Should().NotContain("Add subscription");
    }

    [Fact]
    public void Subscription_changes_refresh_recipient_preview()
    {
        var detail = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDefinitionDetail.razor");
        var integration = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationNotifications.razor");
        detail.Should().Contain("_recipientPreviewRefreshedAt");
        detail.Should().Contain("await ResolveRecipientsAsync();");
        integration.Should().Contain("_recipientPreviewRefreshedAt");
        integration.Should().Contain("Last refreshed");
    }

    [Fact]
    public void Instance_detail_includes_definition_and_integration_links()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationInstanceDetail.razor");
        content.Should().Contain("NotificationUiRoutes.DefinitionDetail");
        content.Should().Contain("NotificationUiRoutes.IntegrationNotifications");
        content.Should().Contain("NotificationUiRoutes.IntegrationScheduleDiagnostics");
        content.Should().Contain("TemplateVersionHash");
    }

    [Fact]
    public void Recipient_snapshot_groups_render()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/Notifications/NotificationRecipientSnapshotDisplay.razor");
        content.Should().Contain("Implicit (attention routing compatibility)");
        content.Should().Contain("Explicit subscriptions and team membership");
        content.Should().Contain("NotificationRecipientSnapshotParser");
    }

    [Fact]
    public void Delivery_row_links_to_instance_detail()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDelivery.razor");
        content.Should().Contain("NotificationUiRoutes.InstanceDetail");
        content.Should().Contain("Delivery is disabled");
        content.Should().Contain("Failed deliveries");
        content.Should().Contain("Succeeded (recent window)");
    }

    [Fact]
    public void Integration_notifications_matches_definition_detail_subscription_features()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationNotifications.razor");
        content.Should().Contain("PatchSubscriptionDeliveryModeAsync");
        content.Should().Contain("GetDefinitionDisplayName");
        content.Should().Contain("NotificationDefinitionPostCreatePrompt");
    }
}
