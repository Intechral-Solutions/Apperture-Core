using Apperture.Core.Application.PlatformSettings;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class Epic114PlatformSettingsValueCodecTests
{
    [Fact]
    public void String_roundtrip_parse_store_and_display()
    {
        var def = PlatformSettingTestDefinitions.ProviderBackedSelectSample;

        var parsed = PlatformSettingsValueCodec.ParseFromInput(def, "alpha");
        parsed.Should().Be("alpha");

        var json = PlatformSettingsValueCodec.ToStoredJson(def, parsed);
        var fromStore = PlatformSettingsValueCodec.ParseFromStoredJson(def, json);
        fromStore.Should().Be("alpha");

        PlatformSettingsValueCodec.ToDisplayString(fromStore).Should().Be("alpha");
    }
}
