using System.Text.Json;

namespace Apperture.Management.Api.Models;

/// <summary>
/// JSON body for procedure launch. Uses <see cref="JsonElement"/> for parameters so primitive values deserialize reliably (Epic 56A).
/// </summary>
public sealed class IntegrationProcedureLaunchBody
{
    public JsonElement? Parameters { get; init; }

    public string? IdempotencyKey { get; init; }

    public string? CorrelationId { get; init; }

    public static IReadOnlyDictionary<string, object?>? CoerceParameters(JsonElement? parameters)
    {
        if (parameters is null)
            return null;

        var el = parameters.Value;
        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (el.ValueKind != JsonValueKind.Object)
            return null;

        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in el.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Number => prop.Value.TryGetInt64(out var l)
                    ? l
                    : prop.Value.TryGetDouble(out var d)
                        ? d
                        : prop.Value.GetRawText(),
                _ => prop.Value.GetRawText()
            };
        }

        return dict;
    }
}
