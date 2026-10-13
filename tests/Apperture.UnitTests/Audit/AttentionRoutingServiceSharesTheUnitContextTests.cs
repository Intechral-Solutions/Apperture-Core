using System.Reflection;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
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
/// AUD-1.4 slice 7: an attention routing change, the integration row lock it takes and its audit row commit or roll back together
/// only because the audited unit, the routing service and the integration repository hold the request's one scoped
/// <see cref="AppertureDbContext"/>. This resolves the service with the registration the Management host uses and follows its
/// dependencies to the context they hold, so a lifetime change in any registration cannot move the write or the lock outside the
/// unit's transaction. The atomicity itself is proven against SQL Server in <c>AttentionRoutingSqlServerTests</c>.
/// </summary>
public sealed class AttentionRoutingServiceSharesTheUnitContextTests
{
    [Fact]
    public void In_one_scope_the_unit_the_service_and_the_integration_repository_hold_the_same_context()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var routing = scope.ServiceProvider.GetRequiredService<IAttentionRoutingManagementService>()
            .Should().BeOfType<AttentionRoutingManagementService>().Subject;

        Dependency(routing, "_auditedUnitOfWork").Should().BeOfType<SqlAuditedUnitOfWork>("the unit is registered, so no routing write refuses to run");
        Context(Dependency(routing, "_auditedUnitOfWork")).Should().BeSameAs(context, "the unit opens the transaction on the scope's context");
        Context(routing).Should().BeSameAs(context, "the routing row write joins the unit's transaction");
        Dependency(routing, "_integrations").Should().BeOfType<SqlIntegrationRepository>();
        Context(Dependency(routing, "_integrations")).Should().BeSameAs(context, "the integration row lock is taken in the unit's transaction");
    }

    private static ServiceCollection Services()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(Mock.Of<IHostEnvironment>());
        services.AddScoped(_ => Mock.Of<IRuntimeSlotResolver>());

        // No connection is opened: the test only compares the instances the service holds.
        services.AddApperturePersistence(_ => "Server=unused;Database=unused;TrustServerCertificate=True");
        services.AddAppertureAuditRecorder();
        services.AddAppertureSqlSecrets(protector: new UnusedProtector());
        services.AddAppertureIntegrationWorkflows();
        services.AddAppertureGovernance();
        services.AddAppertureConnectorWorkflows();
        services.AddApperturePlatformEvents(configuration);
        services.AddAppertureNotifications(configuration);
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
