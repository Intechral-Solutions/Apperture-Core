using System.Text.RegularExpressions;
using Apperture.ConnectorResolution;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Secrets;

/// <summary>
/// SEC-2.2: how the connection-material boundary is composed. The boundary needs the audit event writer, which the Worker Host did
/// not register before this package, so a worker composed the way it was composed would have failed to build a connection resolver
/// at all. These tests pin the composition of both hosts and of the registration extension, and that the hosts label slot
/// resolution as what it is in that host.
/// </summary>
public sealed class ConnectionMaterialResolverCompositionTests
{
    private static ServiceCollection Composed(Action<IServiceCollection> connectorResolution)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Mock.Of<IBindingRepository>());
        services.AddScoped(_ => Mock.Of<IConnectionRepository>());
        services.AddSingleton(Mock.Of<IConnectorDefinitionRegistry>());

        // No connection is opened: the services are only built.
        services.AddApperturePersistence(_ => "Server=unused;Database=unused;TrustServerCertificate=True");
        services.AddAppertureSqlSecrets(protector: Mock.Of<ISecretProtector>());
        services.AddAppertureAuditRecorder();
        connectorResolution(services);
        return services;
    }

    [Fact]
    public void The_boundary_builds_in_a_scope_with_the_audit_writer_the_hosts_register()
    {
        var services = Composed(s => s.AddConnectorResolution());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IConnectionMaterialResolver>().Should().BeOfType<ConnectionMaterialResolver>();
        scope.ServiceProvider.GetRequiredService<IConnectionResolver>().Should().BeOfType<DefaultConnectionResolver>();
        scope.ServiceProvider.GetRequiredService<IAuditEventWriter>().Should().NotBeNull();
    }

    [Fact]
    public void The_boundary_is_registered_once_however_many_extensions_compose_it()
    {
        var services = Composed(s =>
        {
            s.AddPlatformSqlConnector();
            s.AddConnectorResolution();
            s.AddPlatformSqlConnector();
        });

        services.Count(d => d.ServiceType == typeof(IConnectionMaterialResolver)).Should().Be(1);
        services.Single(d => d.ServiceType == typeof(IConnectionMaterialResolver)).Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void A_host_that_only_composes_the_sql_connector_still_gets_the_boundary_its_verifier_needs()
    {
        var services = Composed(s => s.AddPlatformSqlConnector());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IConnectionVerifier>().Should().BeOfType<PlatformSqlConnectionVerifier>();
    }

    [Theory]
    [InlineData(ConnectionResolutionPath.Run)]
    [InlineData(ConnectionResolutionPath.SettingsOptions)]
    public void The_hosts_slot_path_is_what_the_composition_says(ConnectionResolutionPath path)
    {
        var services = Composed(s => s.AddConnectorResolution(path));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ConnectionSlotResolution>().Path.Should().Be(path);
    }

    [Fact]
    public void The_worker_host_registers_the_audit_recorder_and_labels_slot_resolution_a_run_and_the_management_host_a_settings_option()
    {
        var worker = RepositorySource.Find("Apperture.RuntimeHost/Apperture.RuntimeHost.Windows/Apperture.RuntimeHost.Windows.WorkerHost/Program.cs");
        var management = RepositorySource.Find("Apperture.ControlPlane/Apperture.Management.Host/Program.cs");

        worker.Should().NotBeNull();
        management.Should().NotBeNull();
        worker!.Code.Should().Contain("AddAppertureAuditRecorder()", "the boundary writes its refusal row as the worker");
        Regex.IsMatch(worker.Code, @"AddConnectorResolution\(\s*\)").Should().BeTrue("a worker resolves slots to run steps, which is the default");
        management!.Code.Should().Contain("AddConnectorResolution(ConnectionResolutionPath.SettingsOptions)",
            "the Management Host resolves slots only for the integration settings options");
    }

    [Fact]
    public void No_supported_secret_store_operation_changes_a_descriptors_name_or_provider()
    {
        // The owner is verified on the descriptor and the version is read next. That is only sound while a descriptor's owner is
        // fixed when it is created, so nothing in the SQL store may assign them afterwards.
        var store = RepositorySource.Find("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlSecretStore.cs");

        store.Should().NotBeNull();
        Regex.Matches(store!.Code, @"\b\w+\s*\.\s*(Name|Provider)\s*=(?!=)")
            .Select(m => m.Value)
            .Should().BeEmpty("a descriptor's name and provider are set once, in the initializer that creates it");
        Regex.IsMatch(store.Code, @"Name\s*=\s*request\.Name", RegexOptions.CultureInvariant).Should().BeTrue("the guard still sees the creating initializer");
    }

    [Fact]
    public void Self_test_the_descriptor_guard_pattern_catches_an_assignment_and_ignores_an_initializer_and_a_comparison()
    {
        const string pattern = @"\b\w+\s*\.\s*(Name|Provider)\s*=(?!=)";

        Regex.IsMatch("descriptor.Name = other;", pattern).Should().BeTrue();
        Regex.IsMatch("entity.Provider = \"x\";", pattern).Should().BeTrue();
        Regex.IsMatch("var e = new E { Name = request.Name, Provider = request.Provider };", pattern).Should().BeFalse();
        Regex.IsMatch("if (a.Name == b.Name && a.Provider == b.Provider)", pattern).Should().BeFalse();
    }
}
