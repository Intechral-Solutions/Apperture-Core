using System.Linq;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class ShellSectionRoutesTests
{
    [Fact]
    public void ControlCenterHome_MatchesMenuControlCenter()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var item = menus[SectionOwnerConstants.ControlCenter].Items.First(i => i.Text == "Control Center");
        item.Href.Should().Be(ShellSectionRoutes.ControlCenterHome);
    }

    [Fact]
    public void LogoAndMenus_UseSameControlCenterLanding()
    {
        ShellSectionRoutes.ControlCenterHome.Should().Be("/control/center");
        ShellSectionRoutes.DashboardHome.Should().Be("/dashboard");
    }
}
