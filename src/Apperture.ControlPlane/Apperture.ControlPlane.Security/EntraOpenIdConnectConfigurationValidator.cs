using System.Net;
using System.Text.Json;
using Apperture.Core.Application.Services;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

public sealed class EntraOpenIdConnectConfigurationValidator : IEntraOpenIdConnectConfigurationValidator
{
    public const string HttpClientName = "OidcDiscovery";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EntraOpenIdConnectConfigurationValidator> _logger;

    public EntraOpenIdConnectConfigurationValidator(
        IHttpClientFactory httpClientFactory,
        ILogger<EntraOpenIdConnectConfigurationValidator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<EntraOpenIdConnectValidationResult> ValidateAsync(
        string authority,
        string clientId,
        bool usesDeploymentSecret,
        bool clientSecretConfigured,
        string? azureTenantId,
        CancellationToken cancellationToken = default)
    {
        _ = usesDeploymentSecret;
        if (string.IsNullOrWhiteSpace(authority))
            return Fail(OpenIdConfigurationStatus.InvalidConfiguration, "missing_authority", "Authority is required.", null);

        if (!Uri.TryCreate(authority.Trim(), UriKind.Absolute, out var authorityUri) || authorityUri.Scheme != Uri.UriSchemeHttps)
            return Fail(OpenIdConfigurationStatus.InvalidConfiguration, "invalid_authority", "Authority must be an absolute https URL.", null);

        if (string.IsNullOrWhiteSpace(clientId))
            return Fail(OpenIdConfigurationStatus.InvalidConfiguration, "missing_client_id", "Client ID is required.", null);

        if (!clientSecretConfigured)
            return Fail(
                OpenIdConfigurationStatus.InvalidConfiguration,
                "missing_client_secret",
                "A client secret is not configured. Record one on this Entra registration or set Entra:ClientSecret before sign-in can work.",
                null);

        if (EntraAuthorityHelper.TenantSegmentLooksLikeMalformedGuid(authority))
        {
            var segment = EntraAuthorityHelper.TryGetTenantSegmentFromAuthority(authority);
            return Fail(
                OpenIdConfigurationStatus.InvalidConfiguration,
                "malformed_tenant_in_authority",
                "The tenant id in the authority URL is not a valid GUID. Compare it to Azure tenant id and check for an extra or missing character.",
                segment is null ? null : $"authorityTenantSegment={segment}");
        }

        if (!EntraAuthorityHelper.AuthorityTenantMatchesAzureTenantId(authority, azureTenantId))
        {
            var segment = EntraAuthorityHelper.TryGetTenantSegmentFromAuthority(authority);
            return Fail(
                OpenIdConfigurationStatus.InvalidConfiguration,
                "authority_tenant_mismatch",
                "The authority URL tenant segment does not match the Azure tenant id. They must refer to the same directory.",
                $"authorityTenantSegment={segment}; azureTenantId={azureTenantId}");
        }

        var discoveryUri = BuildDiscoveryDocumentUri(authorityUri);
        using var client = _httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(discoveryUri, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "OIDC discovery request failed for {DiscoveryUri}", discoveryUri);
            return Fail(
                OpenIdConfigurationStatus.DiscoveryUnreachable,
                "discovery_http_error",
                "Could not reach the OpenID discovery document. Check the authority URL and network connectivity.",
                ex.Message);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !cancellationToken.IsCancellationRequested)
        {
            return Fail(
                OpenIdConfigurationStatus.DiscoveryUnreachable,
                "discovery_timeout",
                "The OpenID discovery request timed out. Verify the authority URL and try again.",
                ex.Message);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
            return Fail(
                OpenIdConfigurationStatus.DiscoveryUnreachable,
                "discovery_not_found",
                "No OpenID metadata was found at the expected discovery URL. Confirm the authority matches your Entra tenant (e.g. https://login.microsoftonline.com/{tenant-id}).",
                $"HTTP {(int)response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeReadContentAsync(response, cancellationToken);
            return Fail(
                OpenIdConfigurationStatus.DiscoveryUnreachable,
                "discovery_http_status",
                $"Discovery endpoint returned HTTP {(int)response.StatusCode}. The authority may be wrong or the tenant metadata unavailable.",
                body);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        JsonDocument doc;
        try
        {
            doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            return Fail(
                OpenIdConfigurationStatus.DiscoveryUnreachable,
                "discovery_invalid_json",
                "The discovery endpoint returned data that is not valid JSON.",
                ex.Message);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!TryGetString(root, "issuer", out var issuer) ||
                !TryGetString(root, "authorization_endpoint", out _) ||
                !TryGetString(root, "token_endpoint", out _) ||
                !TryGetString(root, "jwks_uri", out _))
            {
                return Fail(
                    OpenIdConfigurationStatus.InvalidConfiguration,
                    "discovery_incomplete",
                    "The discovery document is missing required fields (issuer, authorization_endpoint, token_endpoint, jwks_uri).",
                    null);
            }

            if (!EntraAuthorityHelper.IssuerMatchesAzureTenantId(issuer, azureTenantId))
            {
                return Fail(
                    OpenIdConfigurationStatus.IssuerMismatch,
                    "issuer_mismatch",
                    "The issuer in the discovery document does not match the configured Azure tenant id. Update Azure tenant id or authority so they refer to the same directory.",
                    $"issuer={issuer}");
            }

            return new EntraOpenIdConnectValidationResult(
                true,
                OpenIdConfigurationStatus.Valid,
                "valid",
                "OpenID configuration looks usable: discovery document is reachable and required endpoints are present.",
                null);
        }
    }

    private static EntraOpenIdConnectValidationResult Fail(
        OpenIdConfigurationStatus status,
        string? code,
        string summary,
        string? technicalDetail) =>
        new(false, status, code, summary, technicalDetail);

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = "";
        if (!root.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
            return false;
        value = p.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    private static Uri BuildDiscoveryDocumentUri(Uri authority)
    {
        var baseUri = authority.ToString().TrimEnd('/');
        return new Uri($"{baseUri}/.well-known/openid-configuration", UriKind.Absolute);
    }

    private static async Task<string?> SafeReadContentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var s = await response.Content.ReadAsStringAsync(cancellationToken);
            return s.Length > 2000 ? s[..2000] + "…" : s;
        }
        catch
        {
            return null;
        }
    }
}
