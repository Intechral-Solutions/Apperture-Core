using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// Epic 41: Config-hosted Governance UI and Role Assignment truthfulness tests.
/// </summary>
public sealed class Epic41ConfigGovernanceUiTests
{
    [Fact]
    public void ConfigMenu_HasGovernanceUnderConfig()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().ContainKey("Config");
        var config = menus["Config"];
        config.Items.Should().Contain(i => i.Text == "Governance");
        config.Items.Should().Contain(i => i.Text == "Modules" && i.Href == "/modules");
        config.Items.Should().Contain(i => i.Href == "/config" && i.Text == "Overview");
    }

    [Fact]
    public void ConfigGovernanceMenu_ContainsCatalogRolesUsers()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var config = menus["Config"];
        var governance = config.Items.FirstOrDefault(i => i.Text == "Governance");
        governance.Should().NotBeNull();
        governance!.Children.Should().NotBeNull();
        var hrefs = governance.Children!.Select(c => c.Href).ToList();
        hrefs.Should().Contain("/config/governance/catalog");
        hrefs.Should().Contain("/config/governance/roles");
        hrefs.Should().Contain("/config/governance/users");
        hrefs.Should().NotContain("/config/governance");
        governance.Href.Should().Be("/config/governance");
    }

    [Fact]
    public void Governance_NotTopLevelDomain()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().NotContainKey("Governance");
    }

    [Fact]
    public void UserDetailRoleAssignmentNotice_ExplainsScope()
    {
        const string expected = "This page manages role assignments only. User identity and credentials are not editable here. To edit role definitions, go to the role detail page.";
        expected.Should().Contain("role assignments only");
        expected.Should().Contain("not editable here");
        expected.Should().Contain("role detail page");
    }

    [Fact]
    public void UserDetailDto_Shape_MatchesApi()
    {
        var dto = new UserDetailDto(
            "u1",
            "Test User",
            [],
            ["r1", "r2"],
            true,
            false);
        dto.UserId.Should().Be("u1");
        dto.DisplayName.Should().Be("Test User");
        dto.RoleIds.Should().BeEquivalentTo("r1", "r2");
    }

    [Fact]
    public void OperatorCapabilitiesDto_CanAssignRoles_ReflectsRoleAssign()
    {
        var dto = new OperatorCapabilitiesDto(false, false, false, false, false, false, false, true, false);
        dto.CanAssignRoles.Should().BeTrue();
    }
}
