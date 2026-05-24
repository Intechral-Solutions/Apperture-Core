using System.Globalization;
using System.Text.Json;

namespace Apperture.Core.Application.PlatformSettings;

public static class PlatformSettingsValueCodec
{
    public static object ParseFromInput(PlatformSettingDefinition definition, string rawValue)
    {
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

    public static object ParseFromStoredJson(PlatformSettingDefinition definition, string valueJson)
    {
        return definition.ValueType switch
        {
            PlatformSettingValueType.Boolean => JsonSerializer.Deserialize<bool>(valueJson),
            PlatformSettingValueType.Integer => JsonSerializer.Deserialize<int>(valueJson),
            PlatformSettingValueType.Duration => TimeSpan.Parse(
                JsonSerializer.Deserialize<string>(valueJson) ?? string.Empty,
                CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Unsupported value type {definition.ValueType}.")
        };
    }

    public static string ToStoredJson(PlatformSettingDefinition definition, object value) =>
        definition.ValueType switch
        {
            PlatformSettingValueType.Boolean => JsonSerializer.Serialize((bool)value),
            PlatformSettingValueType.Integer => JsonSerializer.Serialize((int)value),
            PlatformSettingValueType.Duration => JsonSerializer.Serialize(
                ((TimeSpan)value).ToString("c", CultureInfo.InvariantCulture)),
            _ => throw new InvalidOperationException($"Unsupported value type {definition.ValueType}.")
        };

    public static string ToDisplayString(object value) =>
        value switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString("c", CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

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
