using Apperture.Core.Application.PlatformSettings;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class Epic112PlatformSettingsValueCodecTests
{
    [Fact]
    public void ParseFromArrayInput_accepts_valid_selection()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;

        var parsed = PlatformSettingsValueCodec.ParseFromArrayInput(def, ["alpha", "beta"]);

        parsed.Should().Equal("alpha", "beta");
    }

    [Fact]
    public void ParseFromArrayInput_allows_empty_array_when_min_items_zero()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;

        var parsed = PlatformSettingsValueCodec.ParseFromArrayInput(def, []);

        parsed.Should().BeEmpty();
    }

    [Fact]
    public void ParseFromArrayInput_rejects_duplicate_values()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;

        var act = () => PlatformSettingsValueCodec.ParseFromArrayInput(def, ["alpha", "alpha"]);

        act.Should().Throw<ArgumentException>().WithMessage("*Duplicate*");
    }

    [Fact]
    public void ParseFromArrayInput_rejects_invalid_option()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;

        var act = () => PlatformSettingsValueCodec.ParseFromArrayInput(def, ["alpha", "missing"]);

        act.Should().Throw<ArgumentException>().WithMessage("*not allowed*");
    }

    [Fact]
    public void ParseFromArrayInput_rejects_too_many_items()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;

        var act = () => PlatformSettingsValueCodec.ParseFromArrayInput(def, ["alpha", "beta", "gamma"]);

        act.Should().Throw<ArgumentException>().WithMessage("*At most 2*");
    }

    [Fact]
    public void ParseFromInput_rejects_scalar_for_string_array()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;

        var act = () => PlatformSettingsValueCodec.ParseFromInput(def, "alpha");

        act.Should().Throw<ArgumentException>().WithMessage("*values array*");
    }

    [Fact]
    public void ToStoredJson_and_ParseFromStoredJson_round_trip_string_array()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;
        var value = new[] { "alpha", "beta" };

        var json = PlatformSettingsValueCodec.ToStoredJson(def, value);
        var parsed = PlatformSettingsValueCodec.ParseFromStoredJson(def, json);

        parsed.Should().BeEquivalentTo(value);
    }

    [Fact]
    public void ToDisplayString_formats_string_array_readably()
    {
        PlatformSettingsValueCodec.ToDisplayString(Array.Empty<string>()).Should().Be("[]");
        PlatformSettingsValueCodec.ToDisplayString(new[] { "alpha", "beta" }).Should().Be("[alpha, beta]");
    }
}
