using System.Text.Json;

namespace Apperture.Connectors.CampusNexus.Models;

public sealed record ApiCommandResponse
{
    public JsonElement Payload { get; init; }
    public ApiResponseNotification[]? Notifications { get; init; }
    public bool? HasError { get; init; }
    public bool? HasFault { get; init; }
    public bool? HasValidationError { get; init; }
    public bool? HasSecurityError { get; init; }
    public string? Message { get; init; }

    public bool IsSuccess =>
        (HasError is false || HasError is null)
        && (HasFault is false || HasFault is null)
        && (HasValidationError is false || HasValidationError is null)
        && (HasSecurityError is false || HasSecurityError is null);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public T Deserialize<T>()
    {
        JsonElement target = Payload.ValueKind != JsonValueKind.Undefined && Payload.TryGetProperty("data", out JsonElement dataTarget)
            ? dataTarget
            : Payload;
        return target.Deserialize<T>(JsonOptions)!;
    }

    public T? GetFieldByName<T>(string propertyName)
    {
        if (Payload.ValueKind == JsonValueKind.Undefined)
            return default;
        if (Payload.TryGetProperty("data", out JsonElement dataElement) && dataElement.TryGetProperty(propertyName, out JsonElement propertyValue))
            return propertyValue.Deserialize<T>(JsonOptions);
        return default;
    }

    public bool TryGetFieldByName<T>(string propertyName, out T? value)
    {
        value = GetFieldByName<T>(propertyName);
        return value is not null;
    }
}

public sealed record ApiResponseNotification
{
    public string? Message { get; init; }
}

public readonly record struct CnsEntityWithId
{
    public int Id { get; init; }
}
