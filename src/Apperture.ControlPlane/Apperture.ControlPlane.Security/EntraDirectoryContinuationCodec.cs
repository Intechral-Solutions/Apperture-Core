using System.Text;
using System.Text.Json;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Opaque paging state for Entra directory search. Never a Graph URL: the skip token is only
/// applied as Graph <c>$skiptoken</c> for the same registration and search.
/// </summary>
internal static class EntraDirectoryContinuationCodec
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Encode(string registrationId, string? search, string skipToken, int top)
    {
        var json = JsonSerializer.Serialize(new Payload(registrationId, search, skipToken, top), Json);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static bool TryDecode(
        string? token,
        string expectedRegistrationId,
        out string? search,
        out string skipToken,
        out int top)
    {
        search = null;
        skipToken = "";
        top = 25;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(token.Trim()));
            var payload = JsonSerializer.Deserialize<Payload>(json, Json);
            if (payload is null
                || string.IsNullOrWhiteSpace(payload.SkipToken)
                || !string.Equals(payload.RegistrationId, expectedRegistrationId, StringComparison.Ordinal))
            {
                return false;
            }

            search = payload.Search;
            skipToken = payload.SkipToken;
            top = payload.Top is > 0 and <= 100 ? payload.Top : 25;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record Payload(string RegistrationId, string? Search, string SkipToken, int Top);
}
