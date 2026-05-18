using System.Net;
using System.Text.Json;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class ManagementClientNotificationTests
{
    [Fact]
    public async Task ListNotificationDefinitionsAsync_BuildsQueryString()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        _ = await client.ListNotificationDefinitionsAsync("int-1", enabled: true, origin: "builtIn", take: 50);

        handler.LastUri!.ToString().Should().Contain("notification-definitions");
        handler.LastUri.Query.Should().Contain("integrationId=int-1");
        handler.LastUri.Query.Should().Contain("enabled=true");
        handler.LastUri.Query.Should().Contain("origin=builtIn");
    }

    [Fact]
    public async Task SetNotificationDefinitionEnabledAsync_PatchesEnabled()
    {
        var payload = new { id = "def-1", displayName = "Test", isEnabled = false };
        var handler = new CaptureHandler(HttpStatusCode.OK, JsonSerializer.Serialize(payload));
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        _ = await client.SetNotificationDefinitionEnabledAsync("def-1", false);

        handler.LastMethod.Should().Be(HttpMethod.Patch);
        handler.LastUri!.AbsolutePath.Should().Contain("notification-definitions/def-1/enabled");
    }

    [Fact]
    public async Task ListNotificationSubscriptionsByIntegrationAsync_UsesIntegrationPath()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        _ = await client.ListNotificationSubscriptionsByIntegrationAsync("int-1", definitionId: "def-1");

        handler.LastUri!.AbsolutePath.Should().Contain("integrations/int-1/notification-subscriptions");
        handler.LastUri!.Query.Should().Contain("definitionId=def-1");
    }

    [Fact]
    public async Task ListNotificationSubscriptionsByDefinitionAsync_UsesDefinitionPath()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        _ = await client.ListNotificationSubscriptionsByDefinitionAsync("platform.schedule.expected_run_missing");

        handler.LastUri!.AbsolutePath.Should().Contain(
            "notification-definitions/platform.schedule.expected_run_missing/subscriptions");
    }

    [Fact]
    public async Task GetNotificationDeliveryHealthAsync_UsesDiagnosticsHealthPath()
    {
        var json = JsonSerializer.Serialize(new
        {
            instanceCountsByStatus = Array.Empty<object>(),
            outboxCountsByStatus = Array.Empty<object>(),
            recentFailedDeliveryCount = 0,
            recentSuppressedInstanceCount = 0,
            recentPendingOrDeliveringOutboxCount = 0,
            generatedAtUtc = DateTimeOffset.UtcNow
        });
        var handler = new CaptureHandler(HttpStatusCode.OK, json);
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        var result = await client.GetNotificationDeliveryHealthAsync();

        handler.LastUri!.AbsolutePath.Should().Contain("notification-diagnostics/health");
        result.RecentFailedDeliveryCount.Should().Be(0);
    }

    [Fact]
    public async Task GetNotificationConfigurationDiagnosticsAsync_UsesConfigurationPath()
    {
        var json = JsonSerializer.Serialize(new
        {
            platformEventsEnabled = true,
            evaluationEnabled = true,
            instancesEnabled = true,
            deliveryEnabled = false,
            usePlatformPipelineEnabled = false,
            warnings = Array.Empty<object>(),
            generatedAtUtc = DateTimeOffset.UtcNow
        });
        var handler = new CaptureHandler(HttpStatusCode.OK, json);
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        var result = await client.GetNotificationConfigurationDiagnosticsAsync();

        handler.LastUri!.AbsolutePath.Should().Contain("notification-diagnostics/configuration");
        result.PlatformEventsEnabled.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetExpectedRunMissingParityAcceptanceAsync_BuildsQuery()
    {
        var json = JsonSerializer.Serialize(new
        {
            integrationId = "int-1",
            incidentsEvaluated = 1,
            kindComparisonsTotal = 1,
            kindsAligned = 1,
            kindsMissingInPlatform = 0,
            kindsExtraInPlatform = 0,
            kindsStatusMismatch = 0,
            kindsDeliveryFailedInPlatform = 0,
            kindsDeliverySucceededInPlatform = 0,
            kindsSuppressedEpic69Only = 0,
            kindsSuppressedPlatformOnly = 0,
            recipientComparisonsTotal = 0,
            recipientsAligned = 0,
            recipientsMissingInPlatform = 0,
            recipientsExtraInPlatform = 0,
            recipientsDeliveryFailedInPlatform = 0,
            recipientsPendingInPlatform = 0,
            blockingFindings = Array.Empty<string>(),
            nonBlockingFindings = Array.Empty<string>(),
            looksAcceptableForCutover = true,
            generatedAtUtc = DateTimeOffset.UtcNow
        });
        var handler = new CaptureHandler(HttpStatusCode.OK, json);
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        var result = await client.GetExpectedRunMissingParityAcceptanceAsync("int-1", maxIncidents: 5);

        handler.LastUri!.ToString().Should().Contain("expected-run-missing/parity-acceptance");
        handler.LastUri.Query.Should().Contain("integrationId=int-1");
        handler.LastUri.Query.Should().Contain("maxIncidents=5");
        result!.LooksAcceptableForCutover.Should().BeTrue();
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;

        public Uri? LastUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }

        public CaptureHandler(HttpStatusCode status, string content)
        {
            _status = status;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastMethod = request.Method;
            var response = new HttpResponseMessage(_status) { Content = new StringContent(_content) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
}
