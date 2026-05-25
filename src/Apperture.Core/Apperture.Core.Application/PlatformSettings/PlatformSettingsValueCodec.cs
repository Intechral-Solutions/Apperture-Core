using System.Globalization;
using System.Text.Json;

namespace Apperture.Core.Application.PlatformSettings;

public static class PlatformSettingsValueCodec
{
    public static object ParseFromInput(PlatformSettingDefinition definition, string rawValue)
    {
        if (definition.ValueType == PlatformSettingValueType.StringArray)
            throw new ArgumentException("StringArray settings require the values array payload.", nameof(rawValue));

        if (string.IsNullOrWhiteSpace(rawValue))
            throw new ArgumentException("Value is required.", nameof(rawValue));

        return definition.ValueType switch
        {
            PlatformSettingValueType.Boolean => ParseBool(rawValue),
            PlatformSettingValueType.Integer => ParseInt(rawValue, definition),
            PlatformSettingValueType.Duration => ParseDuration(rawValue, definition),
            _ => throw new InvalidOperationException($"Unsupported value type {definition.ValueType}.")
        };
    }

    public static string[] ParseFromArrayInput(PlatformSettingDefinition definition, IReadOnlyList<string>? values)
    {
        if (definition.ValueType != PlatformSettingValueType.StringArray)
            throw new ArgumentException($"Setting '{definition.Key}' does not accept array values.", nameof(values));

        return PlatformSettingsAllowedValuesValidator.ValidateAndNormalizeArray(values, definition);
    }

    public static object ParseFromStoredJson(PlatformSettingDefinition definition, string valueJson)
    {
        try
        {
            return definition.ValueType switch
            {
                PlatformSettingValueType.Boolean => JsonSerializer.Deserialize<bool>(valueJson),
                PlatformSettingValueType.Integer => JsonSerializer.Deserialize<int>(valueJson),
                PlatformSettingValueType.Duration => TimeSpan.Parse(
                    JsonSerializer.Deserialize<string>(valueJson) ?? string.Empty,
                    CultureInfo.InvariantCulture),
                PlatformSettingValueType.StringArray => ParseStoredStringArray(valueJson),
                _ => throw new InvalidOperationException($"Unsupported value type {definition.ValueType}.")
            };
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Stored value is not valid JSON for this setting type.", ex);
        }
    }

    public static string ToStoredJson(PlatformSettingDefinition definition, object value) =>
        definition.ValueType switch
        {
            PlatformSettingValueType.Boolean => JsonSerializer.Serialize((bool)value),
            PlatformSettingValueType.Integer => JsonSerializer.Serialize((int)value),
            PlatformSettingValueType.Duration => JsonSerializer.Serialize(
                ((TimeSpan)value).ToString("c", CultureInfo.InvariantCulture)),
            PlatformSettingValueType.StringArray => JsonSerializer.Serialize(CoerceStringArray(value)),
            _ => throw new InvalidOperationException($"Unsupported value type {definition.ValueType}.")
        };

    public static string ToDisplayString(object value) =>
        value switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString("c", CultureInfo.InvariantCulture),
            string[] items => FormatStringArrayDisplay(items),
            IReadOnlyList<string> readOnlyItems => FormatStringArrayDisplay(readOnlyItems),
            IEnumerable<string> items when value is not string => FormatStringArrayDisplay(items),
            _ => value.ToString() ?? string.Empty
        };

    public static string[] CoerceStringArray(object value) =>
        value switch
        {
            string[] array => array,
            IReadOnlyList<string> readOnly => readOnly.ToArray(),
            IEnumerable<string> enumerable => enumerable.ToArray(),
            _ => throw new ArgumentException("Expected a string array value.", nameof(value))
        };

    private static string[] ParseStoredStringArray(string valueJson)
    {
        var parsed = JsonSerializer.Deserialize<string[]>(valueJson);
        if (parsed is null)
            throw new ArgumentException("Stored value must be a JSON string array.");

        return parsed;
    }

    private static string FormatStringArrayDisplay(IEnumerable<string> items)
    {
        var values = items.ToArray();
        if (values.Length == 0)
            return "[]";

        return $"[{string.Join(", ", values)}]";
    }

    private static bool ParseBool(string rawValue) =>
        bool.TryParse(rawValue, out var b)
            ? b
            : throw new ArgumentException("Expected true or false.", nameof(rawValue));

    private static int ParseInt(string rawValue, PlatformSettingDefinition definition)
    {
        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException("Expected an integer.", nameof(rawValue));

        if (definition.MinInteger is { } min && value < min)
            throw new ArgumentException($"Value must be at least {min}.", nameof(rawValue));

        if (definition.MaxInteger is { } max && value > max)
            throw new ArgumentException($"Value must be at most {max}.", nameof(rawValue));

        return value;
    }

    private static TimeSpan ParseDuration(string rawValue, PlatformSettingDefinition definition)
    {
        if (!TimeSpan.TryParse(rawValue, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException("Expected an ISO 8601 duration (e.g. 00:05:00).", nameof(rawValue));

        if (definition.MinDuration is { } min && value < min)
            throw new ArgumentException($"Duration must be at least {min:c}.", nameof(rawValue));

        return value;
    }
}
