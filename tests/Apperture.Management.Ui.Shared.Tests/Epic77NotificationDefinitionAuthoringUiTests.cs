using System.Text.RegularExpressions;
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

    /// <summary>
    /// True when the element containing <c>@bind="{bindTarget}"</c> also carries
    /// <c>@bind:after="MarkDirty"</c> in the same opening tag, regardless of attribute
    /// ordering or exact whitespace between attributes.
    /// </summary>
    private static bool BindsWithMarkDirty(string source, string bindTarget)
    {
        var pattern = "<[^<>]*?@bind=\"" + Regex.Escape(bindTarget) + "\"[^<>]*?>";
        var match = Regex.Match(source, pattern, RegexOptions.Singleline);
        return match.Success && match.Value.Contains("@bind:after=\"MarkDirty\"");
    }

    [Fact]
    public void Validation_relevant_fields_mark_dirty_on_edit()
    {
        var root = RepoRoot();
        var form = File.ReadAllText(Path.Combine(
            root,
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/Notifications/NotificationDefinitionAuthoringForm.razor"));

        // Fields that feed BuildRuleJson/BuildTemplateJson (and therefore the
        // Validate payload sent to ValidateNotificationDefinitionAsync) must
        // invalidate a prior successful Validate when edited.
        string[] validationRelevantFields =
        [
            "_model.UseAdvancedRuleJson",
            "_model.AdvancedRuleJson",
            "_model.IndexSeverityOperator",
            "_model.IndexSeverityValue",
            "_model.IndexModuleId",
            "row.DataField",
            "row.Operator",
            "_model.UseAdvancedTemplateJson",
            "_model.AdvancedTemplateJson",
            "_model.Subject",
            "_model.Summary",
            "_model.BodyText",
            "_model.ActionText",
            "_model.ActionUrl",
        ];

        foreach (var field in validationRelevantFields)
        {
            BindsWithMarkDirty(form, field).Should().BeTrue(
                $"@bind=\"{field}\" feeds BuildRuleJson/BuildTemplateJson and must call MarkDirty() on change");
        }

        // PropertyFilters and ContentFields both bind a "row.Value" field (distinct rows,
        // same identifier) so the regex above only finds the first occurrence — assert the
        // element count separately: two "row.Value" bindings, both with MarkDirty.
        var rowValueMatches = Regex.Matches(form, "<[^<>]*?@bind=\"row\\.Value\"[^<>]*?>", RegexOptions.Singleline);
        rowValueMatches.Count.Should().Be(2, "PropertyFilters and ContentFields each bind a row.Value field");
        foreach (Match m in rowValueMatches)
            m.Value.Should().Contain("@bind:after=\"MarkDirty\"");

        BindsWithMarkDirty(form, "row.Label").Should().BeTrue(
            "ContentFields row.Label feeds BuildTemplateJson and must call MarkDirty() on change");

        // Fields that do NOT feed BuildRuleJson/BuildTemplateJson (and therefore do not
        // affect the Validate payload) must not be wired to MarkDirty — editing them alone
        // should not force re-validation before Create/Save is re-enabled.
        string[] nonValidationFields = ["_model.Description", "_model.DefaultSeverity", "_model.IsEnabled"];
        foreach (var field in nonValidationFields)
        {
            BindsWithMarkDirty(form, field).Should().BeFalse(
                $"@bind=\"{field}\" does not feed the Validate payload and must not force stale-validation invalidation");
        }
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
