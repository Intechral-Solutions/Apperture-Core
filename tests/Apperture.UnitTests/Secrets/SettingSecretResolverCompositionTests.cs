using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Execution;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Engine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Secrets;

/// <summary>
/// SEC-2.3: how the setting-secret boundary is composed. The boundary needs a secret resolver and the audit event writer; the Worker
/// Host registers the audit recorder since SEC-2.2. These tests pin that the boundary builds in a scope, is registered once however
/// many extensions compose it, and is what the runtime accessor and the SMTP resolver are given.
/// </summary>
public sealed class SettingSecretResolverCompositionTests
{
    private static ServiceCollection Composed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // No connection is opened: the services are only built.
        services.AddApperturePersistence(_ => "Server=unused;Database=unused;TrustServerCertificate=True");
        services.AddAppertureSqlSecrets(protector: Mock.Of<ISecretProtector>());
        services.AddAppertureAuditRecorder();
        return services;
    }

    [Fact]
    public void The_boundary_builds_in_a_scope_with_the_secret_resolver_and_audit_writer_the_hosts_register()
    {
        var services = Composed();
        services.AddSettingSecretResolution();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISettingSecretResolver>().Should().BeOfType<SettingSecretResolver>();
    }

    [Fact]
    public void The_boundary_is_registered_once_however_many_extensions_compose_it_and_as_a_scoped_service()
    {
        var services = Composed();
        services.AddSettingSecretResolution();
        services.AddSettingSecretResolution();
        services.AddAppertureIntegrationRuntimeAccessors();

        services.Count(d => d.ServiceType == typeof(ISettingSecretResolver)).Should().Be(1);
        services.Single(d => d.ServiceType == typeof(ISettingSecretResolver)).Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void The_integration_settings_service_is_composed_with_the_classifier_that_retires_a_legacy_secret_only_on_evidence()
    {
        var constructor = typeof(IntegrationSettingsService).GetConstructors().Single();
        var last = constructor.GetParameters()[^1];

        last.ParameterType.Should().Be(typeof(IntegrationSettingSecretClassifier));
        last.HasDefaultValue.Should().BeTrue("a unit test may build the service without it, in which case a legacy secret is simply kept");

        var registration = Apperture.UnitTests.Audit.RepositorySource.Find(
            "Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.PlatformSettings.cs");
        registration.Should().NotBeNull();
        registration!.Code.Should().Contain("AddScoped<IntegrationSettingSecretClassifier>()", "the host's service container gives the service the classifier");
        registration.Code.Should().Contain("AddScoped<IntegrationSettingsService>()");
    }

    [Fact]
    public void The_worker_hosts_runtime_accessor_is_given_the_boundary_and_the_smtp_resolver_is_given_it_too()
    {
        var services = Composed();
        services.AddScoped(_ => Mock.Of<IIntegrationSettingsResolver>());
        services.AddAppertureIntegrationRuntimeAccessors();
        services.AddScoped<IEmailSmtpCredentialResolver, EmailSmtpCredentialResolver>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IIntegrationSettingsAccessor>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IEmailSmtpCredentialResolver>().Should().BeOfType<EmailSmtpCredentialResolver>();
        scope.ServiceProvider.GetRequiredService<IIntegrationExecutionScopeAccessor>().Should().NotBeNull();
    }
}
