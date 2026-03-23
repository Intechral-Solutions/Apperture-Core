using Apperture.Management.Ui.Shared.Options;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>Resolves management host base URL for OIDC /signin and /signout (cross-origin dev).</summary>
public sealed class ShellHostNavigation
{
    private readonly IOptions<ShellUiOptions> _options;
    private readonly IConfiguration _configuration;

    public ShellHostNavigation(IOptions<ShellUiOptions> options, IConfiguration configuration)
    {
        _options = options;
        _configuration = configuration;
    }

    /// <summary>Absolute base URL (normalized with trailing slash).</summary>
    public Uri GetManagementHostBase(NavigationManager navigationManager)
    {
        var configured = _options.Value.ManagementHostUrl;
        if (!string.IsNullOrWhiteSpace(configured))
            return Normalize(new Uri(configured, UriKind.Absolute));

        var apiBase = _configuration["ApiBaseUrl"] ?? "/api";
        if (apiBase.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var apiUri = new Uri(apiBase, UriKind.Absolute);
            return new Uri($"{apiUri.Scheme}://{apiUri.Authority}/", UriKind.Absolute);
        }

        return new Uri(navigationManager.BaseUri);
    }

    private static Uri Normalize(Uri u)
    {
        var s = u.ToString().TrimEnd('/') + "/";
        return new Uri(s, UriKind.Absolute);
    }
}
