using System.Text.Json;

namespace Apperture.Core.Application.Validation;

/// <summary>
/// Parses and validates the shared JSON parameter schema (<c>fields[]</c>) used by operations and procedures.
/// </summary>
public static class OrchestrationParameterSchemaValidator
{
    public sealed record ParameterFieldDescriptor(
        string Key,
        string Type,
        bool Required,
        IReadOnlyList<string>? AllowedValues,
        decimal? Min,
        decimal? Max,
        int? MaxLength,
        string? Help,
        string? Label,
        string? DefaultJson);

    public static bool TryParseFields(string? schemaJson, out IReadOnlyList<ParameterFieldDescriptor> fields)
    {
        fields = Array.Empty<ParameterFieldDescriptor>();
        if (string.IsNullOrWhiteSpace(schemaJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(schemaJson);
            if (!doc.RootElement.TryGetProperty("fields", out var array) || array.ValueKind != JsonValueKind.Array)
                return false;

            var list = new List<ParameterFieldDescriptor>();
            foreach (var item in array.EnumerateArray())
            {
                if (!item.TryGetProperty("key", out var keyEl))
                    continue;
                var key = keyEl.GetString();
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                var type = item.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "string" : "string";
                var required = item.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.True;

                List<string>? values = null;
                if (item.TryGetProperty("allowedValues", out var allowed) && allowed.ValueKind == JsonValueKind.Array)
                    values = allowed.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList();

                decimal? min = item.TryGetProperty("min", out var minEl) && minEl.TryGetDecimal(out var minVal) ? minVal : null;
                decimal? max = item.TryGetProperty("max", out var maxEl) && maxEl.TryGetDecimal(out var maxVal) ? maxVal : null;
                int? maxLength = item.TryGetProperty("maxLength", out var lenEl) && lenEl.TryGetInt32(out var l) ? l : null;
                var help = item.TryGetProperty("help", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
                var label = item.TryGetProperty("label", out var lab) && lab.ValueKind == JsonValueKind.String ? lab.GetString() : null;
                string? defaultJson = null;
                if (item.TryGetProperty("default", out var defEl))
                    defaultJson = defEl.GetRawText();

                list.Add(new ParameterFieldDescriptor(key, type, required, values, min, max, maxLength, help, label, defaultJson));
            }

            fields = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Merges declared defaults into <paramref name="parameters"/> (copy-on-write).
    /// </summary>
    public static IReadOnlyDictionary<string, object?> MergeDefaults(
        IReadOnlyList<ParameterFieldDescriptor> fields,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        var dict = parameters is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(parameters, StringComparer.Ordinal);

        foreach (var f in fields)
        {
            if (string.IsNullOrEmpty(f.DefaultJson) || dict.ContainsKey(f.Key))
                continue;

            if (TryCoerceDefault(f.Type, f.DefaultJson, out var coerced))
                dict[f.Key] = coerced;
        }

        return dict;
    }

    public static void Validate(string? schemaJson, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            return;

        if (!TryParseFields(schemaJson, out var fields))
            return;

        var merged = MergeDefaults(fields, parameters);
        foreach (var field in fields)
        {
            merged.TryGetValue(field.Key, out var value);
            if (value is null)
            {
                if (field.Required)
                    throw new ArgumentException($"Missing required field '{field.Key}'.");
                continue;
            }

            if (!IsValueCompatible(field.Type, value))
                throw new ArgumentException($"Invalid value type for field '{field.Key}'. Expected '{field.Type}'.");

            if (field.MaxLength is not null && value is string text && text.Length > field.MaxLength.Value)
                throw new ArgumentException($"Field '{field.Key}' exceeds maxLength {field.MaxLength.Value}.");

            if (field.AllowedValues is { Count: > 0 })
            {
                var valueText = value.ToString();
                if (!field.AllowedValues.Contains(valueText, StringComparer.Ordinal))
                    throw new ArgumentException($"Field '{field.Key}' must be one of: {string.Join(", ", field.AllowedValues)}.");
            }

            if (TryToDecimal(value, out var numeric))
            {
                if (field.Min is not null && numeric < field.Min.Value)
                    throw new ArgumentException($"Field '{field.Key}' must be >= {field.Min.Value}.");
                if (field.Max is not null && numeric > field.Max.Value)
                    throw new ArgumentException($"Field '{field.Key}' must be <= {field.Max.Value}.");
            }
        }
    }

    private static bool TryCoerceDefault(string type, string defaultJson, out object? value)
    {
        value = null;
        try
        {
            using var doc = JsonDocument.Parse(defaultJson);
            var el = doc.RootElement;
            switch (type.ToLowerInvariant())
            {
                case "string":
                case "enum":
                case "json":
                case "datetime":
                    if (el.ValueKind != JsonValueKind.String)
                        return false;
                    value = el.GetString();
                    return true;
                case "bool":
                    if (el.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return false;
                    value = el.GetBoolean();
                    return true;
                case "int":
                    if (!el.TryGetInt32(out var i))
                        return false;
                    value = i;
                    return true;
                case "decimal":
                    if (!el.TryGetDecimal(out var d))
                        return false;
                    value = d;
                    return true;
                default:
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        value = el.GetString();
                        return true;
                    }

                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValueCompatible(string type, object value)
    {
        return type.ToLowerInvariant() switch
        {
            "string" => value is string,
            "int" => value is int or long or short or byte,
            "decimal" => value is decimal or double or float or int or long,
            "bool" => value is bool,
            "datetime" => value is DateTimeOffset or DateTime or string,
            "enum" => value is string,
            "json" => value is string,
            _ => true
        };
    }

    private static bool TryToDecimal(object value, out decimal numeric)
    {
        switch (value)
        {
            case decimal d:
                numeric = d;
                return true;
            case double db:
                numeric = (decimal)db;
                return true;
            case float f:
                numeric = (decimal)f;
                return true;
            case int i:
                numeric = i;
                return true;
            case long l:
                numeric = l;
                return true;
            case short s:
                numeric = s;
                return true;
            default:
                numeric = 0;
                return false;
        }
    }
}
