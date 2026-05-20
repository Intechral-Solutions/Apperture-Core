using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic96IntegrationActionFeedbackTests
{
    private static string ReadIntegrationDetailSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var path = Path.Combine(dir!.FullName, "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client", "Pages", "Integrations", "IntegrationDetail.razor");
        File.Exists(path).Should().BeTrue();
        return File.ReadAllText(path);
    }

    [Fact]
    public void IntegrationDetail_renders_ActionFeedback_after_summary()
    {
        var content = ReadIntegrationDetailSource();
        content.Should().Contain("<ActionFeedback Kind=\"@_actionFeedbackKind\" Message=\"@_actionMessage\" />");
        content.Should().Contain("SetActionFeedback(");
    }

    [Fact]
    public void IntegrationDetail_preserves_action_feedback_across_reload()
    {
        var content = ReadIntegrationDetailSource();
        content.Should().Contain("LoadAsync(bool preserveActionFeedback = false)");
        content.Should().Contain("preserveActionFeedback: true");
        content.Should().Contain("if (!preserveActionFeedback)");
    }

    [Fact]
    public void IntegrationDetail_refresh_clears_feedback_via_LoadAsync()
    {
        var content = ReadIntegrationDetailSource();
        content.Should().Contain("RefreshAsync");
        content.Should().Contain("@onclick=\"RefreshAsync\"");
    }
}
