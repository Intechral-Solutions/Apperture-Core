namespace Apperture.Core.Application.PlatformSettings;

public static class PlatformSettingsAllowedValuesValidator
{
    public static void ValidateSingle(string value, PlatformSettingDefinition definition)
    {
        if (definition.AllowedValues is not { Count: > 0 })
            return;

        if (definition.AllowedValues.Any(v => string.Equals(v.Value, value, StringComparison.Ordinal)))
            return;

        throw new ArgumentException(FormatInvalidValueMessage(value, definition.AllowedValues));
    }

    public static string[] ValidateAndNormalizeArray(
        IReadOnlyList<string>? values,
        PlatformSettingDefinition definition)
    {
        var normalized = NormalizeArray(values);

        if (HasDuplicates(normalized))
            throw new ArgumentException("Duplicate values are not allowed.");

        ValidateItemCount(normalized, definition);

        if (definition.AllowedValues is { Count: > 0 })
        {
            foreach (var value in normalized)
            {
                if (definition.AllowedValues.All(v => !string.Equals(v.Value, value, StringComparison.Ordinal)))
                    throw new ArgumentException(FormatInvalidValueMessage(value, definition.AllowedValues));
            }
        }

        return normalized;
    }

    private static string[] NormalizeArray(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            return [];

        return values.Select(v => v?.Trim() ?? string.Empty).ToArray();
    }

    private static bool HasDuplicates(string[] values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!seen.Add(value))
                return true;
        }

        return false;
    }

    private static void ValidateItemCount(string[] values, PlatformSettingDefinition definition)
    {
        if (definition.MinItems is { } min && values.Length < min)
            throw new ArgumentException($"At least {min} value(s) are required.");

        if (definition.MaxItems is { } max && values.Length > max)
            throw new ArgumentException($"At most {max} value(s) are allowed.");
    }

    private static string FormatInvalidValueMessage(
        string value,
        IReadOnlyList<PlatformSettingAllowedValue> allowedValues) =>
        $"Value '{value}' is not allowed. Allowed: {string.Join(", ", allowedValues.Select(v => v.Value))}.";
}
