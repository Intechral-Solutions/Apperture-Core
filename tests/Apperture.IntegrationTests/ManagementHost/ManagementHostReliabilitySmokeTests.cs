using System.Net;
using System.Net.Http.Json;
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
            $"These tests require a reachable test SQL Server. {SqlServerTestConfiguration.DescribeActiveConfiguration()} Connection attempted: {SqlServerTestConfiguration.DescribeConnectionSummary(_fixture.ConnectionString)}");
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
        "/api/dashboard/default",
        "/api/integrations/fleet",
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
        "/api/integrations/fleet",
        "/api/dashboard/default",
    };

    [Fact]
    public async Task Anonymous_bootstrap_status_returns_200_not_500()
    {
        AssertSqlAvailable();
        var client = _fixture.CreateClient();
        var response = await client.GetAsync("/api/bootstrap/status");
        AssertNoServerError(response, "bootstrap status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertCorrelationHeader(response);
    }

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
    public async Task Integration_reader_notification_diagnostics_by_integration_is_403_not_500()
    {
        AssertSqlAvailable();
        // The route needs notification-definition read on top of integration read
        // (NotificationDefinitionAuthorization.RequireIntegrationReadAsync). The integration reader role holds
        // integration.read only, so the refusal is the contract here, not one of two acceptable outcomes.
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync(
            $"/api/notification-diagnostics/by-integration/{ManagementGovernanceTestConstants.SeededIntegrationId}?take=10");
        AssertNoServerError(response, "notification diagnostics by integration");
        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "integration.read alone does not include notification-definition read");
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
    public async Task Auth_no_permissions_user_is_refused_on_permissioned_routes_and_gets_a_filtered_menu_not_500()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalAuthNoPerms);

        // /api/menus is [Authorize]-only by design: it answers every signed-in user with the menu that user's
        // capabilities allow, so a user without permissions gets a 200 with the ungated entries. The other two
        // routes check a permission in the service layer and must refuse.
        var expected = new (string Path, HttpStatusCode Status)[]
        {
            ("/api/control/center", HttpStatusCode.Forbidden),
            ("/api/menus", HttpStatusCode.OK),
            ($"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/detail", HttpStatusCode.Forbidden),
        };

        foreach (var (path, status) in expected)
        {
            var response = await client.GetAsync(path);
            AssertNoServerError(response, $"no-perms {path}");
            response.StatusCode.Should().Be(status, $"a user without permissions on {path}");
        }
    }
}
