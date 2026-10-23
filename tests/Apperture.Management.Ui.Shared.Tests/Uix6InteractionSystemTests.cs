using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>UIX-6 — interaction and progressive disclosure system.</summary>
public sealed class Uix6InteractionSystemTests
{
    // ---------- Disclosure ----------

    [Fact]
    public void Disclosure_uses_semantic_details_without_javascript()
    {
        var content = ReadComponent("Molecules/Disclosure.razor");

        content.Should().Contain("<details");
        content.Should().Contain("<summary");
        content.Should().Contain("InitiallyOpen");
        content.Should().Contain("SummaryContent");
        content.Should().Contain(ManagementInteractionSurfaces.DisclosureClass);

        // No JavaScript dependency and no URL state for an ephemeral disclosure.
        content.Should().NotContain("IJSRuntime");
        content.Should().NotContain("NavigationManager");
        content.Should().NotContain("@onclick:");
        content.Should().NotContain("@onkeydown:");
    }

    [Fact]
    public void TechnicalDetails_is_collapsed_disclosure_with_definition_list()
    {
        var content = ReadComponent("Molecules/TechnicalDetails.razor");

        content.Should().Contain("<Disclosure");
        content.Should().Contain("<dl");
        content.Should().Contain("TechnicalDetailsUiHelpers.ResolveSummary");
        content.Should().Contain("ui-technical-details__list");

        // Collapsed by default: the component must not force the disclosure open.
        content.Should().NotContain("InitiallyOpen=\"true\"");
        content.Should().NotContain("IJSRuntime");
    }

    [Fact]
    public void TechnicalDetail_renders_definition_pair_with_monospace_default()
    {
        var content = ReadComponent("Molecules/TechnicalDetail.razor");

        content.Should().Contain("<dt");
        content.Should().Contain("<dd");
        content.Should().Contain("TechnicalDetailsUiHelpers.FormatValue");
        content.Should().Contain("public bool Monospace { get; set; } = true;");
        content.Should().Contain("ui-technical-details__value--mono");
    }

    // ---------- Dialog foundation ----------

    [Fact]
    public void ManagementDialog_meets_accessible_dialog_contract()
    {
        var content = ReadComponent("Organisms/ManagementDialog.razor");

        content.Should().Contain("role=\"dialog\"");
        content.Should().Contain("aria-modal=\"true\"");
        content.Should().Contain("aria-labelledby");
        content.Should().Contain("aria-busy");
        content.Should().Contain("data-testid=\"dialog-close\"");
        content.Should().Contain("Escape");
        content.Should().Contain("tabindex=\"-1\"");
        content.Should().Contain("ui-dialog__focus-sentinel");

        // Focus is captured on open and restored on close.
        content.Should().Contain("appertureShell.captureFocusedElementId");
        content.Should().Contain("appertureShell.focusElementById");
        content.Should().Contain("appertureShell.focusEdgeIn");
        content.Should().Contain("appertureShell.setModalOpen");

        // Disposal-safe interop: no callbacks after teardown.
        content.Should().Contain("IAsyncDisposable");
        content.Should().Contain("catch (JSException)");
        content.Should().Contain("catch (InvalidOperationException)");
        content.Should().Contain("_disposed");

        // Shell invariants: no route-keyed remount, no structural DOM rewriting.
        content.Should().NotContain("@key");
        content.Should().NotContain("innerHTML");
        content.Should().NotContain("appendChild");
    }

    [Fact]
    public void ConfirmActionDialog_builds_on_dialog_and_survives_failure()
    {
        var content = ReadComponent("Organisms/ConfirmActionDialog.razor");

        content.Should().Contain("<ManagementDialog");
        content.Should().Contain("data-testid=\"confirm-accept\"");
        content.Should().Contain("data-testid=\"confirm-cancel\"");
        content.Should().Contain("ConfirmActionUiHelpers.ConfirmButtonClass");
        content.Should().Contain("ConfirmActionUiHelpers.ResolveBusyLabel");

        // Duplicate submission guard.
        content.Should().Contain("if (_busy || ConfirmDisabled)");
        content.Should().Contain("disabled=\"@(_busy || ConfirmDisabled)\"");

        // A failed action reports the reason and does not raise the success callback.
        content.Should().Contain("catch (Exception ex)");
        content.Should().Contain("_error = ");
        content.Should().Contain("OnConfirmed");
        content.Should().Contain("PageMessageKind.Error");
    }

    // ---------- Confirmation migrations ----------

    [Theory]
    [InlineData("Pages/Integrations/IntegrationDetail.razor")]
    [InlineData("Pages/Integrations/IntegrationSettings.razor")]
    [InlineData("Pages/Connections/ConnectionDetail.razor")]
    [InlineData("Pages/Config/PlatformSettings.razor")]
    public void Migrated_pages_confirm_through_the_shared_dialog(string relativePath)
    {
        var content = ReadComponent(relativePath);

        content.Should().Contain("<ConfirmActionDialog");
        content.Should().NotContain("\"confirm\"");
    }

    [Fact]
    public void Destructive_actions_use_destructive_emphasis()
    {
        var integration = ReadComponent("Pages/Integrations/IntegrationDetail.razor");

        integration.Should().Contain("ConfirmActionEmphasis.Destructive");
        integration.Should().Contain("Delete integration?");

        // HUB-1: binding management (and its confirmation dialog) moved to the dedicated
        // Connections hub section.
        var connections = ReadComponent("Pages/Integrations/IntegrationConnections.razor");
        connections.Should().Contain("Remove binding?");

        var connection = ReadComponent("Pages/Connections/ConnectionDetail.razor");
        connection.Should().Contain("ConfirmActionEmphasis.Caution");
        connection.Should().Contain("Retire connection?");
    }

    [Fact]
    public void No_native_confirm_remains_in_production_ui()
    {
        var uiRoot = Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client");

        var offenders = Directory
            .EnumerateFiles(uiRoot, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                        || p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(p => File.ReadAllText(p).Contains("\"confirm\"", StringComparison.Ordinal))
            .ToArray();

        offenders.Should().BeEmpty("UIX-6 replaced native window.confirm with ConfirmActionDialog");
    }

    [Fact]
    public void Every_ConfirmActionDialog_usage_passes_only_parameters_the_dialog_declares()
    {
        // Blazor rejects an argument the component does not declare, at render time. AvatarPicker passed IsBusy (the dialog keeps its
        // own busy state), so /account/profile threw on every load. Compile-time checks do not catch it; this does.
        var declared = System.Text.RegularExpressions.Regex
            .Matches(ReadComponent("Organisms/ConfirmActionDialog.razor"), @"\[Parameter\][^;{]*?\s(\w+)\s*\{")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        declared.Should().Contain(["IsOpen", "OnConfirm", "OnCancel"], "the parameter list was read from the component");

        var uiRoot = Path.Combine(FindRepoRoot(), "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client");
        var offenders = new List<string>();
        var usages = 0;
        foreach (var file in Directory.EnumerateFiles(uiRoot, "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var from = 0;
            while ((from = text.IndexOf("<ConfirmActionDialog", from, StringComparison.Ordinal)) >= 0)
            {
                from += "<ConfirmActionDialog".Length;
                usages++;
                foreach (var name in TopLevelAttributeNames(text, from))
                {
                    if (!name.StartsWith('@') && !declared.Contains(name))
                        offenders.Add($"{Path.GetRelativePath(uiRoot, file)}: {name}");
                }
            }
        }

        usages.Should().BeGreaterThan(5, "the scan found the dialog's callers");
        offenders.Should().BeEmpty("a ConfirmActionDialog argument it does not declare throws while the page renders");
    }

    /// <summary>The attribute names of the start tag that begins at <paramref name="index"/>, ignoring what is inside quotes.</summary>
    private static IEnumerable<string> TopLevelAttributeNames(string text, int index)
    {
        var quote = '\0';
        var depth = 0;
        var name = new System.Text.StringBuilder();
        for (var i = index; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
                continue;
            }

            if (c == '"' || c == '\'')
            {
                quote = c;
                continue;
            }

            if (c == '(')
                depth++;
            else if (c == ')')
                depth--;

            if (depth > 0)
                continue;

            if (c == '>')
                yield break;

            if (c == '=' && name.Length > 0)
            {
                yield return name.ToString();
                name.Clear();
            }
            else if (char.IsLetterOrDigit(c) || c is '@' or ':' or '.' or '-' or '_')
            {
                name.Append(c);
            }
            else
            {
                name.Clear();
            }
        }
    }

    // ---------- Technical detail migrations ----------

    [Theory]
    [InlineData("Pages/Demo/DemoCustomerDetail.razor")]
    [InlineData("Pages/Demo/DemoCustomerActivity.razor")]
    [InlineData("Organisms/RunOperationItemizationPanel.razor")]
    [InlineData("Pages/Integrations/IntegrationDetail.razor")]
    public void Representative_disclosures_use_shared_technical_details(string relativePath)
    {
        var content = ReadComponent(relativePath);

        content.Should().Contain("<TechnicalDetails");
        content.Should().Contain("<TechnicalDetail ");
    }

    [Fact]
    public void Migrated_pages_do_not_lead_with_raw_identifier_markup()
    {
        var activity = ReadComponent("Pages/Demo/DemoCustomerActivity.razor");

        activity.Should().NotContain("<details");
        activity.Should().NotContain("runId:");
        activity.Should().Contain("Run ID");
    }

    // ---------- Tokens and layering ----------

    [Fact]
    public void Layering_model_is_ordered_and_places_dialog_above_shell()
    {
        var layers = ManagementInteractionSurfaces.LayeringModel;

        layers.Select(l => l.ZIndex).Should().BeInAscendingOrder();

        int Z(string name) => layers.Single(l => l.Layer == name).ZIndex;

        Z("dialog-scrim").Should().BeGreaterThan(Z("shell-topbar"));
        Z("dialog").Should().BeGreaterThan(Z("dialog-scrim"));
        Z("toast").Should().BeGreaterThan(Z("dialog"));
    }

    [Fact]
    public void Interaction_tokens_are_centralized_in_primitives_css()
    {
        var css = ReadCss();

        css.Should().Contain("--z-dialog-scrim");
        css.Should().Contain("--z-dialog");
        css.Should().Contain("--z-toast");
        css.Should().Contain("--interaction-duration");
        css.Should().Contain("--interaction-easing");
        css.Should().Contain("--dialog-width-sm");
        css.Should().Contain("--dialog-width-md");
        css.Should().Contain("--dialog-width-lg");
        css.Should().Contain("--interaction-focus-ring");
        css.Should().Contain("prefers-reduced-motion");
        css.Should().Contain(".ui-dialog");
        css.Should().Contain(".ui-disclosure");
        css.Should().Contain(".ui-technical-details__list");
    }

    [Fact]
    public void Ui_components_do_not_introduce_arbitrary_z_index_values()
    {
        var uiRoot = Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client");

        var offenders = Directory
            .EnumerateFiles(uiRoot, "*.razor", SearchOption.AllDirectories)
            .Where(p => File.ReadAllText(p).Contains("z-[", StringComparison.Ordinal))
            .ToArray();

        offenders.Should().BeEmpty("overlay layering belongs to the shared --z-* tokens");
    }

    // ---------- Feedback policy ----------

    [Fact]
    public void Errors_persist_until_dismissed_and_other_toasts_expire()
    {
        ToastPolicy.ResolveDuration(ToastSeverity.Error, null).Should().BeNull();
        ToastPolicy.ResolveDuration(ToastSeverity.Success, null).Should().Be(ToastPolicy.DefaultDuration);
        ToastPolicy.ResolveDuration(ToastSeverity.Info, TimeSpan.FromSeconds(2)).Should().Be(TimeSpan.FromSeconds(2));
        ToastPolicy.ResolveDuration(ToastSeverity.Error, TimeSpan.FromSeconds(3)).Should().Be(TimeSpan.FromSeconds(3));
        ToastPolicy.DefaultDuration.Should().Be(TimeSpan.FromSeconds(6));
    }

    [Fact]
    public void Toast_announcements_are_assertive_only_for_errors()
    {
        ToastPolicy.ResolveAriaLive(ToastSeverity.Error).Should().Be("assertive");
        ToastPolicy.ResolveAriaLive(ToastSeverity.Success).Should().Be("polite");
        ToastPolicy.ResolveAriaLive(ToastSeverity.Warning).Should().Be("polite");
    }

    [Fact]
    public void Duplicate_outcomes_share_a_signature()
    {
        var first = ToastPolicy.BuildSignature(ToastSeverity.Success, "Saved.", null);
        var same = ToastPolicy.BuildSignature(ToastSeverity.Success, " Saved. ", null);
        var differentSeverity = ToastPolicy.BuildSignature(ToastSeverity.Error, "Saved.", null);
        var differentTitle = ToastPolicy.BuildSignature(ToastSeverity.Success, "Saved.", "Settings");

        same.Should().Be(first);
        differentSeverity.Should().NotBe(first);
        differentTitle.Should().NotBe(first);
    }

    [Fact]
    public void ToastHost_separates_live_regions_and_uses_the_shared_layer()
    {
        var content = ReadComponent("Organisms/ToastHost.razor");

        content.Should().Contain("ui-toast-layer");
        content.Should().Contain("aria-live=\"assertive\"");
        content.Should().Contain("aria-live=\"polite\"");
        content.Should().Contain("aria-label=\"Dismiss notification\"");
        content.Should().NotContain("z-[100]");
    }

    // ---------- Confirm helpers ----------

    [Fact]
    public void Confirm_labels_fall_back_to_defaults()
    {
        ConfirmActionUiHelpers.ResolveConfirmLabel(null).Should().Be("Confirm");
        ConfirmActionUiHelpers.ResolveConfirmLabel("  ").Should().Be("Confirm");
        ConfirmActionUiHelpers.ResolveConfirmLabel(" Delete integration ").Should().Be("Delete integration");
        ConfirmActionUiHelpers.ResolveCancelLabel(null).Should().Be("Cancel");
        ConfirmActionUiHelpers.ResolveBusyLabel("Delete integration").Should().Be("Delete integration…");
    }

    [Fact]
    public void Confirm_emphasis_maps_to_shared_button_treatments()
    {
        ConfirmActionUiHelpers.ConfirmButtonClass(ConfirmActionEmphasis.Destructive)
            .Should().Contain("ui-toolbar-btn--danger");
        ConfirmActionUiHelpers.ConfirmButtonClass(ConfirmActionEmphasis.Caution)
            .Should().Contain("ui-dialog__confirm--caution");
        ConfirmActionUiHelpers.ConfirmButtonClass(ConfirmActionEmphasis.Standard)
            .Should().Be("ui-toolbar-btn");

        ConfirmActionUiHelpers.RequiresConsequenceText(ConfirmActionEmphasis.Destructive).Should().BeTrue();
        ConfirmActionUiHelpers.RequiresConsequenceText(ConfirmActionEmphasis.Caution).Should().BeTrue();
        ConfirmActionUiHelpers.RequiresConsequenceText(ConfirmActionEmphasis.Standard).Should().BeFalse();
    }

    [Fact]
    public void Technical_detail_values_have_a_placeholder_and_default_summary()
    {
        TechnicalDetailsUiHelpers.ResolveSummary(null).Should().Be("Technical details");
        TechnicalDetailsUiHelpers.ResolveSummary(" Group technical details ").Should().Be("Group technical details");
        TechnicalDetailsUiHelpers.FormatValue(null).Should().Be("—");
        TechnicalDetailsUiHelpers.FormatValue("  ").Should().Be("—");
        TechnicalDetailsUiHelpers.FormatValue(" run-1 ").Should().Be("run-1");
        TechnicalDetailsUiHelpers.FormatLabel(" Run ID ").Should().Be("Run ID");
    }

    // ---------- Deferred scope ----------

    [Fact]
    public void No_inspection_panel_or_drawer_framework_was_introduced()
    {
        var uiRoot = Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client");

        var componentNames = Directory
            .EnumerateFiles(uiRoot, "*.razor", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();

        componentNames.Should().NotContain("InspectionPanel");
        componentNames.Should().NotContain("Drawer");
        componentNames.Should().NotContain("SidePanel");
        componentNames.Should().NotContain("Popover");
        componentNames.Should().NotContain("Accordion");
    }

    [Fact]
    public void Interaction_pattern_guide_is_published()
    {
        var guide = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "docs", "design-system", "interaction-patterns.md"));

        guide.Should().Contain("Disclosure");
        guide.Should().Contain("TechnicalDetails");
        guide.Should().Contain("ConfirmActionDialog");
        guide.Should().Contain("Inspection panel");
        guide.Should().Contain("window.confirm");
    }

    // ---------- Helpers ----------

    private static string ReadComponent(string relativePath) =>
        File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client",
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string ReadCss() =>
        File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client",
            "wwwroot",
            "css",
            "management-ui-primitives.css"));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Apperture.slnx"))
                || File.Exists(Path.Combine(dir.FullName, "Apperture.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root from test base directory.");
    }
}
