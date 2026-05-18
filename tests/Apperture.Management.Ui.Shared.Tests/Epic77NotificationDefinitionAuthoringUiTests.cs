using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic77NotificationDefinitionAuthoringUiTests
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

    [Fact]
    public void Create_routes_and_authoring_form_exist()
    {
        var root = RepoRoot();
        var configCreate = File.ReadAllText(Path.Combine(
            root,
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDefinitionCreate.razor"));
        var integrationCreate = File.ReadAllText(Path.Combine(
            root,
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationNotificationDefinitionCreate.razor"));
        var edit = File.ReadAllText(Path.Combine(
            root,
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDefinitionEdit.razor"));
        var form = File.ReadAllText(Path.Combine(
            root,
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/Notifications/NotificationDefinitionAuthoringForm.razor"));

        configCreate.Should().Contain("@page \"/config/notifications/definitions/new\"");
        integrationCreate.Should().Contain("@page \"/integrations/{IntegrationId}/notifications/definitions/new\"");
        integrationCreate.Should().Contain("LockIntegration=\"true\"");
        edit.Should().Contain("@page \"/config/notifications/definitions/{DefinitionId}/edit\"");
        edit.Should().Contain("fullyEditable");
        form.Should().Contain("ValidateNotificationDefinitionAsync");
        form.Should().Contain("RenderNotificationPreviewDraftAsync");
        form.Should().Contain("disabled=\"@(_busy || !CanSave || !CanSubmit)\"");
    }

    [Fact]
    public void Definitions_list_and_detail_show_create_edit_and_locked_callout()
    {
        var root = RepoRoot();
        var list = File.ReadAllText(Path.Combine(
            root,
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDefinitionsList.razor"));
        var detail = File.ReadAllText(Path.Combine(
            root,
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDefinitionDetail.razor"));
        var integration = File.ReadAllText(Path.Combine(
            root,
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationNotifications.razor"));

        list.Should().Contain("Create definition");
        list.Should().Contain("CanManageNotificationDefinitions && caps.CanUpdateIntegrations");
        detail.Should().Contain("Rule and template are locked");
        detail.Should().Contain("Edit definition");
        detail.Should().Contain("_canManageDefinitions");
        integration.Should().Contain("notifications/definitions/new");
        integration.Should().Contain("NotificationUiRoutes.DefinitionEdit");
    }

    [Fact]
    public void Catalog_api_and_client_methods_exist()
    {
        var root = RepoRoot();
        var controller = File.ReadAllText(Path.Combine(
            root,
            "src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/PlatformEventsCatalogController.cs"));
        var client = File.ReadAllText(Path.Combine(
            root,
            "src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs"));

        controller.Should().Contain("api/platform-events/catalog");
        client.Should().Contain("ListPlatformEventCatalogAsync");
        client.Should().Contain("CreateNotificationDefinitionAsync");
        client.Should().Contain("notification-definitions/render-preview");
    }
}
