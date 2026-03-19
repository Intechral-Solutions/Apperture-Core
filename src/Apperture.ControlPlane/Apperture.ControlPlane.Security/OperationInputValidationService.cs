using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;

namespace Apperture.ControlPlane.Security;

public sealed class OperationInputValidationService : IOperationInputValidationService
{
    private readonly IOperationRegistryService _operations;

    public OperationInputValidationService(IOperationRegistryService operations)
    {
        _operations = operations;
    }

    public async ValueTask ValidateAsync(OperationId operationId, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken = default)
    {
        var definition = await _operations.GetOperationAsync(operationId, cancellationToken);
        if (definition is null || string.IsNullOrWhiteSpace(definition.ParameterSchema))
            return;

        if (!TryParseFields(definition.ParameterSchema, out var fields))
            return;

        foreach (var field in fields)
        {
            object? value = null;
            parameters?.TryGetValue(field.Key, out value);
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

    private static bool TryParseFields(string schemaJson, out IReadOnlyList<ParsedField> fields)
    {
        try
        {
            using var doc = JsonDocument.Parse(schemaJson);
            if (!doc.RootElement.TryGetProperty("fields", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                fields = Array.Empty<ParsedField>();
                return false;
            }

            var list = new List<ParsedField>();
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

                list.Add(new ParsedField(key, type, required, values, min, max, maxLength));
            }

            fields = list;
            return true;
        }
        catch (JsonException)
        {
            fields = Array.Empty<ParsedField>();
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

    private sealed record ParsedField(
        string Key,
        string Type,
        bool Required,
        IReadOnlyList<string>? AllowedValues,
        decimal? Min,
        decimal? Max,
        int? MaxLength);
}

