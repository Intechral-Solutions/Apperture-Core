using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class S8BPersonalNotificationDeepLinkTests
{
    [Fact]
    public void ResolveOpenTarget_prefers_rendered_action_url()
    {
        var item = new PersonalNotificationInboxItemDto
        {
            NotificationInstanceId = "inst-1",
            CanOpenAdminInstanceDetail = true,
            RenderedActionUrl = "/integrations/int-1/schedule-diagnostics",
        };

        PersonalNotificationUiRoutes.ResolveOpenTarget(item, canOpenAdminInstanceDetail: true)
            .Should().Be("/integrations/int-1/schedule-diagnostics");
    }

    [Fact]
    public void ResolveOpenTarget_falls_back_to_admin_instance_detail()
    {
        var item = new PersonalNotificationInboxItemDto
        {
            NotificationInstanceId = "inst-42",
            CanOpenAdminInstanceDetail = true,
        };

        PersonalNotificationUiRoutes.ResolveOpenTarget(item, canOpenAdminInstanceDetail: true)
            .Should().Be("/config/notifications/instances/inst-42");
    }

    [Fact]
    public void ResolveOpenTarget_returns_null_when_no_target()
    {
        var item = new PersonalNotificationInboxItemDto
        {
            NotificationInstanceId = "inst-1",
            CanOpenAdminInstanceDetail = false,
        };

        PersonalNotificationUiRoutes.ResolveOpenTarget(item, canOpenAdminInstanceDetail: false)
            .Should().BeNull();
    }

    [Fact]
    public void PersonalNotifications_page_uses_resolve_open_target()
    {
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Me/PersonalNotifications.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("PersonalNotificationUiRoutes.ResolveOpenTarget");
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
