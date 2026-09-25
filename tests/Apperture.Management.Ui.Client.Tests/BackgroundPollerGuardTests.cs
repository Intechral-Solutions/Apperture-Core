using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// SEC-1A3 inventory. A new timer in the UI client that is not on this list fails the build, so a poller
/// cannot silently renew the session.
/// </summary>
public sealed class BackgroundPollerGuardTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Timer_driven_api_callers_are_the_known_set_and_open_a_background_scope()
    {
        var root = Path.Combine(RepoRoot(), "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client");
        var timerFiles = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains("PeriodicTimer", StringComparison.Ordinal)
                    || text.Contains("CreateTimer(", StringComparison.Ordinal)
                    || text.Contains("new Timer(", StringComparison.Ordinal);
            })
            .Select(f => Path.GetRelativePath(RepoRoot(), f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        timerFiles.Should().BeEquivalentTo(
        [
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor",
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/NotificationToastWatcher.cs",
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/ToastService.cs",
        ]);

        var runDetail = Source("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor");
        var poll = runDetail[runDetail.IndexOf("private void StartPolling()", StringComparison.Ordinal)..runDetail.IndexOf("private void StopPolling()", StringComparison.Ordinal)];
        poll.Should().Contain("BackgroundRequest.Begin()", "automatic run refresh is background");
        var load = runDetail[runDetail.IndexOf("private async Task LoadAsync(", StringComparison.Ordinal)..];
        load[..load.IndexOf("private void PublishShellTitle()", StringComparison.Ordinal)]
            .Should().NotContain("BackgroundRequest.Begin()", "the shared load method stays foreground so an operator Refresh renews");

        Source("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/NotificationToastWatcher.cs")
            .Should().Contain("BackgroundRequest.Begin()");
        Source("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/ToastService.cs")
            .Should().NotContain("IManagementClient", "dwell timers are local UI, not API calls");
    }

    [Fact]
    public void Automatic_unread_count_loads_are_background_and_opening_the_menu_is_not()
    {
        var menu = Source("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/PersonalNotificationMenu.razor");
        var firstPaint = menu[menu.IndexOf("private async Task RefreshCountAndRenderAsync()", StringComparison.Ordinal)..menu.IndexOf("private async void OnSessionChanged()", StringComparison.Ordinal)];
        var sessionChange = menu[menu.IndexOf("private async void OnSessionChanged()", StringComparison.Ordinal)..menu.IndexOf("private async Task LoadMenuAsync()", StringComparison.Ordinal)];
        var menuOpen = menu[menu.IndexOf("private async Task LoadMenuAsync()", StringComparison.Ordinal)..menu.IndexOf("private async Task RefreshCountAsync()", StringComparison.Ordinal)];

        firstPaint.Should().Contain("BackgroundRequest.Begin()");
        sessionChange.Should().Contain("BackgroundRequest.Begin()");
        menuOpen.Should().NotContain("BackgroundRequest.Begin()", "opening the menu is operator activity");
    }
}
