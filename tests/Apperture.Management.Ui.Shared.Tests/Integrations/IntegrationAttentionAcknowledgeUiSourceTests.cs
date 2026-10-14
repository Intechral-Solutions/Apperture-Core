using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests.Integrations;

/// <summary>
/// Razor source-text assertions (this suite's convention; the rendered behaviour is covered by bUnit in
/// <c>IntegrationAttentionAcknowledgeUiTests</c>). Acknowledging an attention incident needs <c>integration.update</c> on the
/// server, so the control follows <c>CanUpdateIntegrations</c> and never <c>integration.read</c> or <c>schedule.manage</c>.
/// </summary>
public sealed class IntegrationAttentionAcknowledgeUiSourceTests
{
    private const string TargetFile = "IntegrationScheduleDiagnostics.razor";

    [Fact]
    public void The_Acknowledge_button_is_offered_only_with_the_integration_update_capability()
    {
        var content = ReadRazor();

        content.Should().Contain("private bool CanAcknowledgeIncidents => _capabilities?.CanUpdateIntegrations is true;");
        var button = content.IndexOf("await AcknowledgeIncidentAsync(i.Id)", StringComparison.Ordinal);
        button.Should().BeGreaterThan(0);
        var guard = content.LastIndexOf("@if (string.Equals(i.Status, \"Active\"", button, StringComparison.Ordinal);
        guard.Should().BeGreaterThan(0);
        content[guard..button].Should().Contain("&& CanAcknowledgeIncidents", "the button sits inside the capability guard");
    }

    [Fact]
    public void The_handler_holds_the_line_if_it_is_driven_without_the_capability_and_maps_the_new_refusals()
    {
        var content = ReadRazor();
        var start = content.IndexOf("private async Task AcknowledgeIncidentAsync(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var handler = content[start..];

        handler.Should().Contain("!CanAcknowledgeIncidents");
        handler.Should().Contain("System.Net.HttpStatusCode.Conflict").And.Contain("System.Net.HttpStatusCode.Forbidden");
        handler.Should().Contain("_ackNotice", "a refusal that leaves the page usable is shown beside the history");
        content.Should().NotContain("CanManageSchedules");
    }

    private static string ReadRazor()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var path = Path.Combine(dir!.FullName, "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client", "Pages", "Integrations", TargetFile);
        File.Exists(path).Should().BeTrue($"expected {path} to exist");
        return File.ReadAllText(path);
    }
}
