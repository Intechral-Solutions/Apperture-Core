using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class S9PersonalNotificationPreferencesUiTests
{
    [Fact]
    public void Personal_notifications_page_includes_email_preferences_section()
    {
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Me/PersonalNotifications.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("PersonalNotificationEmailPreferences");
    }

    [Fact]
    public void Email_preferences_component_mentions_digest_deferred()
    {
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/PersonalNotificationEmailPreferences.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("Daily digest");
        content.Should().Contain("GetMyNotificationEmailPreferencesAsync");
    }

    private static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(dir!.FullName, relative);
    }
}
