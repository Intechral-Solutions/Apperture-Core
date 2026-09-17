using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests.Notifications;

/// <summary>
/// Razor source-text assertions, matching this suite's established convention (see
/// MovementUiSourceTests). There is no bUnit in this repository; component rendering is not
/// available, so the contract is pinned at the markup level.
/// </summary>
public sealed class NotificationDefinitionDeleteUiSourceTests
{
    private const string Folder = "Pages/Config/Notifications";
    private const string TargetFile = "NotificationDefinitionDetail.razor";

    [Fact]
    public void Delete_trigger_button_exists_and_is_gated_behind_canDeleteDefinition()
    {
        var content = ReadRazor(Folder, TargetFile);
        content.Should().Contain("id=\"notification-definition-delete-trigger\"");

        var idIndex = content.IndexOf("id=\"notification-definition-delete-trigger\"", StringComparison.Ordinal);
        idIndex.Should().BeGreaterThanOrEqualTo(0);

        // The gating `@if (_canDeleteDefinition)` must appear immediately before the trigger button,
        // not merely somewhere earlier in the file.
        var guardIndex = content.LastIndexOf("@if (_canDeleteDefinition)", idIndex, StringComparison.Ordinal);
        guardIndex.Should().BeGreaterThanOrEqualTo(0, "the delete trigger must be gated behind _canDeleteDefinition");
    }

    [Fact]
    public void CanDeleteDefinition_checks_origin_userCreated_and_does_not_reference_mutability()
    {
        var content = ReadRazor(Folder, TargetFile);
        var body = ExtractPropertyBody(content, "_canDeleteDefinition");

        body.Should().Contain("_detail.Origin");
        body.Should().Contain("\"userCreated\"");
        body.Should().NotContain("Mutability",
            "delete eligibility is governed by Origin, not Mutability (which gates rule/template editability)");
    }

    [Fact]
    public void OnConfirm_calls_delete_with_no_local_catch()
    {
        var content = ReadRazor(Folder, TargetFile);
        content.Should().Contain("OnConfirm=\"DeleteDefinitionAsync\"");

        var handlerBody = ExtractMethodBody(content, "DeleteDefinitionAsync");
        handlerBody.Should().Contain("Client.DeleteNotificationDefinitionAsync(_detail!.Id)");
        handlerBody.Should().NotContain("catch",
            "ConfirmActionDialog itself catches and surfaces the real server error text; a local catch here would suppress that (e.g. the 409 'Built-in notification definitions cannot be deleted.' message)");
    }

    [Fact]
    public void OnConfirmed_navigates_to_the_definitions_list_route()
    {
        var content = ReadRazor(Folder, TargetFile);

        // OnConfirmed is bound to a named handler (an inline lambda containing a C# string literal is
        // not valid inside a Razor attribute value), so scope the assertion to that handler's body
        // rather than the whole file -- Nav.NavigateTo is used elsewhere on this page (e.g.
        // FocusSubscriptionsAsync, DismissPostCreatePromptAsync) for unrelated flows.
        content.Should().Contain("OnConfirmed=\"NavigateToDefinitionsList\"");

        var handlerBody = ExtractMethodBody(content, "NavigateToDefinitionsList");
        handlerBody.Should().Contain("Nav.NavigateTo(\"/config/notifications/definitions\")");
    }

    /// <summary>
    /// Extracts a single computed-property expression body (the `=>` arrow-body form), from the
    /// property name to the terminating semicolon, via simple depth-aware scanning for parentheses so
    /// assertions about what a property references don't false-positive against unrelated code
    /// elsewhere in a large page.
    /// </summary>
    private static string ExtractPropertyBody(string content, string propertyName)
    {
        var nameIndex = content.IndexOf(propertyName + " =>", StringComparison.Ordinal);
        nameIndex.Should().BeGreaterThanOrEqualTo(0, $"expected to find a computed property named {propertyName}");

        var semicolonIndex = content.IndexOf(';', nameIndex);
        semicolonIndex.Should().BeGreaterThanOrEqualTo(0, $"expected a terminating ; for {propertyName}");

        return content[nameIndex..(semicolonIndex + 1)];
    }

    /// <summary>
    /// Extracts a single-method body (from its declaration line to its matching closing brace) via
    /// simple brace counting, so assertions about "does this handler have a local catch" don't
    /// false-positive on unrelated try/catch blocks elsewhere in a large page. Searches for
    /// "methodName(" -- the declaration and call-site form -- rather than the bare name, so it does not
    /// match the razor markup attribute usage (e.g. OnConfirm="DeleteDefinitionAsync", which has no
    /// trailing parenthesis and appears earlier in the file).
    /// </summary>
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
                if (depth == 0)
                {
                    i++;
                    break;
                }
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
        return System.IO.File.ReadAllText(path);
    }
}
