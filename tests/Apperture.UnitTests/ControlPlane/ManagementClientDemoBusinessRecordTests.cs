using System.Net;
using System.Text.Json;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>BDS-4: Management Client contract for demo customer business records.</summary>
public sealed class ManagementClientDemoBusinessRecordTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task GetDemoCustomerRecordsAsync_DeserializesPage_WithSourcesAndConflicts()
    {
        var payload = new
        {
            items = new[]
            {
                new
                {
                    recordType = "customer",
                    businessKey = "C-100",
                    displayName = "Valid One",
                    primaryEmail = "conflict.c100@apipolling.example",
                    primaryStatus = "Inactive",
                    sourceCount = 2,
                    hasConflict = true,
                    conflictCount = 2,
                    lastObservedAtUtc = "2026-07-21T11:00:00Z",
                    primaryConflictSummary = "email differs across sources. (+1 more)",
                    sources = new[]
                    {
                        new
                        {
                            sourceKind = "api_polling",
                            sourceKindLabel = "API polling",
                            moduleId = "Sample.ApiPolling",
                            integrationId = "int-ap",
                            operationKind = "api_poll",
                            operationItemId = "oi-ap-100",
                            operationGroupId = "og-ap",
                            runId = "run-ap",
                            businessKey = "C-100",
                            displayName = "Valid One",
                            email = "conflict.c100@apipolling.example",
                            status = "Inactive",
                            changeKind = "updated",
                            observedAtUtc = "2026-07-21T11:00:00Z",
                            provenanceLabel = "API polling · item oi-ap-100 · run run-ap",
                        },
                        new
                        {
                            sourceKind = "file_import",
                            sourceKindLabel = "File import",
                            moduleId = "Sample.FileImport",
                            integrationId = "int-fi",
                            operationKind = "file_import",
                            operationItemId = "oi-fi-100",
                            operationGroupId = "og-fi",
                            runId = "run-fi",
                            businessKey = "C-100",
                            displayName = "Valid One",
                            email = "valid.one@example.com",
                            status = "Active",
                            changeKind = "imported",
                            observedAtUtc = "2026-07-21T10:00:00Z",
                            provenanceLabel = "File import · item oi-fi-100 · run run-fi",
                        },
                    },
                    conflicts = new[]
                    {
                        new
                        {
                            field = "email",
                            valuesBySource = new Dictionary<string, string>
                            {
                                ["api_polling:Sample.ApiPolling"] = "conflict.c100@apipolling.example",
                                ["file_import:Sample.FileImport"] = "valid.one@example.com",
                            },
                            message = "email differs across sources.",
                        },
                        new
                        {
                            field = "status",
                            valuesBySource = new Dictionary<string, string>
                            {
                                ["api_polling:Sample.ApiPolling"] = "Inactive",
                                ["file_import:Sample.FileImport"] = "Active",
                            },
                            message = "status differs across sources.",
                        },
                    },
                },
            },
            totalCount = 1,
            skip = 0,
            take = 50,
        };

        var handler = new CaptureHandler(HttpStatusCode.OK, payload);
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        var page = await client.GetDemoCustomerRecordsAsync();

        page.Should().NotBeNull();
        page!.TotalCount.Should().Be(1);
        page.Skip.Should().Be(0);
        page.Take.Should().Be(50);
        page.Items.Should().ContainSingle();

        var row = page.Items[0];
        row.BusinessKey.Should().Be("C-100");
        row.HasConflict.Should().BeTrue();
        row.ConflictCount.Should().Be(2);
        row.PrimaryConflictSummary.Should().Contain("email differs");
        row.Sources.Should().HaveCount(2);
        row.Sources.Should().Contain(s =>
            s.SourceKind == "file_import"
            && s.SourceKindLabel == "File import"
            && s.OperationItemId == "oi-fi-100"
            && s.RunId == "run-fi");
        row.Conflicts.Should().Contain(c => c.Field == "email" && c.ValuesBySource.Count == 2);
        row.Conflicts.Should().Contain(c => c.Field == "status");

        handler.LastRequestUri!.AbsolutePath.Should().Contain("/demo/business-records/customers");
    }

    [Fact]
    public async Task GetDemoCustomerRecordsAsync_SerializesFiltersAndPagination()
    {
        var handler = new CaptureHandler(
            HttpStatusCode.OK,
            new { items = Array.Empty<object>(), totalCount = 0, skip = 10, take = 25 });
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        var page = await client.GetDemoCustomerRecordsAsync(new DemoCustomerRecordListFilter(
            Q: "Ada",
            SourceKind: "file_import",
            HasConflict: true,
            Skip: 10,
            Take: 25));

        page.Should().NotBeNull();
        page!.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
        page.Skip.Should().Be(10);
        page.Take.Should().Be(25);

        var q = handler.LastRequestUri!.Query;
        q.Should().Contain("q=Ada");
        q.Should().Contain("sourceKind=file_import");
        q.Should().Contain("hasConflict=true");
        q.Should().Contain("skip=10");
        q.Should().Contain("take=25");
    }

    [Fact]
    public async Task GetDemoCustomerRecordAsync_DeserializesDetail()
    {
        var payload = new
        {
            recordType = "customer",
            businessKey = "C-001",
            displayName = "Ada Lovelace",
            primaryEmail = "ada@example.com",
            primaryStatus = "Active",
            sourceCount = 1,
            hasConflict = false,
            conflictCount = 0,
            lastObservedAtUtc = "2026-07-21T11:00:00Z",
            primaryConflictSummary = (string?)null,
            sources = new[]
            {
                new
                {
                    sourceKind = "file_import",
                    sourceKindLabel = "File import",
                    moduleId = "Sample.FileImport",
                    integrationId = "int-fi",
                    operationKind = "file_import",
                    operationItemId = "oi-1",
                    operationGroupId = "og-1",
                    runId = "run-1",
                    businessKey = "C-001",
                    displayName = "Ada Lovelace",
                    email = "ada@example.com",
                    status = "Active",
                    changeKind = "imported",
                    observedAtUtc = "2026-07-21T11:00:00Z",
                    provenanceLabel = "File import · item oi-1 · run run-1",
                },
            },
            conflicts = Array.Empty<object>(),
        };

        var handler = new CaptureHandler(HttpStatusCode.OK, payload);
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        var dto = await client.GetDemoCustomerRecordAsync("C-001");

        dto.Should().NotBeNull();
        dto!.BusinessKey.Should().Be("C-001");
        dto.DisplayName.Should().Be("Ada Lovelace");
        dto.PrimaryEmail.Should().Be("ada@example.com");
        dto.Sources.Should().ContainSingle(s => s.ProvenanceLabel.Contains("oi-1"));
        dto.Conflicts.Should().BeEmpty();
        handler.LastRequestUri!.AbsolutePath.Should().EndWith("/demo/business-records/customers/C-001");
    }

    [Fact]
    public async Task GetDemoCustomerRecordAsync_EscapesBusinessKey()
    {
        var handler = new CaptureHandler(HttpStatusCode.NotFound, new { });
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        var dto = await client.GetDemoCustomerRecordAsync("C 100/odd");

        dto.Should().BeNull();
        handler.LastRequestUri!.AbsolutePath.Should().Contain(Uri.EscapeDataString("C 100/odd"));
    }

    [Fact]
    public async Task GetDemoCustomerRecordAsync_ReturnsNull_On404()
    {
        var handler = new CaptureHandler(HttpStatusCode.NotFound, new { });
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        (await client.GetDemoCustomerRecordAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task GetDemoCustomerRecordsAsync_ReturnsNull_On403()
    {
        var handler = new CaptureHandler(HttpStatusCode.Forbidden, new { });
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        (await client.GetDemoCustomerRecordsAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetDemoCustomerRecordAsync_ReturnsNull_On403()
    {
        var handler = new CaptureHandler(HttpStatusCode.Forbidden, new { });
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") });

        (await client.GetDemoCustomerRecordAsync("C-001")).Should().BeNull();
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;

        public Uri? LastRequestUri { get; private set; }

        public CaptureHandler(HttpStatusCode status, object payload)
        {
            _status = status;
            _json = JsonSerializer.Serialize(payload, CamelCase);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
}
