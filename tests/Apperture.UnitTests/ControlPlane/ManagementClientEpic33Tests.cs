using System.Net;
using System.Text.Json;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Epic 33: ManagementClient tests for runtime health, schedules, and recovery.
/// </summary>
public sealed class ManagementClientEpic33Tests
{
    [Fact]
    public async Task GetRuntimeHealthAsync_DeserializesSnapshot()
    {
        var snapshot = new
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            OverallStatus = "Healthy",
            DegradationReasons = Array.Empty<string>(),
            ModuleDiagnostics = Array.Empty<object>(),
            ScheduleDiagnostics = Array.Empty<object>(),
            BlockedDeferredWork = Array.Empty<object>(),
            Anomalies = Array.Empty<object>(),
            RestartBoundSurfaces = Array.Empty<string>(),
            NotYetImplementedSurfaces = Array.Empty<string>()
        };
        var client = CreateClientWithJsonResponse(snapshot, "control/runtime-health");

        var result = await client.GetRuntimeHealthAsync();

        result.Should().NotBeNull();
        result!.OverallStatus.Should().Be("Healthy");
    }

    [Fact]
    public async Task GetSchedulesAsync_DeserializesList()
    {
        var schedules = new[] { new { Id = "s1", Name = "Test", RecurrenceExpression = "0 * * * *", TimeZone = "UTC", Enabled = true, TargetJobType = 0, TargetJobConfiguration = (string?)null, Version = 1 } };
        var client = CreateClientWithJsonResponse(schedules, "schedules");

        var result = await client.GetSchedulesAsync();

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("s1");
        result[0].Name.Should().Be("Test");
    }

    [Fact]
    public async Task GetBlockedWorkAsync_DeserializesList()
    {
        var items = new[] { new { RunId = "r1", DefinitionId = "wf-1", DefinitionKind = "workflow", ModuleId = (string?)"m1", FailedAtUtc = DateTimeOffset.UtcNow, BlockedReason = "blocked", RetryEligibleNow = true, RetryEligibleReason = (string?)null } };
        var client = CreateClientWithJsonResponse(items, "control/recovery/blocked-work");

        var result = await client.GetBlockedWorkAsync();

        result.Should().HaveCount(1);
        result[0].RunId.Should().Be("r1");
        result[0].DefinitionKind.Should().Be("workflow");
        result[0].RetryEligibleNow.Should().BeTrue();
    }

    [Fact]
    public async Task RetryBlockedWorkAsync_On202_ReturnsSuccessWithNewRunId()
    {
        var response = new { originalRunId = "r1", runId = "r2", workflowInstanceId = "inst-2", workflowDefinitionId = "wf-1", status = "requested" };
        var client = CreateClientWithResponse(HttpStatusCode.Accepted, response, "control/recovery/blocked-work/r1/retry");

        var result = await client.RetryBlockedWorkAsync("r1");

        result.Success.Should().BeTrue();
        result.NewRunId.Should().Be("r2");
        result.InspectionOnly.Should().BeFalse();
    }

    [Fact]
    public async Task RetryBlockedWorkAsync_On400Operation_ReturnsInspectionOnly()
    {
        var response = new { error = "Operation retry not yet supported.", runId = "r1", definitionKind = "operation", inspectionOnly = true, message = "Inspection only." };
        var client = CreateClientWithResponse(HttpStatusCode.BadRequest, response, "control/recovery/blocked-work/r1/retry");

        var result = await client.RetryBlockedWorkAsync("r1");

        result.Success.Should().BeFalse();
        result.InspectionOnly.Should().BeTrue();
    }

    [Fact]
    public async Task GetRunsAsync_IncludesCorrelationKeyQuery_Epic35()
    {
        var handler = new RunsQueryCaptureHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var client = new ManagementClient(http);

        _ = await client.GetRunsAsync(new RunQueryFilter(CorrelationKey: "schedule:s1"));

        handler.LastRequestUri.Should().NotBeNull();
        var q = handler.LastRequestUri!.Query;
        q.Should().Contain("correlationKey=");
        q.Should().Contain(Uri.EscapeDataString("schedule:s1"));
    }

    private static ManagementClient CreateClientWithJsonResponse(object payload, string expectedPathContains)
    {
        var json = JsonSerializer.Serialize(payload);
        return CreateClientWithResponse(HttpStatusCode.OK, json, expectedPathContains, isJsonString: true);
    }

    private static ManagementClient CreateClientWithResponse<T>(HttpStatusCode status, T payload, string expectedPathContains, bool isJsonString = false)
    {
        var json = isJsonString ? (payload as string) ?? "{}" : JsonSerializer.Serialize(payload);
        var handler = new MockHttpHandler(status, json, expectedPathContains);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        return new ManagementClient(http);
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;
        private readonly string _pathContains;

        public MockHttpHandler(HttpStatusCode status, string content, string pathContains)
        {
            _status = status;
            _content = content;
            _pathContains = pathContains;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri?.ToString().Should().Contain(_pathContains);
            var response = new HttpResponseMessage(_status) { Content = new StringContent(_content) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }

    private sealed class RunsQueryCaptureHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
}
