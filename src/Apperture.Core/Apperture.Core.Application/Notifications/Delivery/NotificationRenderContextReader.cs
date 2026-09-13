using System.Text.Json;

namespace Apperture.Core.Application.Notifications.Delivery;

public static class NotificationRenderContextReader
{
    public static string? TryGetValue(string? renderContextJson, string key)
    {
        if (string.IsNullOrWhiteSpace(renderContextJson) || string.IsNullOrWhiteSpace(key))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(renderContextJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!string.Equals(prop.Name, key, StringComparison.Ordinal))
                    continue;
                return prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => prop.Value.ToString(),
                };
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
