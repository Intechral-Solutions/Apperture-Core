using System.Text.Json;
using System.Text.RegularExpressions;

namespace Apperture.ControlPlane.Security;

/// <summary>What a stored value that may hold secret references looked like.</summary>
internal enum SecretReferenceShape
{
    /// <summary>Nothing: no value, an empty or whitespace text, or a JSON null. There is no reference to find.</summary>
    None,

    /// <summary>One text that is a GUID in a format <see cref="Guid.TryParse(string?, out Guid)"/> accepts.</summary>
    Guid,

    /// <summary>One JSON text that is not itself a GUID. Any GUID embedded in it is still reported.</summary>
    OtherText,

    /// <summary>A JSON array or object. Every string in it (an object's values, not its names) is read.</summary>
    Structured,

    /// <summary>Text that is not valid JSON. Any GUID-looking token in it is still reported.</summary>
    Malformed,
}

/// <summary>The GUIDs one stored value references, and the shape they were found in.</summary>
internal readonly record struct SecretReferenceReading(SecretReferenceShape Shape, IReadOnlyList<Guid> Guids)
{
    public static SecretReferenceReading Nothing { get; } = new(SecretReferenceShape.None, []);
}

/// <summary>
/// Reads secret references the way the repository accepts them: as a GUID compared by value, not as text. A reference is a
/// text that <see cref="Guid.TryParse(string?, out Guid)"/> accepts after trimming, so the formats <c>D</c>, <c>N</c>, <c>B</c>,
/// <c>P</c> and <c>X</c>, upper or lower case, are all the same reference (the platform's own provisioners and resolver parse them
/// this way). Nothing here compares serialized JSON or raw text as identity, and nothing narrows a scan by text.
/// </summary>
/// <remarks>
/// <para>Where a value is not what its column should hold (not valid JSON, an array where a text was expected, a text with a GUID
/// embedded in other characters), the reader still reports every GUID-looking token in it. That is deliberately conservative:
/// a stray token that happens to equal a candidate secret is read as a reference, which can only make a secret be retained, never
/// retired.</para>
/// <para><b>Limit.</b> The leniency covers the formats <c>D</c>, <c>N</c>, <c>B</c> and <c>P</c> (a hyphenated GUID, or 32 hex
/// digits, with or without braces or parentheses). A format <c>X</c> GUID (<c>{0x…,0x…,{0x…}}</c>) is read when it is the whole
/// value or a whole JSON string, but not when embedded in other text.</para>
/// </remarks>
internal static partial class SecretReferenceReader
{
    /// <summary>True when <paramref name="text"/> is, after trimming, a GUID in a format the platform accepts.</summary>
    public static bool TryReadGuid(string? text, out Guid guid)
    {
        guid = Guid.Empty;
        return !string.IsNullOrWhiteSpace(text) && Guid.TryParse(text.Trim(), out guid);
    }

    /// <summary>Reads a stored setting value (<c>PlatformSettingValues.ValueJson</c>, a JSON document).</summary>
    public static SecretReferenceReading ReadJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return SecretReferenceReading.Nothing;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new SecretReferenceReading(SecretReferenceShape.Malformed, Tokens(json));
        }

        using (document)
        {
            var root = document.RootElement;
            switch (root.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return SecretReferenceReading.Nothing;
                case JsonValueKind.String:
                {
                    var text = root.GetString();
                    if (string.IsNullOrWhiteSpace(text))
                        return SecretReferenceReading.Nothing;
                    return TryReadGuid(text, out var guid)
                        ? new SecretReferenceReading(SecretReferenceShape.Guid, [guid])
                        : new SecretReferenceReading(SecretReferenceShape.OtherText, Tokens(text));
                }

                case JsonValueKind.Array:
                case JsonValueKind.Object:
                {
                    var found = new List<Guid>();
                    Collect(root, found);
                    return new SecretReferenceReading(SecretReferenceShape.Structured, Distinct(found));
                }

                default:
                    // A number or a boolean cannot hold a reference.
                    return SecretReferenceReading.Nothing;
            }
        }
    }

    /// <summary>
    /// Reads a plain text column (<c>EntraInstanceRegistrations.ClientSecretRef</c>), which is a GUID text and not a JSON document.
    /// </summary>
    public static SecretReferenceReading ReadText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SecretReferenceReading.Nothing;

        return TryReadGuid(text, out var guid)
            ? new SecretReferenceReading(SecretReferenceShape.Guid, [guid])
            : new SecretReferenceReading(SecretReferenceShape.OtherText, Tokens(text));
    }

    private static void Collect(JsonElement element, List<Guid> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                var text = element.GetString();
                if (TryReadGuid(text, out var guid))
                    found.Add(guid);
                else if (!string.IsNullOrEmpty(text))
                    found.AddRange(Tokens(text));
                break;
            }

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, found);
                break;
            case JsonValueKind.Object:
                // The values hold references; the names are field keys.
                foreach (var property in element.EnumerateObject())
                    Collect(property.Value, found);
                break;
        }
    }

    private static List<Guid> Tokens(string text)
    {
        var found = new List<Guid>();
        foreach (Match match in GuidToken().Matches(text))
        {
            if (Guid.TryParse(match.Value, out var guid))
                found.Add(guid);
        }

        return Distinct(found);
    }

    private static List<Guid> Distinct(List<Guid> guids) => guids.Distinct().ToList();

    // A hyphenated GUID, or exactly 32 hex digits not part of a longer run. Braces and parentheses around either are not part of
    // the token, and their presence does not change which GUID it is.
    [GeneratedRegex(
        "(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}|[0-9A-Fa-f]{32})(?![0-9A-Fa-f])",
        RegexOptions.CultureInvariant)]
    private static partial Regex GuidToken();
}
