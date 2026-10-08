using System.Reflection;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.4: the settings services change a setting row, the secret it owns and the audit rows in one transaction only because
/// the audited unit, the settings repository and the SQL secret store each resolve the request's one scoped
/// <see cref="AppertureDbContext"/>. <see cref="SecretStoreSharesTheUnitContextTests"/> pins the store and the unit. These tests
/// resolve the services the Management host registers and follow each one's own dependencies to the context it actually holds,
/// so a lifetime change in any registration cannot move a setting or its secret outside the unit.
/// </summary>
public sealed class SettingsServicesShareTheUnitContextTests
{
    [Fact]
    public void In_one_scope_both_settings_services_the_repository_the_secret_store_and_the_unit_hold_the_same_context()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;
        var context = scoped.GetRequiredService<AppertureDbContext>();

        var platform = scoped.GetRequiredService<IPlatformSettingsService>().Should().BeOfType<PlatformSettingsService>().Subject;
        var integration = scoped.GetRequiredService<IIntegrationSettingsService>().Should().BeOfType<IntegrationSettingsService>().Subject;

        foreach (var (name, service) in new (string, object)[] { ("platform", platform), ("integration", integration) })
        {
            var unit = Dependency(service, "_auditedUnitOfWork").Should().BeOfType<SqlAuditedUnitOfWork>($"the {name} service's unit").Subject;
            var repository = Dependency(service, "_repository").Should().BeOfType<SqlPlatformSettingValueRepository>($"the {name} service's repository").Subject;
            var provisioner = Dependency(service, "_secretProvisioner");
            var store = Dependency(provisioner, "_secretStore").Should().BeOfType<SqlSecretStore>($"the {name} service's secret store").Subject;

            Context(unit).Should().BeSameAs(context, $"the {name} service's audited unit transaction is the scope's");
            Context(repository).Should().BeSameAs(context, $"the {name} service's setting row is written on the unit's context");
            Context(store).Should().BeSameAs(context, $"the {name} service's secret is stored on the unit's context");
        }

        // The integration service also reaches the integration repository from inside the unit.
        Context(Dependency(integration, "_integrations")).Should().BeSameAs(context);
    }

    [Fact]
    public void The_two_services_in_one_scope_share_one_unit_and_separate_scopes_share_nothing()
    {
        using var provider = Services().BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Context(Dependency(first.ServiceProvider.GetRequiredService<IPlatformSettingsService>(), "_auditedUnitOfWork"))
            .Should().BeSameAs(Context(Dependency(first.ServiceProvider.GetRequiredService<IIntegrationSettingsService>(), "_auditedUnitOfWork")));
        Context(Dependency(first.ServiceProvider.GetRequiredService<IPlatformSettingsService>(), "_repository"))
            .Should().NotBeSameAs(Context(Dependency(second.ServiceProvider.GetRequiredService<IPlatformSettingsService>(), "_repository")));
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(Mock.Of<IHostEnvironment>());
        services.AddScoped(_ => Mock.Of<IPrincipalContext>());
        services.AddScoped(_ => Mock.Of<IAuthorizationEvaluationService>());
        // Collaborators of the option providers, which are not part of the transaction.
        services.AddScoped(_ => Mock.Of<IConnectorDefinitionCatalogReadService>());
        services.AddScoped(_ => Mock.Of<IRuntimeSlotResolver>());
        // The registration the integration host uses: the repository on the scope's context.
        services.AddScoped<IIntegrationRepository, SqlIntegrationRepository>();

        // No connection is opened: the tests only compare the instances the services hold.
        services.AddApperturePersistence(_ => "Server=unused;Database=unused;TrustServerCertificate=True");
        services.AddAppertureAuditRecorder();
        services.AddAppertureSqlSecrets(protector: new UnusedProtector());
        services.AddApperturePlatformSettingsManagementHost(configuration);
        return services;
    }

    private static object Dependency(object service, string field) =>
        service.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;

    private static AppertureDbContext Context(object service) => (AppertureDbContext)Dependency(service, "_db");

    private sealed class UnusedProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
