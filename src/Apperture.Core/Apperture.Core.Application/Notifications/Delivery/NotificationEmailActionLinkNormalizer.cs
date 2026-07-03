namespace Apperture.Core.Application.Notifications.Delivery;

/// <summary>
/// Normalizes notification action URLs for email clients (absolute when base URL is configured).
/// </summary>
public static class NotificationEmailActionLinkNormalizer
{
    public static string? Normalize(string? instanceBaseUrl, string? actionUrl)
    {
        if (string.IsNullOrWhiteSpace(actionUrl))
            return null;

        var trimmed = actionUrl.Trim();
        if (IsAbsoluteHttpUrl(trimmed))
            return trimmed;

        if (string.IsNullOrWhiteSpace(instanceBaseUrl))
            return trimmed;

        if (!TryNormalizeBaseUrl(instanceBaseUrl, out var baseUrl))
            return trimmed;

        var path = trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed;
        return baseUrl + path;
    }

    public static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static bool TryNormalizeBaseUrl(string? instanceBaseUrl, out string baseUrl)
    {
        baseUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(instanceBaseUrl))
            return false;

        var trimmed = instanceBaseUrl.Trim();
        if (!IsAbsoluteHttpUrl(trimmed))
            return false;

        baseUrl = trimmed.TrimEnd('/');
        return true;
    }
}
