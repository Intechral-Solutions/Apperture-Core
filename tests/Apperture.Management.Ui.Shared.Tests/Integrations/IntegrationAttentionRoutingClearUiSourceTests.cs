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
    public void Clear_button_exists_in_attention_routing_section_and_opens_the_confirm_dialog()
    {
        var content = ReadRazor(Folder, TargetFile);
        content.Should().Contain("id=\"integration-attention-routing-clear-trigger\"");

        // The trigger button no longer calls ClearRoutingAsync directly -- it opens the confirm
        // dialog, which is the one that invokes ClearRoutingAsync via OnConfirm (see
        // A_ConfirmActionDialog_gates_the_clear_operation below).
        content.Should().Contain("@onclick=\"() => _confirmClearOpen = true\"");
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

        // Task 5: ClearRoutingAsync is now the ConfirmActionDialog's OnConfirm target, so it must
        // not locally catch -- a local catch here would suppress the real server error text that
        // ConfirmActionDialog is responsible for surfacing verbatim (same no-local-catch convention
        // as NotificationDefinitionDetail's DeleteDefinitionAsync from Task 1).
        handlerBody.Should().NotContain("catch",
            "ConfirmActionDialog itself catches and surfaces the real server error text; a local catch here would suppress it");

        // Refreshing the effective-routing display now happens post-confirmation (see
        // OnRoutingClearConfirmedAsync_refreshes_via_LoadAsync_and_closes_the_dialog below), not
        // inside the OnConfirm handler itself.
        handlerBody.Should().NotContain("LoadAsync",
            "refresh-on-success now belongs to OnConfirmed, not to the OnConfirm handler itself");
    }

    [Fact]
    public void A_ConfirmActionDialog_gates_the_clear_operation()
    {
        var content = ReadRazor(Folder, TargetFile);

        // Task 5 addendum: clicking Clear immediately changes the Integration's effective attention
        // routing (who gets notified), with no on-screen record of the prior configuration -- so it
        // now requires confirmation, scoped to the Clear button's own enclosing markup block (not
        // the whole 700+ line file), so this can't false-positive against a ConfirmActionDialog
        // legitimately added elsewhere on this page for an unrelated action. Since the dialog itself
        // is rendered as a sibling of the button (not nested inside its markup block), this looks for
        // the dialog wiring anywhere in the file but pins its content precisely below.
        var clearButtonBlock = ExtractClearButtonBlock(content);
        clearButtonBlock.Should().Contain("@onclick=\"() => _confirmClearOpen = true\"",
            "the trigger button must open the dialog rather than call the clear method directly");
        clearButtonBlock.Should().NotContain("@onclick=\"ClearRoutingAsync\"",
            "the button must no longer call the clear method directly -- confirmation is required first");

        content.Should().Contain("ConfirmActionDialog IsOpen=\"@_confirmClearOpen\"");

        var dialogBlock = ExtractConfirmActionDialogBlock(content);
        dialogBlock.Should().Contain("Title=\"Clear routing override?\"");
        dialogBlock.Should().Contain("ConfirmLabel=\"Clear override\"");
        dialogBlock.Should().Contain("Emphasis=\"ConfirmActionEmphasis.Standard\"",
            "clearing an override reverts to a default -- it is not a destructive/unrecoverable action");
        dialogBlock.Should().Contain("OnConfirm=\"ClearRoutingAsync\"",
            "the dialog must invoke the same clear method the direct-click handler used to call");
        dialogBlock.Should().Contain(
            "This integration will stop using its custom Attention routing and fall back to the default routing configuration.");
    }

    [Fact]
    public void OnRoutingClearConfirmedAsync_refreshes_via_LoadAsync_and_closes_the_dialog()
    {
        var content = ReadRazor(Folder, TargetFile);
        content.Should().Contain("OnConfirmed=\"OnRoutingClearConfirmedAsync\"");

        var handlerBody = ExtractMethodBody(content, "OnRoutingClearConfirmedAsync");
        handlerBody.Should().Contain("_confirmClearOpen = false");
        handlerBody.Should().Contain("await LoadAsync()");
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

    /// <summary>
    /// Extracts the Clear override button's own enclosing markup block (the surrounding
    /// <c>&lt;div class="flex flex-wrap gap-2 items-center"&gt;...&lt;/div&gt;</c>) so assertions
    /// about that button's own markup can't false-positive against unrelated markup elsewhere in
    /// this large page -- the same "find the nearest preceding boundary from the button's index"
    /// scoping technique used above for the CanManageSchedules guard check.
    /// </summary>
    private static string ExtractClearButtonBlock(string content)
    {
        var clearIndex = content.IndexOf("id=\"integration-attention-routing-clear-trigger\"", StringComparison.Ordinal);
        clearIndex.Should().BeGreaterThanOrEqualTo(0, "expected to find the Clear override button");

        const string blockOpenTag = "<div class=\"flex flex-wrap gap-2 items-center\">";
        var blockStart = content.LastIndexOf(blockOpenTag, clearIndex, StringComparison.Ordinal);
        blockStart.Should().BeGreaterThanOrEqualTo(0, "expected to find the Clear button's enclosing markup block");

        const string closeTag = "</div>";
        var blockEnd = content.IndexOf(closeTag, clearIndex, StringComparison.Ordinal);
        blockEnd.Should().BeGreaterThanOrEqualTo(0, "expected a closing </div> for the Clear button's block");

        return content[blockStart..(blockEnd + closeTag.Length)];
    }

    /// <summary>
    /// Extracts the &lt;ConfirmActionDialog ...&gt;...&lt;/ConfirmActionDialog&gt; element that gates
    /// the clear operation, from its opening tag to its matching closing tag, so assertions about its
    /// parameters/copy can't false-positive against some other ConfirmActionDialog usage elsewhere on
    /// this page.
    /// </summary>
    private static string ExtractConfirmActionDialogBlock(string content)
    {
        const string openTag = "<ConfirmActionDialog IsOpen=\"@_confirmClearOpen\"";
        var blockStart = content.IndexOf(openTag, StringComparison.Ordinal);
        blockStart.Should().BeGreaterThanOrEqualTo(0, "expected to find the Clear confirm dialog's opening tag");

        const string closeTag = "</ConfirmActionDialog>";
        var blockEnd = content.IndexOf(closeTag, blockStart, StringComparison.Ordinal);
        blockEnd.Should().BeGreaterThanOrEqualTo(0, "expected a closing </ConfirmActionDialog> tag");

        return content[blockStart..(blockEnd + closeTag.Length)];
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
