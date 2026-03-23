using System.Net.Http.Headers;
using System.Text;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apperture.Connectors.CampusNexus;

/// <summary>
/// Builds <see cref="ICampusNexusApiClient"/> from connection material.
/// Expected keys in <see cref="ResolvedConnectionMaterial.ConfigAndResolvedSecrets"/> (from Connection.Config and resolved SecretRefs):
/// <list type="bullet">
/// <item><description><c>BaseUrl</c> — required API root (e.g. https://tenant.example.edu/CampusNexus).</description></item>
/// <item><description><c>AuthMode</c> — <c>ApiKey</c> or <c>Basic</c> (case-insensitive).</description></item>
/// <item><description><c>ApiKey</c> — sent as <c>ApiKey</c> header when AuthMode is ApiKey.</description></item>
/// <item><description><c>Username</c> / <c>Password</c> — Basic auth when AuthMode is Basic.</description></item>
/// </list>
/// Caller must dispose the returned client.
/// </summary>
public sealed class CampusNexusApiConnectorClientFactory : IConnectorClientFactory<ICampusNexusApiClient>
{
    private const string BaseUrlKey = "BaseUrl";
    private const string AuthModeKey = "AuthMode";
    private const string ApiKeyKey = "ApiKey";
    private const string UsernameKey = "Username";
    private const string PasswordKey = "Password";

    public ICampusNexusApiClient Create(Connection connection, ResolvedConnectionMaterial material)
    {
        var dict = material.ConfigAndResolvedSecrets;
        string baseUrl = GetRequired(dict, BaseUrlKey);
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
            baseUrl += "/";

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        var http = new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri(baseUrl, UriKind.Absolute) };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        string? authMode = dict.TryGetValue(AuthModeKey, out var am) ? am : null;
        if (string.Equals(authMode, "ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            string key = GetRequired(dict, ApiKeyKey);
            http.DefaultRequestHeaders.Add("ApiKey", key);
        }
        else if (string.Equals(authMode, "Basic", StringComparison.OrdinalIgnoreCase))
        {
            string user = GetRequired(dict, UsernameKey);
            string pass = dict.TryGetValue(PasswordKey, out var p) ? p : "";
            string token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{pass}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
        else
            throw new InvalidOperationException($"Connection material must specify {AuthModeKey} as ApiKey or Basic.");

        return new CampusNexusApiClient(http, NullLogger<CampusNexusApiClient>.Instance);
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
            throw new InvalidOperationException($"Connection material missing required key '{key}'.");
        return v.Trim();
    }
}
