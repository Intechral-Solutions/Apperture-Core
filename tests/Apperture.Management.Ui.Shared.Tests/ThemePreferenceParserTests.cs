using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class ThemePreferenceParserTests
{
    [Theory]
    [InlineData(null, ThemePreference.System)]
    [InlineData("", ThemePreference.System)]
    [InlineData("   ", ThemePreference.System)]
    [InlineData("Light", ThemePreference.Light)]
    [InlineData("light", ThemePreference.Light)]
    [InlineData("Dark", ThemePreference.Dark)]
    [InlineData("dArK", ThemePreference.Dark)]
    [InlineData("System", ThemePreference.System)]
    [InlineData("not-a-theme", ThemePreference.System)]
    public void ParseOrDefault_maps_storage_to_enum(string? stored, ThemePreference expected) =>
        ThemePreferenceParser.ParseOrDefault(stored).Should().Be(expected);

    [Fact]
    public void ToStorageValue_roundtrips_parseable_values()
    {
        foreach (var p in Enum.GetValues<ThemePreference>())
        {
            var s = ThemePreferenceParser.ToStorageValue(p);
            ThemePreferenceParser.ParseOrDefault(s).Should().Be(p);
        }
    }

    [Fact]
    public void StorageKey_is_stable_for_layout_script_alignment() =>
        ThemePreferenceParser.StorageKey.Should().Be("apperture.theme");
}
