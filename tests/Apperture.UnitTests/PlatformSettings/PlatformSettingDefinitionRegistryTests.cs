using Apperture.Core.Application.PlatformSettings;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class PlatformSettingDefinitionRegistryTests
{
    [Fact]
    public void Registry_contains_phase1_nine_keys()
    {
        var sut = new PlatformSettingDefinitionRegistry();

        var defs = sut.GetAll();

        defs.Should().HaveCount(9);
        defs.Select(x => x.Key).Should().OnlyHaveUniqueItems();
        sut.TryGet(PlatformSettingKeys.PlatformEventsEnabled).Should().NotBeNull();
    }
}
