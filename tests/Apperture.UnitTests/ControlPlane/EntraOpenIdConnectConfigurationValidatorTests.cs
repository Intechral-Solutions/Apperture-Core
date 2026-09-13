using System.Net;
using System.Net.Http;
using System.Text;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class EntraOpenIdConnectConfigurationValidatorTests
{
    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public TestHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> impl) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(impl(request));
    }

    [Fact]
    public async Task ValidateAsync_WhenDiscoveryDocumentComplete_ReturnsValid()
    {
        var handler = new RecordingHandler(req =>
        {
            req.RequestUri!.AbsoluteUri.Should().Contain(".well-known/openid-configuration");
            const string json = """
            {
              "issuer":"https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0",
              "authorization_endpoint":"https://login.microsoftonline.com/authorize",
              "token_endpoint":"https://login.microsoftonline.com/token",
              "jwks_uri":"https://login.microsoftonline.com/common/discovery/keys"
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        var validator = new EntraOpenIdConnectConfigurationValidator(
            new TestHttpClientFactory(handler),
            NullLogger<EntraOpenIdConnectConfigurationValidator>.Instance);

        var result = await validator.ValidateAsync(
            "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111",
            "client-id",
            usesDeploymentSecret: true,
            clientSecretConfigured: true,
            azureTenantId: "11111111-1111-1111-1111-111111111111");

        result.Success.Should().BeTrue();
        result.Status.Should().Be(OpenIdConfigurationStatus.Valid);
    }

    [Fact]
    public async Task ValidateAsync_WhenDeploymentSecretMissing_ReturnsInvalidConfiguration()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var validator = new EntraOpenIdConnectConfigurationValidator(
            new TestHttpClientFactory(handler),
            NullLogger<EntraOpenIdConnectConfigurationValidator>.Instance);

        var result = await validator.ValidateAsync(
            "https://login.microsoftonline.com/tenant",
            "client",
            usesDeploymentSecret: true,
            clientSecretConfigured: false,
            azureTenantId: null);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(OpenIdConfigurationStatus.InvalidConfiguration);
        result.Code.Should().Be("missing_client_secret");
    }

    [Fact]
    public async Task ValidateAsync_WhenAuthorityTenantHasTypo_ReturnsInvalidConfigurationWithoutHttp()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP should not run"));
        var validator = new EntraOpenIdConnectConfigurationValidator(
            new TestHttpClientFactory(handler),
            NullLogger<EntraOpenIdConnectConfigurationValidator>.Instance);

        var result = await validator.ValidateAsync(
            "https://login.microsoftonline.com/f47559c4-15e1-4df2-8bf2-a31cd60330c6a",
            "client",
            usesDeploymentSecret: true,
            clientSecretConfigured: true,
            azureTenantId: "f47559c4-15e1-4df2-8bf2-a31cd60330c6");

        result.Success.Should().BeFalse();
        result.Status.Should().Be(OpenIdConfigurationStatus.InvalidConfiguration);
        result.Code.Should().Be("malformed_tenant_in_authority");
    }

    [Fact]
    public async Task ValidateAsync_WhenDiscoveryNotFound_ReturnsDiscoveryUnreachable()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var validator = new EntraOpenIdConnectConfigurationValidator(
            new TestHttpClientFactory(handler),
            NullLogger<EntraOpenIdConnectConfigurationValidator>.Instance);

        var result = await validator.ValidateAsync(
            "https://login.microsoftonline.com/tenant",
            "client",
            usesDeploymentSecret: true,
            clientSecretConfigured: true,
            azureTenantId: null);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(OpenIdConfigurationStatus.DiscoveryUnreachable);
    }
}
