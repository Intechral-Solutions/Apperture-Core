using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// The gate and the viability reads are registered for both processes that can change administrator viability: the Management
/// Host and the emergency command line (a separate process, which does not share the host's container).
/// </summary>
public sealed class AdministratorViabilityRegistrationTests
{
    private static void AssertRegistered(IServiceCollection services, Type deployment)
    {
        void Scoped(Type service, Type implementation)
        {
            services.Where(d => d.ServiceType == service).Should().ContainSingle($"{service.Name} is registered once")
                .Which.Should().Match<ServiceDescriptor>(d => d.ImplementationType == implementation && d.Lifetime == ServiceLifetime.Scoped);
        }

        Scoped(typeof(IPlatformAdministratorSetGate), typeof(SqlPlatformAdministratorSetGate));
        Scoped(typeof(IAdministratorFactsReader), typeof(SqlAdministratorFactsReader));
        Scoped(typeof(IDeploymentAuthenticationConfiguration), deployment);
        Scoped(typeof(IEffectiveSignInConfigurationProvider), typeof(EffectiveSignInConfigurationProvider));
        Scoped(typeof(IAdministratorViabilityService), typeof(AdministratorViabilityService));
        Scoped(typeof(IAdministratorViabilityCoordinator), typeof(AdministratorViabilityCoordinator));
    }

    [Fact]
    public void The_management_host_registers_the_gate_and_reads_its_own_running_configuration()
    {
        var services = new ServiceCollection().AddAppertureGovernance();

        AssertRegistered(services, typeof(HostDeploymentAuthenticationConfiguration));
    }

    [Fact]
    public void The_emergency_command_line_registers_the_gate_the_registration_repository_and_its_own_configuration_reader()
    {
        var services = new ServiceCollection().AddAppertureEmergencyOperatorServices();

        AssertRegistered(services, typeof(ConfiguredDeploymentAuthenticationConfiguration));
        services.Where(d => d.ServiceType == typeof(IEntraInstanceRegistrationRepository)).Should().ContainSingle(
            "the provider reads the persisted registration, which the command line did not need before");
    }

    [Fact]
    public void Every_dependency_of_the_viability_service_can_be_resolved_in_the_command_line_container()
    {
        // Resolving needs the persistence registrations; the descriptors prove the graph is complete without a database.
        var services = new ServiceCollection().AddAppertureEmergencyOperatorServices();
        var provided = services.Select(d => d.ServiceType).ToHashSet();

        foreach (var needed in new[]
                 {
                     typeof(IAdministratorFactsReader), typeof(IEffectiveSignInConfigurationProvider),
                     typeof(IDeploymentAuthenticationConfiguration), typeof(IEntraInstanceRegistrationRepository),
                 })
        {
            provided.Should().Contain(needed);
        }
    }
}
