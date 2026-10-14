using System.Data;
using Apperture.ConnectorResolution;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.UnitTests.Conventions;

/// <summary>
/// How the one real tool provider is composed: registered by the SQL connector's own extension, scoped (it uses the scoped
/// secret resolver), with a side-effect-free constructor and constant availability, so building the provider set to answer
/// "which tools?" does no work and can never reach the remote system.
/// </summary>
public sealed class ConnectionToolProviderRegistrationTests
{
    private static ServiceCollection Composed()
    {
        var services = new ServiceCollection();
        services.AddPlatformSqlStructureBrowser();
        return services;
    }

    [Fact]
    public void M7_the_runtime_workers_connector_registration_does_not_compose_the_operator_tool()
    {
        var services = new ServiceCollection();

        services.AddPlatformSqlConnector();

        services.Where(d => d.ServiceType == typeof(IConnectionToolProvider)).Should().BeEmpty(
            "the structure browser is a management-plane tool; only the Management Host composes it");
        services.Any(d => d.ServiceType == typeof(IConnectorClientFactory<IDbConnection>)).Should().BeTrue();
    }

    [Fact]
    public void The_management_host_composes_the_browser_and_the_worker_host_does_not()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git")))
            root = root.Parent;
        var management = File.ReadAllText(Path.Combine(root!.FullName, "src", "Apperture.ControlPlane", "Apperture.Management.Host", "Program.cs"));
        var worker = File.ReadAllText(Path.Combine(root.FullName, "src", "Apperture.RuntimeHost", "Apperture.RuntimeHost.Windows", "Apperture.RuntimeHost.Windows.WorkerHost", "Program.cs"));

        management.Should().Contain("AddPlatformSqlStructureBrowser()");
        worker.Should().NotContain("AddPlatformSqlStructureBrowser");
    }

    [Fact]
    public void The_sql_provider_is_registered_by_the_sql_connectors_own_extension_and_only_once()
    {
        var services = Composed();
        services.AddPlatformSqlStructureBrowser(); // idempotent

        var registration = services.Where(d => d.ServiceType == typeof(IConnectionToolProvider)).Should().ContainSingle().Subject;
        registration.ImplementationType.Should().Be(typeof(PlatformSqlStructureBrowser));
    }

    [Fact]
    public void The_sql_provider_is_scoped_so_it_never_captures_a_scoped_secret_resolver()
    {
        Composed().Single(d => d.ServiceType == typeof(IConnectionToolProvider)).Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void The_provider_constructor_takes_only_the_resolver_the_factory_and_options_and_availability_is_a_constant()
    {
        typeof(PlatformSqlStructureBrowser).GetConstructors().Single().GetParameters().Select(p => p.ParameterType)
            .Should().BeEquivalentTo([typeof(IConnectionMaterialResolver), typeof(IConnectorClientFactory<IDbConnection>), typeof(SqlStructureBrowserOptions)]);

        typeof(PlatformSqlStructureBrowser).GetProperty(nameof(IConnectionToolProvider.Availability))!.GetGetMethod()!
            .GetMethodBody()!.GetILAsByteArray()!.Length.Should().BeLessThan(16, "a constant, not a computation or a probe");
    }

    [Fact]
    public void Only_platform_sql_has_a_provider_among_the_well_known_connectors()
    {
        var provider = new PlatformSqlStructureBrowser(null!, null!);

        provider.ConnectorDefinitionId.Should().Be(WellKnownConnectorDefinitionIds.PlatformSql);
        provider.ConnectorDefinitionId.Should().NotBe(WellKnownConnectorDefinitionIds.PlatformSftp);
        provider.ConnectorDefinitionId.Should().NotBe(WellKnownConnectorDefinitionIds.PlatformCampusNexus);
    }
}
