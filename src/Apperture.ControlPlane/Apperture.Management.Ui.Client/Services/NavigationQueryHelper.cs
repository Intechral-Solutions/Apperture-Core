namespace Apperture.Management.Ui.Client.Services;

/// <summary>Lightweight query string parsing for Blazor WASM (no Microsoft.AspNetCore.WebUtilities).</summary>
internal static class NavigationQueryHelper
{
    public static Dictionary<string, string> ParseQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        foreach (var segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = segment.IndexOf('=');
            if (equals < 0)
            {
                result[Uri.UnescapeDataString(segment)] = "";
                continue;
            }

            var key = Uri.UnescapeDataString(segment[..equals]);
            var value = Uri.UnescapeDataString(segment[(equals + 1)..]);
            result[key] = value;
        }

        return result;
    }
}
