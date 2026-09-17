using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests.ResourceContexts;

/// <summary>
/// Razor source-text assertions, matching this suite's established convention (see
/// Epic96IntegrationActionFeedbackTests). There is no bUnit in this repository; component rendering is not
/// available, so the contract is pinned at the markup level.
/// </summary>
public sealed class MovementUiSourceTests
{
    [Fact]
    public void Move_dialog_disables_submit_unless_the_model_says_it_can_submit()
    {
        ReadRazor("Organisms", "MoveOwnershipDialog.razor")
            .Should().Contain("disabled=\"@(!_model.CanSubmit)\"");
    }

    [Fact]
    public void Move_dialog_uses_mirrored_blocker_code_constants_not_string_literals()
    {
        var content = ReadRazor("Organisms", "MoveOwnershipDialog.razor");
        content.Should().Contain("ResourceMoveBlockerCodes.ScheduleAttachedToIntegration");
        content.Should().NotContain("\"schedule_attached_to_integration\"",
            "blocker codes are mirrored constants, never literals");
    }

    [Fact]
    public void Move_dialog_injects_the_view_state_rather_than_taking_it_as_a_parameter()
    {
        var content = ReadRazor("Organisms", "MoveOwnershipDialog.razor");
        content.Should().Contain("@inject ResourceContextViewState ViewState");
        content.Should().NotContain("[Parameter] public ResourceContextViewState");
    }

    [Theory]
    [InlineData("Organisms", "MoveOwnershipDialog.razor")]
    [InlineData("Pages/Integrations", "IntegrationDetail.razor")]
    [InlineData("Pages/Connections", "ConnectionDetail.razor")]
    [InlineData("Pages/Schedules", "ScheduleDetail.razor")]
    public void Movement_ui_never_switches_the_shell_view(string folder, string file)
    {
        ReadRazor(folder, file).Should().NotContain("SetExplicitAsync",
            "opening or completing a move is not choosing a workspace -- the shell View must never change by itself (§4.6, §8.9)");
    }

    [Theory]
    [InlineData("Organisms", "MoveOwnershipDialog.razor")]
    [InlineData("Pages/Integrations", "IntegrationDetail.razor")]
    [InlineData("Pages/Connections", "ConnectionDetail.razor")]
    [InlineData("Pages/Schedules", "ScheduleDetail.razor")]
    [InlineData("Pages/Config/Applications", "ApplicationDetail.razor")]
    public void Operator_copy_says_context_not_application(string folder, string file)
    {
        var content = ReadRazor(folder, file);
        content.Should().NotContain("Move to another application",
            "the target may be Shared as well as an Application, so operator copy says Context");
    }

    [Fact]
    public void Attached_schedule_shows_a_disabled_action_and_a_link_instead_of_a_dialog()
    {
        var content = ReadRazor("Pages/Schedules", "ScheduleDetail.razor");
        content.Should().Contain("This schedule moves with its integration.");
        content.Should().Contain("Go to integration");
    }

    [Fact]
    public void Integration_detail_refreshes_in_place_after_a_move_and_does_not_navigate()
    {
        var content = ReadRazor("Pages/Integrations", "IntegrationDetail.razor");
        content.Should().Contain("OnMoved=\"OnMovedAsync\"");
        content.Should().Contain("await RefreshAsync();");

        // Scoped to the new handler's body: IntegrationDetail.razor legitimately calls Nav.NavigateTo
        // elsewhere (e.g. after delete), so a whole-file assertion would either false-positive against
        // that unrelated flow or (since the injected field is aliased "Nav", not "NavigationManager")
        // silently fail to test anything at all. Extracting the method body keeps this assertion honest.
        var onMovedBody = ExtractMethodBody(content, "OnMovedAsync");
        onMovedBody.Should().NotContain("Nav.NavigateTo", "the page stays put after a successful move");
        onMovedBody.Should().NotContain("NavigationManager.NavigateTo", "the page stays put after a successful move");
    }

    [Fact]
    public void Connection_detail_reloads_in_place_after_a_move_and_preserves_used_in()
    {
        var content = ReadRazor("Pages/Connections", "ConnectionDetail.razor");
        content.Should().Contain("OnMoved=\"OnMovedAsync\"");
        content.Should().Contain("await LoadAsync();",
            "ConnectionDetail has no RefreshAsync wrapper -- the Refresh toolbar button calls LoadAsync directly");

        var onMovedBody = ExtractMethodBody(content, "OnMovedAsync");
        onMovedBody.Should().NotContain("Nav.NavigateTo", "the page stays put after a successful move");
        onMovedBody.Should().NotContain("NavigationManager.NavigateTo", "the page stays put after a successful move");

        // §8.5: the blast radius (existing bindings) must always be visible, unaffected by this task.
        content.Should().Contain("Used in");
        content.Should().Contain("_connection.UsedIn");
    }

    [Fact]
    public void Schedule_detail_refreshes_in_place_after_a_move_and_does_not_navigate()
    {
        var content = ReadRazor("Pages/Schedules", "ScheduleDetail.razor");
        content.Should().Contain("OnMoved=\"OnMovedAsync\"");
        content.Should().Contain("await RefreshAsync();");
        content.Should().Contain("@inject IToastService Toasts");

        var onMovedBody = ExtractMethodBody(content, "OnMovedAsync");
        onMovedBody.Should().NotContain("Nav.NavigateTo", "the page stays put after a successful move");
        onMovedBody.Should().NotContain("NavigationManager.NavigateTo", "the page stays put after a successful move");
    }

    /// <summary>
    /// Extracts a single-method body (from its declaration line to its matching closing brace) via
    /// simple brace counting, so assertions about "does this handler navigate" don't false-positive on
    /// unrelated navigation elsewhere in a large page. Searches for "methodName(" -- the declaration and
    /// call-site form -- rather than the bare name, so it does not match the razor markup attribute usage
    /// (e.g. OnMoved="OnMovedAsync", which has no trailing parenthesis and appears earlier in the file).
    /// </summary>
    private static string ExtractMethodBody(string content, string methodName)
    {
        var declaration = methodName + "()";
        var nameIndex = content.IndexOf(declaration, StringComparison.Ordinal);
        nameIndex.Should().BeGreaterThanOrEqualTo(0, $"expected to find a method declared as {declaration}");

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
        return File.ReadAllText(path);
    }
}
