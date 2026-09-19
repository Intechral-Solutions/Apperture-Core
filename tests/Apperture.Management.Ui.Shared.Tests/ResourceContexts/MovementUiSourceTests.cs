using System.Text.RegularExpressions;
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

    /// <summary>
    /// HSK-B moved every code-to-copy decision out of the razor and into <c>MoveOwnershipPresenter</c>, so
    /// the original razor-scoped assertion is retargeted with the same intent: the presenter switches on
    /// the mirrored constants, and the razor contains no blocker/effect code literal at all (it renders
    /// only what the presenter returned).
    /// </summary>
    [Fact]
    public void Move_presenter_uses_mirrored_blocker_code_constants_not_string_literals()
    {
        var presenter = ReadShared("MoveOwnershipPresenter.cs");
        presenter.Should().Contain("ResourceMoveBlockerCodes.ScheduleAttachedToIntegration");
        presenter.Should().NotContain("\"schedule_attached_to_integration\"",
            "blocker codes are mirrored constants, never literals");

        var razor = ReadRazor("Organisms", "MoveOwnershipDialog.razor");
        foreach (var code in MoveCodeLiterals)
        {
            razor.Should().NotContain($"\"{code}\"",
                "the dialog renders presenter output and never compares codes itself");
        }
    }

    /// <summary>Every blocker and effect code the server can emit, as a raw literal.</summary>
    private static readonly string[] MoveCodeLiterals =
    [
        "target_not_found",
        "target_archived",
        "already_in_target",
        "binding_connection_unreachable",
        "schedule_context_inconsistent",
        "binding_integration_unreachable",
        "unowned_binding_requires_shared",
        "schedule_attached_to_integration",
        "schedules_move_with_integration",
        "binding_references_missing_connection",
        "binding_references_missing_integration",
        "used_by_integration"
    ];

    [Fact]
    public void Move_dialog_renders_presenter_output_rather_than_raw_dto_fields()
    {
        var content = ReadRazor("Organisms", "MoveOwnershipDialog.razor");
        content.Should().Contain("MoveOwnershipPresenter",
            "all operator copy for a preview comes from the presenter");
        content.Should().NotContain("blocker.Message",
            "server blocker messages embed raw ids; the presenter decides what is safe to show");
        content.Should().NotContain("effect.Message",
            "server effect messages are only a presenter fallback");
        content.Should().NotContain("RelatedResourceContextId",
            "context ids never reach the markup; names come from DescribeContext via the presenter");
    }

    [Fact]
    public void Move_dialog_target_select_uses_the_shared_input_primitive_and_is_labelled()
    {
        var content = ReadRazor("Organisms", "MoveOwnershipDialog.razor");
        content.Should().Contain("id=\"move-target-select\"");
        content.Should().Contain("class=\"ui-input\"",
            "the target select was the only select in the app without the shared input class -- that was the dark-theme bug");
        content.Should().Contain("for=\"move-target-select\"",
            "the select must keep an explicit label association");
    }

    [Fact]
    public void Move_dialog_disables_the_target_select_while_a_move_is_in_flight()
    {
        ReadRazor("Organisms", "MoveOwnershipDialog.razor")
            .Should().Contain("disabled=\"@_model.IsSubmitting\"");
    }

    [Fact]
    public void Move_dialog_uses_the_medium_dialog_width()
    {
        ReadRazor("Organisms", "MoveOwnershipDialog.razor")
            .Should().Contain("Size=\"md\"");
    }

    [Fact]
    public void Move_dialog_treats_the_transient_busy_gate_as_its_own_retryable_case()
    {
        var content = ReadRazor("Organisms", "MoveOwnershipDialog.razor");
        content.Should().Contain("catch (OwnershipMoveBusyException");

        var submitBody = ExtractMethodBody(content, "SubmitAsync");
        var busyIndex = submitBody.IndexOf("catch (OwnershipMoveBusyException", StringComparison.Ordinal);
        var generalIndex = submitBody.IndexOf("catch (InvalidOperationException", StringComparison.Ordinal);
        busyIndex.Should().BeGreaterThanOrEqualTo(0);
        generalIndex.Should().BeGreaterThanOrEqualTo(0);
        busyIndex.Should().BeLessThan(generalIndex,
            "OwnershipMoveBusyException derives from InvalidOperationException, so it must be caught first");
    }

    [Fact]
    public void Move_dialog_applies_a_submit_time_conflict_through_the_guarded_model_entry_point()
    {
        var submitBody = ExtractMethodBody(ReadRazor("Organisms", "MoveOwnershipDialog.razor"), "SubmitAsync");
        submitBody.Should().Contain("ApplySubmitConflict",
            "a blocked move response is a fresh preview flagged as changed-since-preview, not a generic error");
    }

    [Theory]
    [InlineData("Pages/Integrations", "IntegrationDetail.razor")]
    [InlineData("Pages/Connections", "ConnectionDetail.razor")]
    [InlineData("Pages/Schedules", "ScheduleDetail.razor")]
    public void Host_pages_tell_the_move_dialog_which_resource_it_is_moving(string folder, string file)
    {
        ReadRazor(folder, file).Should().Contain("ResourceDisplayName=",
            "the dialog names its subject; only the host page knows the display name");
    }

    [Fact]
    public void Move_dialog_only_uses_move_classes_that_the_primitives_stylesheet_defines()
    {
        var razor = ReadRazor("Organisms", "MoveOwnershipDialog.razor");
        var css = ReadPrimitivesCss();

        var used = Regex.Matches(razor, @"\bmove-[a-z][a-z0-9_-]*")
            .Select(m => m.Value)
            .Where(name => !string.Equals(name, "move-target-select", StringComparison.Ordinal)) // element id, not a class
            .Distinct(StringComparer.Ordinal)
            .ToList();

        used.Should().NotBeEmpty("the dialog carries its own small move-* block");
        foreach (var name in used)
        {
            // A real selector occurrence, not a prefix of a longer class: plain Contain(".move-item")
            // would be satisfied by ".move-item__text" and hide a missing base rule.
            var selector = new Regex($@"\.{Regex.Escape(name)}(?![A-Za-z0-9_-])", RegexOptions.None);
            selector.IsMatch(css).Should().BeTrue(
                $"{name} is used by MoveOwnershipDialog.razor but has no rule of its own in management-ui-primitives.css");
        }
    }

    [Fact]
    public void Move_dialog_keeps_the_accessibility_affordances_the_design_relies_on()
    {
        var content = ReadRazor("Organisms", "MoveOwnershipDialog.razor");
        content.Should().Contain("<div class=\"move-status\" aria-live=\"polite\">",
            "the outcome region announces phase changes politely");
        content.Should().Contain("aria-hidden=\"true\">→",
            "the route arrow is decoration -- the labels carry the meaning");
        content.Should().Contain("role=\"list\"",
            "list-style:none drops list semantics in Safari/VoiceOver");
    }

    [Fact]
    public void Move_dialog_retry_button_reruns_the_whole_preview_load()
    {
        var content = ReadRazor("Organisms", "MoveOwnershipDialog.razor");
        content.Should().Contain("@onclick=\"LoadPreviewAsync\"",
            "Try again must go through LoadPreviewAsync (SelectTarget then BeginPreview), never BeginPreview alone");
    }

    [Fact]
    public void Move_dialog_guards_late_name_lookups_by_generation_and_reference()
    {
        var content = ReadRazor("Organisms", "MoveOwnershipDialog.razor");
        content.Should().Contain("_model.CurrentGeneration == generation && ReferenceEquals(_model.Preview, preview)",
            "a lookup that returns after the operator picked another target must not mutate state");
        content.Should().Contain("IsStillCurrent(generation, preview)");
    }

    [Fact]
    public void Move_dialog_name_resolution_can_never_fail_the_dialog()
    {
        var body = ExtractMethodBody(
            ReadRazor("Organisms", "MoveOwnershipDialog.razor"), "AfterPreviewAppliedAsync", "int generation");
        body.Should().Contain("catch (Exception",
            "the reads go through GetFromJsonAsync (JsonException/NotSupportedException) and run view-state "
            + "subscribers, so cosmetic enrichment must be total or it escapes the Blazor event handler");
    }

    [Fact]
    public void Move_dialog_pre_resolves_owner_contexts_through_the_presenter()
    {
        var content = ReadRazor("Organisms", "MoveOwnershipDialog.razor");
        content.Should().Contain("MoveOwnershipPresenter.GetContextIdsToResolve(preview)",
            "the razor may not name RelatedResourceContextId, so the presenter hands it the id list");
        content.Should().Contain("ViewState.EnsureContextsKnownAsync");
    }

    [Fact]
    public void Move_route_header_stacks_at_the_dialogs_narrow_breakpoint()
    {
        var css = ReadPrimitivesCss();
        const string marker = "@media (max-width: 30rem)";

        var stacksRoute = false;
        for (var index = css.IndexOf(marker, StringComparison.Ordinal);
             index >= 0;
             index = css.IndexOf(marker, index + marker.Length, StringComparison.Ordinal))
        {
            if (ExtractBraceBlock(css, index).Contains(".move-route", StringComparison.Ordinal))
            {
                stacksRoute = true;
                break;
            }
        }

        stacksRoute.Should().BeTrue(
            "the route header must collapse to one column at the same width the dialog chrome stacks at");
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
    /// <param name="argPrefix">
    /// Text that follows the opening parenthesis of the declaration, so methods that take arguments can be
    /// located too. Defaults to <c>")"</c>, i.e. the original parameterless <c>Name()</c> form.
    /// </param>
    private static string ExtractMethodBody(string content, string methodName, string argPrefix = ")")
    {
        var declaration = methodName + "(" + argPrefix;
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

    /// <summary>Extracts the brace-delimited block that follows <paramref name="fromIndex"/> (e.g. an @media rule).</summary>
    private static string ExtractBraceBlock(string content, int fromIndex)
    {
        var braceStart = content.IndexOf('{', fromIndex);
        if (braceStart < 0)
            return "";

        var depth = 0;
        for (var i = braceStart; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return content[braceStart..(i + 1)];
            }
        }

        return content[braceStart..];
    }

    private static string ReadRazor(string folder, string file) =>
        ReadRepoFile(Path.Combine("src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client",
            Path.Combine(folder.Split('/')), file));

    /// <summary>Reads a source file from Apperture.Management.Ui.Shared (the presenter/model home).</summary>
    private static string ReadShared(string file) =>
        ReadRepoFile(Path.Combine("src", "Apperture.ControlPlane", "Apperture.Management.Ui.Shared", file));

    private static string ReadPrimitivesCss() =>
        ReadRepoFile(Path.Combine("src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client",
            "wwwroot", "css", "management-ui-primitives.css"));

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var path = Path.Combine(dir!.FullName, relativePath);
        File.Exists(path).Should().BeTrue($"expected {path} to exist");
        return File.ReadAllText(path);
    }
}
