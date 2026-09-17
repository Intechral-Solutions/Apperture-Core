using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests.Integrations;

/// <summary>
/// Razor source-text assertions, matching this suite's established convention (see
/// NotificationDefinitionDeleteUiSourceTests, and the older MovementUiSourceTests). There is no
/// bUnit in this repository; component rendering is not available, so the contract is pinned at
/// the markup level.
/// </summary>
public sealed class IntegrationAttentionRoutingClearUiSourceTests
{
    private const string Folder = "Pages/Integrations";
    private const string TargetFile = "IntegrationScheduleDiagnostics.razor";
    private const string CanManageSchedulesGuard = "@if (_capabilities?.CanManageSchedules == true)";

    [Fact]
    public void Clear_button_exists_in_attention_routing_section()
    {
        var content = ReadRazor(Folder, TargetFile);
        content.Should().Contain("id=\"integration-attention-routing-clear-trigger\"");
        content.Should().Contain("@onclick=\"ClearRoutingAsync\"");
    }

    [Fact]
    public void Clear_button_is_gated_by_the_exact_same_CanManageSchedules_condition_as_Save()
    {
        var content = ReadRazor(Folder, TargetFile);

        var saveIndex = content.IndexOf("@onclick=\"SaveRoutingAsync\"", StringComparison.Ordinal);
        saveIndex.Should().BeGreaterThanOrEqualTo(0, "expected to find the Save integration override button");

        var clearIndex = content.IndexOf("id=\"integration-attention-routing-clear-trigger\"", StringComparison.Ordinal);
        clearIndex.Should().BeGreaterThanOrEqualTo(0, "expected to find the Clear override button");

        // Both buttons must be inside the same enclosing CanManageSchedules guard -- not merely
        // each gated by *some* condition somewhere earlier in the file. Find the nearest preceding
        // guard for each button and assert they resolve to the identical guard occurrence.
        var saveGuardIndex = content.LastIndexOf(CanManageSchedulesGuard, saveIndex, StringComparison.Ordinal);
        saveGuardIndex.Should().BeGreaterThanOrEqualTo(0, "the Save button must be gated behind CanManageSchedules");

        var clearGuardIndex = content.LastIndexOf(CanManageSchedulesGuard, clearIndex, StringComparison.Ordinal);
        clearGuardIndex.Should().BeGreaterThanOrEqualTo(0, "the Clear button must be gated behind CanManageSchedules");

        clearGuardIndex.Should().Be(saveGuardIndex,
            "Clear must share the exact same CanManageSchedules guard as Save, not a new/different authorization boundary");

        // There is exactly one CanManageSchedules guard in this file, so both buttons resolving to
        // it (rather than -1, or to two different occurrences) confirms they share one boundary.
        content.Split("_capabilities?.CanManageSchedules == true").Length.Should().Be(2,
            "expected exactly one CanManageSchedules guard in this file");
    }

    [Fact]
    public void ClearRoutingAsync_calls_the_clear_client_method_not_put()
    {
        var content = ReadRazor(Folder, TargetFile);
        content.Should().Contain("Task ClearRoutingAsync()");

        var handlerBody = ExtractMethodBody(content, "ClearRoutingAsync");
        handlerBody.Should().Contain("Client.ClearIntegrationAttentionRoutingAsync(IntegrationId)");
        handlerBody.Should().NotContain("PutIntegrationAttentionRoutingAsync",
            "Clear must call the dedicated clear endpoint, not the upsert/save endpoint");

        // Refreshes the effective-routing display the same way Save does, rather than hand-patching
        // local state.
        handlerBody.Should().Contain("await LoadAsync()");

        // Uses its own scoped error field, independent from Save's.
        handlerBody.Should().Contain("_routingClearError");
        handlerBody.Should().Contain("catch (HttpRequestException ex)");
    }

    [Fact]
    public void No_ConfirmActionDialog_was_added_for_the_clear_operation()
    {
        var content = ReadRazor(Folder, TargetFile);

        // This clear operation is safe/idempotent (the backend returns 204 whether or not an
        // override row existed), so it must not gain a confirmation dialog -- that would be
        // scope creep into Task 1's destructive-delete territory.
        content.Should().NotContain("ConfirmActionDialog");
    }

    [Fact]
    public void Misleading_instance_default_copy_has_been_replaced()
    {
        var content = ReadRazor(Folder, TargetFile);

        content.Should().NotContain("Leave as “Instance default” to clear the override for that role.",
            "selecting Instance default and clicking Save does not clear the underlying override row");
        content.Should().NotContain("Leave as \"Instance default\" to clear the override for that role.",
            "selecting Instance default and clicking Save does not clear the underlying override row");

        content.Should().Contain("Clear override",
            "the replacement copy or button should point the operator at the new Clear override action");
    }

    /// <summary>
    /// Extracts a single-method body (from its declaration line to its matching closing brace) via
    /// simple brace counting, so assertions about "does this handler call X" don't false-positive on
    /// unrelated code elsewhere in a large page. Searches for "methodName(" -- the declaration and
    /// call-site form -- rather than the bare name, so it does not match the razor markup attribute
    /// usage (e.g. @onclick="ClearRoutingAsync", which has no trailing parenthesis and appears
    /// earlier in the file).
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
