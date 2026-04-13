using System.Data;
using System.Linq;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.Sftp;
using Apperture.ConnectorResolution;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Apperture.UnitTests.Connector;

/// <summary>
/// Regression guard: <see cref="IConnectorClientFactoryContributor"/> registrations are enumerated and applied at startup.
/// </summary>
public sealed class ConnectorClientFactoryContributorPipelineTests
{
    [Fact]
    public async Task Connector_resolution_hosted_service_registers_platform_factories()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());
        services.AddConnectorResolution();
        services.AddPlatformSqlConnector();
        services.AddAppertureCampusNexusConnector();
        services.AddAppertureSftpConnector();

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.Should().ContainSingle("AddConnectorResolution registers exactly one IHostedService.");
        await hostedServices[0].StartAsync(CancellationToken.None);

        var registry = provider.GetRequiredService<IConnectorClientFactoryRegistry>();
        registry.GetFactory<IDbConnection>(WellKnownConnectorDefinitionIds.PlatformSql).Should().NotBeNull();
        registry.GetFactory<ICampusNexusApiClient>(WellKnownConnectorDefinitionIds.PlatformCampusNexus).Should().NotBeNull();
        registry.GetFactory<ISftpFileClient>(WellKnownConnectorDefinitionIds.PlatformSftp).Should().NotBeNull();
    }
}
