namespace Apperture.Management.Ui.Shared.Options;

/// <summary>
/// Resolves management API and host URLs when the Blazor UI runs on loopback :9001 and the API on :9000.
/// Keeps <c>wwwroot/appsettings.json</c> usable with relative <c>/api</c> for same-origin deployments.
/// </summary>
public static class ManagementUiDevApiRouting
{
    private const int DevManagementHostPort = 9000;

    /// <summary>
    /// Returns an absolute API base (with or without trailing slash — caller normalizes) when dev split-host applies.
    /// </summary>
    public static string ResolveApiBaseUrl(string? apiBaseUrlFromConfig, string wasmBaseAddress)
    {
        var apiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrlFromConfig) ? "/api" : apiBaseUrlFromConfig.Trim();
        // Dev split-host: UI on loopback (script path :9001 or Aspire dynamic port behind :9001 proxy).
        if (!apiBaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(wasmBaseAddress, UriKind.Absolute, out var wasmBase)
            && wasmBase.IsLoopback)
        {
            return $"http://localhost:{DevManagementHostPort}/api";
        }

        return apiBaseUrl;
    }
}
