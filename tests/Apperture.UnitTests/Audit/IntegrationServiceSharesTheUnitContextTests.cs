using System.Reflection;
using Apperture.ControlPlane.Security;
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
/// AUD-1.4 slice 3, work package 4b: the integration delete removes the integration, its overrides and the secrets it owned, and
/// stages its rows, in one transaction only because the audited unit, the integration repository (and its row lock), the
/// classifier's inventory and secret store, and the retirement's secret store all use the request's one scoped
/// <see cref="AppertureDbContext"/>. These tests resolve the service with the Management host's registration extensions and follow
/// each participant to the context it holds, so a lifetime change cannot move part of the delete, or its classification reads,
/// outside the unit's transaction. Atomicity itself is proven against SQL Server.
/// </summary>
public sealed class IntegrationServiceSharesTheUnitContextTests
{
    [Fact]
    public void In_one_scope_every_participant_of_the_integration_delete_holds_the_same_context()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var integrations = scope.ServiceProvider.GetRequiredService<IIntegrationManagementService>()
            .Should().BeOfType<IntegrationManagementService>().Subject;

        var unit = Dependency(integrations, "_auditedUnitOfWork").Should().BeOfType<SqlAuditedUnitOfWork>().Subject;
        var repository = Dependency(integrations, "_integrationRepository").Should().BeOfType<SqlIntegrationRepository>().Subject;
        var classifier = Dependency(integrations, "_settingSecretClassifier").Should().BeOfType<IntegrationSettingSecretClassifier>().Subject;
        var retirement = Dependency(integrations, "_settingSecretRetirement").Should().BeOfType<IntegrationSettingSecretRetirement>().Subject;

        Context(integrations).Should().BeSameAs(context, "the blocker counts and the removals run on the unit's context");
        Context(unit).Should().BeSameAs(context, "the unit opens the transaction on the scope's context");
        Context(repository).Should().BeSameAs(context, "the row lock is taken in the unit's transaction");
        Context(Dependency(classifier, "_inventory")).Should().BeSameAs(context, "classification reads inside the unit's transaction");
        Context(Dependency(classifier, "_secretStore").Should().BeOfType<SqlSecretStore>().Subject).Should().BeSameAs(context);
        Context(Dependency(retirement, "_secretStore").Should().BeOfType<SqlSecretStore>().Subject)
            .Should().BeSameAs(context, "a retirement joins the unit's transaction only on its context");
    }

    [Fact]
    public void Separate_scopes_get_separate_contexts_for_the_integration_service()
    {
        using var provider = Services().BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Context(first.ServiceProvider.GetRequiredService<IIntegrationManagementService>())
            .Should().NotBeSameAs(Context(second.ServiceProvider.GetRequiredService<IIntegrationManagementService>()));
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(Mock.Of<IHostEnvironment>());
        services.AddScoped(_ => Mock.Of<IConnectorDefinitionCatalogReadService>());
        services.AddScoped(_ => Mock.Of<IRuntimeSlotResolver>());

        // No connection is opened: the tests only compare the instances the service holds.
        services.AddApperturePersistence(_ => "Server=unused;Database=unused;TrustServerCertificate=True");
        services.AddAppertureAuditRecorder();
        services.AddAppertureSqlSecrets(protector: new UnusedProtector());
        services.AddAppertureIntegrationWorkflows();
        services.AddAppertureGovernance();
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
