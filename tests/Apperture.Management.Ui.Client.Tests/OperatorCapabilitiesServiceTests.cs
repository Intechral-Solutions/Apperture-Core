using System.Net;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>SEC-1A1 (REQ-5): a request that met 401 is not re-sent, including by the capabilities service's own retry.</summary>
public sealed class OperatorCapabilitiesServiceTests
{
    private static (OperatorCapabilitiesService Service, StubHttpMessageHandler Http) Create()
    {
        var http = new StubHttpMessageHandler();
        var client = new ManagementClient(new HttpClient(http) { BaseAddress = new Uri("https://api.example/api/") });
        return (new OperatorCapabilitiesService(client, NullLogger<OperatorCapabilitiesService>.Instance), http);
    }

    [Fact]
    public async Task A_401_is_not_retried()
    {
        var (service, http) = Create();
        http.Respond("control/capabilities", HttpStatusCode.Unauthorized);

        var dto = await service.RefreshAsync();

        http.Count("control/capabilities").Should().Be(1, "a request that met 401 is never re-sent");
        dto.CanViewConnections.Should().BeFalse("deny-all until a later successful fetch");
    }

    [Fact]
    public async Task A_transient_server_error_is_still_retried()
    {
        var (service, http) = Create();
        http.Respond("control/capabilities", HttpStatusCode.ServiceUnavailable);

        await service.RefreshAsync();

        http.Count("control/capabilities").Should().Be(3);
    }
}
