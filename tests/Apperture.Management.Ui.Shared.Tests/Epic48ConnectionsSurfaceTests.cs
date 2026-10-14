using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Connections;

/// <summary>
/// Epic 48: Connections Surface and Used In Foundation.
/// </summary>
public sealed class Epic48ConnectionsSurfaceTests
{
    [Fact]
    public void Connections_AreOffRail_DiscoveredFromInventory()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().NotContainKey("Connections");
        menus.Should().ContainKey(SectionOwnerConstants.Config);
        menus[SectionOwnerConstants.Config].Items.Should().Contain(i => i.Href == ShellSectionRoutes.Inventory);
    }

    [Fact]
    public void Clients_NotSeparateDomain_ConsolidatedIntoTab()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().NotContainKey("Clients");
    }

    [Fact]
    public void ConnectionDto_UsedIn_Shape()
    {
        var usedIn = new[]
        {
            new ConnectionUsedInEntry("ModuleA", "DbSlot", "System.Data.IDbConnection")
        };
        var dto = new ConnectionDto(
            "conn-1",
            "connector-sql",
            "My DB",
            new Dictionary<string, string>(),
            new List<ConnectionSecretFieldDto>(),
            null,
            null,
            "Active",
            false,
            "NeverTested",
            null,
            null,
            usedIn);
        dto.Id.Should().Be("conn-1");
        dto.UsedIn.Should().HaveCount(1);
        dto.UsedIn[0].ModuleId.Should().Be("ModuleA");
        dto.UsedIn[0].SlotName.Should().Be("DbSlot");
        dto.UsedIn[0].ContractType.Should().Be("System.Data.IDbConnection");
    }

    [Fact]
    public void OperatorCapabilitiesDto_CanViewConnections()
    {
        var dto = new OperatorCapabilitiesDto(
            false, false, false, false, false, false, false, false, false, false, false, true);
        dto.CanViewConnections.Should().BeTrue();
    }

    [Fact]
    public void OperatorCapabilitiesDto_ConnectionAuthoringFlags()
    {
        var dto = new OperatorCapabilitiesDto(
            false, false, false, false, false,
            false, false, false, false, false, false, false,
            false, false, false,
            CanCreateConnections: true,
            CanUpdateConnections: true,
            CanRetireConnections: false);
        dto.CanCreateConnections.Should().BeTrue();
        dto.CanUpdateConnections.Should().BeTrue();
        dto.CanRetireConnections.Should().BeFalse();
    }

    [Fact]
    public void ConnectorDefinitionCatalogDto_Shape_Epic52()
    {
        var fields = new[]
        {
            new ConnectionFieldCatalogDto("BaseUrl", "API base URL", false, "Root URL")
        };
        var dto = new ConnectorDefinitionCatalogDto(
            "Apperture.Platform.CampusNexus",
            "Campus Nexus API",
            null,
            "HTTP API connector.",
            fields,
            "None");
        dto.Id.Should().Be("Apperture.Platform.CampusNexus");
        dto.Name.Should().Be("Campus Nexus API");
        dto.ModuleId.Should().BeNull();
        dto.Description.Should().Contain("HTTP");
        dto.Fields.Should().HaveCount(1);
        dto.Fields[0].Key.Should().Be("BaseUrl");
        dto.Fields[0].IsSecret.Should().BeFalse();
        dto.Capabilities.Should().Be("None");
    }
}
