using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

/// <summary>
/// AUD-1.4 slice 3, work package 4a: secret references are compared as GUIDs, not as text. The reader accepts what the platform's
/// own provisioners and resolver accept (<see cref="Guid.TryParse(string?, out Guid)"/> after trimming) and, for values that are
/// not what their column should hold, reports every GUID-looking token so that unexpected data can only make a secret be retained.
/// </summary>
public sealed class SecretReferenceReaderTests
{
    private static readonly Guid Secret = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");

    public static IEnumerable<object[]> Formats()
    {
        foreach (var format in new[] { "D", "N", "B", "P", "X" })
        {
            yield return [Secret.ToString(format), format];
            yield return [Secret.ToString(format).ToUpperInvariant().Replace("0X", "0x", StringComparison.Ordinal), format + " upper"];
        }

        yield return [$"  {Secret:D}  ", "padded"];
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Every_format_and_case_of_a_reference_is_the_same_guid_in_a_json_string_and_in_plain_text(string text, string label)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(text);

        var fromJson = SecretReferenceReader.ReadJson(json);
        var fromText = SecretReferenceReader.ReadText(text);

        fromJson.Shape.Should().Be(SecretReferenceShape.Guid, label);
        fromJson.Guids.Should().Equal(Secret);
        fromText.Shape.Should().Be(SecretReferenceShape.Guid, label);
        fromText.Guids.Should().Equal(Secret);
    }

    [Fact]
    public void A_connection_secret_refs_object_is_read_by_its_values_and_not_its_names()
    {
        var other = Guid.NewGuid();
        var json = $$"""{"ApiKey":"{{Secret:N}}","Password":"{{other:B}}","{{Secret:D}}":"not a reference"}""";

        var reading = SecretReferenceReader.ReadJson(json);

        reading.Shape.Should().Be(SecretReferenceShape.Structured);
        reading.Guids.Should().BeEquivalentTo(new[] { Secret, other });
    }

    [Fact]
    public void An_empty_object_an_empty_text_and_a_null_hold_no_reference()
    {
        SecretReferenceReader.ReadJson("{}").Guids.Should().BeEmpty();
        SecretReferenceReader.ReadJson("\"\"").Shape.Should().Be(SecretReferenceShape.None);
        SecretReferenceReader.ReadJson("\"   \"").Shape.Should().Be(SecretReferenceShape.None);
        SecretReferenceReader.ReadJson("null").Shape.Should().Be(SecretReferenceShape.None);
        SecretReferenceReader.ReadJson("").Shape.Should().Be(SecretReferenceShape.None);
        SecretReferenceReader.ReadJson(null).Shape.Should().Be(SecretReferenceShape.None);
        SecretReferenceReader.ReadText(null).Shape.Should().Be(SecretReferenceShape.None);
        SecretReferenceReader.ReadText("  ").Shape.Should().Be(SecretReferenceShape.None);
    }

    [Fact]
    public void A_number_or_a_boolean_cannot_hold_a_reference()
    {
        SecretReferenceReader.ReadJson("42").Shape.Should().Be(SecretReferenceShape.None);
        SecretReferenceReader.ReadJson("true").Shape.Should().Be(SecretReferenceShape.None);
    }

    [Fact]
    public void An_array_of_references_reports_each()
    {
        var other = Guid.NewGuid();

        var reading = SecretReferenceReader.ReadJson($"[\"{Secret:D}\",\"{other:N}\",\"{Secret:B}\"]");

        reading.Shape.Should().Be(SecretReferenceShape.Structured);
        reading.Guids.Should().BeEquivalentTo(new[] { Secret, other }, "a repeated reference is reported once");
    }

    [Fact]
    public void Text_that_embeds_a_reference_still_reports_it_so_unexpected_data_can_only_retain_a_secret()
    {
        var embedded = SecretReferenceReader.ReadJson($"\"ref={Secret:D};other\"");
        var malformed = SecretReferenceReader.ReadJson($"{{not json {Secret:N} ");
        var entra = SecretReferenceReader.ReadText($"secret:{Secret:D}");

        embedded.Shape.Should().Be(SecretReferenceShape.OtherText);
        embedded.Guids.Should().Equal(Secret);
        malformed.Shape.Should().Be(SecretReferenceShape.Malformed);
        malformed.Guids.Should().Equal(Secret);
        entra.Shape.Should().Be(SecretReferenceShape.OtherText);
        entra.Guids.Should().Equal(Secret);
    }

    [Fact]
    public void A_longer_hex_run_or_a_partial_guid_is_not_a_reference()
    {
        var longer = SecretReferenceReader.ReadText(Secret.ToString("N") + "ab");
        var partial = SecretReferenceReader.ReadText(Secret.ToString("D")[..30]);
        var notJson = SecretReferenceReader.ReadJson("some words");

        longer.Guids.Should().BeEmpty("a 34-digit run is not a 32-digit GUID token");
        partial.Guids.Should().BeEmpty();
        notJson.Shape.Should().Be(SecretReferenceShape.Malformed);
        notJson.Guids.Should().BeEmpty();
    }

    [Fact]
    public void TryReadGuid_trims_and_refuses_empty_text()
    {
        SecretReferenceReader.TryReadGuid($" {Secret:N}\t", out var guid).Should().BeTrue();
        guid.Should().Be(Secret);
        SecretReferenceReader.TryReadGuid("", out _).Should().BeFalse();
        SecretReferenceReader.TryReadGuid(null, out _).Should().BeFalse();
        SecretReferenceReader.TryReadGuid("not-a-guid", out _).Should().BeFalse();
    }
}
