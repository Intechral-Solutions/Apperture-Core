using Apperture.Connectors.CampusNexus.Models;
using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class CampusNexusCampusesOptionProviderMappingTests
{
    [Fact]
    public void MapCampusOption_uses_id_as_value_and_name_as_label()
    {
        var option = CampusNexusCampusesOptionProvider.MapCampusOption(new CampusODataRow
        {
            Id = 42,
            Name = "North Campus",
            Code = "NORTH"
        });

        option.Value.Should().Be("42");
        option.Label.Should().Be("North Campus");
        option.Description.Should().Be("NORTH");
    }

    [Fact]
    public void MapCampusOption_falls_back_to_code_then_id_when_name_missing()
    {
        var fromCode = CampusNexusCampusesOptionProvider.MapCampusOption(new CampusODataRow
        {
            Id = 7,
            Code = "WEST"
        });
        fromCode.Value.Should().Be("7");
        fromCode.Label.Should().Be("WEST");
        fromCode.Description.Should().BeNull();

        var fromId = CampusNexusCampusesOptionProvider.MapCampusOption(new CampusODataRow { Id = 3 });
        fromId.Label.Should().Be("Campus 3");
    }
}
