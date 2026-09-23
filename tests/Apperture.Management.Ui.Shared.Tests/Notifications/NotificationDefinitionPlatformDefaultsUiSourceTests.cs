using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests.Notifications;

/// <summary>
/// CMP-X. Razor source-text assertions, matching this suite's established convention (see
/// <see cref="NotificationDefinitionDeleteUiSourceTests"/> / MovementUiSourceTests). There is no bUnit
/// in this repository; component rendering is not available, so the contract is pinned at the markup
/// level. Covers the "Platform defaults" section that exposes the already-server-supported
/// <c>DefaultSeverity</c>/<c>ToastEnabledByDefault</c> fields for built-in Notification Definitions,
/// under the existing <c>notification.definition.manage</c> gate, without touching rule/template
/// immutability or duplicating the authoring form's existing controls for user-created definitions.
/// </summary>
public sealed class NotificationDefinitionPlatformDefaultsUiSourceTests
{
    private const string Folder = "Pages/Config/Notifications";
    private const string TargetFile = "NotificationDefinitionDetail.razor";

    [Fact]
    public void Platform_defaults_section_exists_and_is_gated_behind_showPlatformDefaults()
    {
        var content = ReadRazor(Folder, TargetFile);
        content.Should().Contain("id=\"notification-definition-platform-defaults\"");

        var idIndex = content.IndexOf("id=\"notification-definition-platform-defaults\"", StringComparison.Ordinal);
        idIndex.Should().BeGreaterThanOrEqualTo(0);

        var guardIndex = content.LastIndexOf("@if (_showPlatformDefaults)", idIndex, StringComparison.Ordinal);
        guardIndex.Should().BeGreaterThanOrEqualTo(0, "the platform defaults section must be gated behind _showPlatformDefaults");
    }

    [Fact]
    public void ShowPlatformDefaults_checks_origin_for_built_in_values_only()
    {
        var content = ReadRazor(Folder, TargetFile);
        var body = ExtractPropertyBody(content, "_showPlatformDefaults");

        body.Should().Contain("_detail.Origin");
        body.Should().Contain("\"platformBuiltIn\"");
        body.Should().Contain("\"moduleBuiltIn\"");
        body.Should().NotContain("\"userCreated\"",
            "eligibility is a positive built-in check, not a negated userCreated check, matching the house Origin-predicate convention");
        body.Should().NotContain("Mutability",
            "platform defaults are gated on Origin, not on the fullyEditable Mutability gate that _canEditDefinition/_isRuleTemplateLocked use");
    }

    [Fact]
    public void Platform_defaults_section_does_not_expose_rule_or_template_editing()
    {
        var content = ReadRazor(Folder, TargetFile);
        var section = ExtractPlatformDefaultsSectionMarkup(content);

        section.Should().NotContain("RuleJson");
        section.Should().NotContain("TemplateJson");
    }

    [Fact]
    public void Default_severity_control_is_gated_on_canManage_and_calls_the_update_handler()
    {
        var content = ReadRazor(Folder, TargetFile);
        var section = ExtractPlatformDefaultsSectionMarkup(content);

        section.Should().Contain("id=\"platform-default-severity\"");
        section.Should().Contain("@onchange=\"SetPlatformDefaultSeverityAsync\"");
        section.Should().Contain("_canManage");
    }

    [Fact]
    public void Toast_default_control_is_gated_on_canManage_and_calls_the_update_handler()
    {
        var content = ReadRazor(Folder, TargetFile);
        var section = ExtractPlatformDefaultsSectionMarkup(content);

        section.Should().Contain("id=\"platform-toast-default\"");
        section.Should().Contain("@onchange=\"SetToastEnabledByDefaultAsync\"");
        section.Should().Contain("_canManage");
    }

    [Fact]
    public void SetPlatformDefaultSeverityAsync_requires_canManage_updates_via_client_and_reassigns_detail_from_the_response()
    {
        var content = ReadRazor(Folder, TargetFile);
        var body = ExtractMethodBody(content, "SetPlatformDefaultSeverityAsync");

        body.Should().Contain("if (!_canManage");
        body.Should().Contain("Client.UpdateNotificationDefinitionAsync(");
        body.Should().Contain("_detail = await Client.UpdateNotificationDefinitionAsync(",
            "the definition must be reloaded from the server DTO after mutation, not patched locally");
    }

    [Fact]
    public void SetToastEnabledByDefaultAsync_requires_canManage_updates_via_client_and_reassigns_detail_from_the_response()
    {
        var content = ReadRazor(Folder, TargetFile);
        var body = ExtractMethodBody(content, "SetToastEnabledByDefaultAsync");

        body.Should().Contain("if (!_canManage");
        body.Should().Contain("Client.UpdateNotificationDefinitionAsync(");
        body.Should().Contain("_detail = await Client.UpdateNotificationDefinitionAsync(",
            "the definition must be reloaded from the server DTO after mutation, not patched locally");
    }

    [Fact]
    public void Platform_defaults_handlers_introduce_no_new_permission_or_capability_field()
    {
        var content = ReadRazor(Folder, TargetFile);
        var severityBody = ExtractMethodBody(content, "SetPlatformDefaultSeverityAsync");
        var toastBody = ExtractMethodBody(content, "SetToastEnabledByDefaultAsync");

        // No new capability flag is checked; both handlers reuse _canManage, which is already computed
        // in LoadAsync from CanManageNotificationDefinitions plus the existing Integration gate.
        severityBody.Should().NotContain("Capabilities.");
        toastBody.Should().NotContain("Capabilities.");
    }

    /// <summary>
    /// Extracts the platform-defaults section's markup, from its @if guard through the matching closing
    /// brace, via depth-aware Razor-block scanning (mirrors the sibling suite's method-body extraction),
    /// so assertions about its contents don't false-positive against unrelated markup elsewhere in this
    /// large page.
    /// </summary>
    private static string ExtractPlatformDefaultsSectionMarkup(string content)
    {
        const string guard = "@if (_showPlatformDefaults)";
        var guardIndex = content.IndexOf(guard, StringComparison.Ordinal);
        guardIndex.Should().BeGreaterThanOrEqualTo(0, "expected the _showPlatformDefaults guard to exist");

        var braceStart = content.IndexOf('{', guardIndex);
        braceStart.Should().BeGreaterThanOrEqualTo(0);

        var depth = 0;
        var i = braceStart;
        for (; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0) { i++; break; }
            }
        }

        return content[guardIndex..i];
    }

    private static string ExtractPropertyBody(string content, string propertyName)
    {
        var nameIndex = content.IndexOf(propertyName + " =>", StringComparison.Ordinal);
        nameIndex.Should().BeGreaterThanOrEqualTo(0, $"expected to find a computed property named {propertyName}");

        var semicolonIndex = content.IndexOf(';', nameIndex);
        semicolonIndex.Should().BeGreaterThanOrEqualTo(0, $"expected a terminating ; for {propertyName}");

        return content[nameIndex..(semicolonIndex + 1)];
    }

    private static string ExtractMethodBody(string content, string methodName)
    {
        var declaration = methodName + "(";
        var nameIndex = content.IndexOf(declaration, StringComparison.Ordinal);
        nameIndex.Should().BeGreaterThanOrEqualTo(0, $"expected to find a method declared as {methodName}(...)");

        var braceStart = content.IndexOf('{', nameIndex);
        braceStart.Should().BeGreaterThanOrEqualTo(0, $"expected an opening brace after {methodName}");

        var depth = 0;
        var i = braceStart;
        for (; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0) { i++; break; }
            }
        }

        return content[nameIndex..i];
    }

    private static string ReadRazor(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var path = Path.Combine(dir!.FullName, "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client",
            Path.Combine(folder.Split('/')), file);
        File.Exists(path).Should().BeTrue($"expected {path} to exist");
        return File.ReadAllText(path);
    }
}
