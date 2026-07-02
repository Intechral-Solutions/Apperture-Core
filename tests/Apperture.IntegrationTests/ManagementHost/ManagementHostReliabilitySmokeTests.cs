using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// S1 reliability smoke: linked Management API routes must not return HTTP 500 during normal navigation.
/// Blazor page rendering is not covered here; add Playwright or bUnit route smoke in a follow-up pass.
/// </summary>
[Collection(ManagementHostGovernanceCollection.Name)]
public sealed class ManagementHostReliabilitySmokeTests
{
    private readonly ManagementHostGovernanceFixture _fixture;

    public ManagementHostReliabilitySmokeTests(ManagementHostGovernanceFixture fixture) => _fixture = fixture;

    private void AssertSqlAvailable()
    {
        Assert.True(
            _fixture.SqlAvailable,
            $"These tests require SQL Server or LocalDB. Set {ManagementGovernanceTestConstants.ConnectionStringEnvironmentVariable} or install LocalDB. Connection attempted: {_fixture.ConnectionString}");
    }

    private static void AssertNoServerError(HttpResponseMessage response, string label)
    {
        var status = (int)response.StatusCode;
        status.Should().BeLessThan(
            500,
            $"{label}: {status} from {response.RequestMessage?.RequestUri}");
    }

    private static void AssertCorrelationHeader(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue(
            "responses should include X-Correlation-ID from CorrelationIdMiddleware");
        values!.First().Should().NotBeNullOrWhiteSpace();
    }

    public static TheoryData<string> AnonymousApiRoutes { get; } = new()
    {
        "/api/control/center",
        "/api/control/capabilities",
        "/api/control/runtime-health",
        "/api/control/activity",
        "/api/menus",
        "/api/managed-contacts",
        "/api/me/notifications",
        "/api/me/notifications/unread-count",
        "/api/notification-diagnostics/health",
        $"/api/notification-diagnostics/by-integration/{ManagementGovernanceTestConstants.SeededIntegrationId}",
        $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/detail",
        $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures",
        "/api/notification-definitions",
        "/api/modules",
    };

    public static TheoryData<string> IntegrationReaderApiRoutes { get; } = new()
    {
        "/api/control/center",
        "/api/control/capabilities",
        "/api/control/runtime-health",
        "/api/control/activity",
        "/api/menus",
        "/api/managed-contacts",
        "/api/me/notifications",
        "/api/me/notifications/unread-count",
        "/api/notification-diagnostics/health",
        $"/api/notification-diagnostics/by-integration/{ManagementGovernanceTestConstants.SeededIntegrationId}",
        $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/detail",
        $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures",
        "/api/notification-definitions",
        "/api/modules",
        "/api/integrations",
    };

    [Theory]
    [MemberData(nameof(AnonymousApiRoutes))]
    public async Task Anonymous_linked_api_routes_return_401_not_500(string path)
    {
        AssertSqlAvailable();
        var client = _fixture.CreateClient();
        var response = await client.GetAsync(path);
        AssertNoServerError(response, "anonymous");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AssertCorrelationHeader(response);
    }

    [Theory]
    [MemberData(nameof(IntegrationReaderApiRoutes))]
    public async Task Integration_reader_linked_api_routes_do_not_return_500(string path)
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync(path);
        AssertNoServerError(response, "integration reader");
        AssertCorrelationHeader(response);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Integration_reader_seeded_integration_detail_returns_200()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/detail");
        AssertNoServerError(response, "integration detail");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Integration_reader_missing_integration_detail_returns_404_not_500()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync("/api/integrations/intgnotfound999999999/detail");
        AssertNoServerError(response, "missing integration detail");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Integration_reader_notification_diagnostics_by_integration_returns_200_or_403_not_500()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync(
            $"/api/notification-diagnostics/by-integration/{ManagementGovernanceTestConstants.SeededIntegrationId}?take=10");
        AssertNoServerError(response, "notification diagnostics by integration");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Forbidden);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.TryGetProperty("recentOutboxRows", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Missing_notification_definition_returns_404_not_500()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "smoke-corr-404-001");

        var response = await client.GetAsync("/api/notification-definitions/ndefnotfound99999999999");
        AssertNoServerError(response, "missing notification definition");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        AssertCorrelationHeader(response);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("at Apperture.");
    }

    [Fact]
    public async Task Auth_no_permissions_linked_routes_return_403_not_500()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalAuthNoPerms);
        var routes = new[]
        {
            "/api/control/center",
            "/api/menus",
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/detail",
        };

        foreach (var path in routes)
        {
            var response = await client.GetAsync(path);
            AssertNoServerError(response, $"no-perms {path}");
            response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.OK);
        }
    }
}
