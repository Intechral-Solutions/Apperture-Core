using System.Linq;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class ShellSectionRoutesTests
{
    [Fact]
    public void DashboardHome_MatchesMenuDashboardHome()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var item = menus[SectionOwnerConstants.Dashboard].Items.First(i => i.Text == "Dashboard home");
        item.Href.Should().Be(ShellSectionRoutes.DashboardHome);
    }

    [Fact]
    public void LogoAndMenus_UseSameDashboardLanding_AsCanonicalEntry()
    {
        ShellSectionRoutes.DashboardHome.Should().Be("/dashboard");
    }
}
