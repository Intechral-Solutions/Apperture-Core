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

            if (field.AllowedValues is { Count: > 0 })
            {
                var valueText = value.ToString();
                if (!field.AllowedValues.Contains(valueText, StringComparer.Ordinal))
                    throw new ArgumentException($"Field '{field.Key}' must be one of: {string.Join(", ", field.AllowedValues)}.");
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

                list.Add(new ParsedField(key, type, required, values));
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

    private sealed record ParsedField(string Key, string Type, bool Required, IReadOnlyList<string>? AllowedValues);
}

