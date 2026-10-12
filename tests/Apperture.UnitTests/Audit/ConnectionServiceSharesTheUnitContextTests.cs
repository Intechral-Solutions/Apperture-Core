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
/// AUD-1.4 slice 5: a connection's secret material, its row and its audit rows commit or roll back together, which is what removes
/// the live-secret-first defect, only because the audited unit, the connection repository, the secret store behind the provisioner
/// and the ownership gate all use the request's one scoped <see cref="AppertureDbContext"/>. These tests resolve the service the
/// Management host registers with the production registration extensions and follow each participant's own dependency to the
/// context it actually holds, so a lifetime change in any registration cannot move a credential change outside the unit's
/// transaction. The atomicity itself is proven against SQL Server in <c>ConnectionLifecycleAuditSqlServerTests</c>.
/// </summary>
public sealed class ConnectionServiceSharesTheUnitContextTests
{
    [Fact]
    public void In_one_scope_the_unit_the_repository_the_secret_store_and_the_gate_hold_the_same_context()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var connections = scope.ServiceProvider.GetRequiredService<IConnectionManagementService>()
            .Should().BeOfType<ConnectionManagementService>().Subject;

        Dependency(connections, "_auditedUnitOfWork").Should().BeOfType<SqlAuditedUnitOfWork>("the unit is registered, so no mutation refuses to run");
        Dependency(connections, "_connectionRepository").Should().BeOfType<SqlConnectionRepository>();
        Dependency(connections, "_ownershipGate").Should().BeOfType<SqlResourceOwnershipMutationGate>();
        var provisioner = Dependency(connections, "_secretProvisioner").Should().BeOfType<ConnectionSecretMaterialProvisioner>().Subject;
        Dependency(provisioner, "_secretStore").Should().BeOfType<SqlSecretStore>("secret changes join the unit's transaction only through the SQL store");

        Context(Dependency(connections, "_auditedUnitOfWork")).Should().BeSameAs(context, "the unit opens the transaction on the scope's context");
        Context(Dependency(connections, "_connectionRepository")).Should().BeSameAs(context, "the connection row joins the unit's transaction");
        Context(Dependency(connections, "_ownershipGate")).Should().BeSameAs(context, "the gate requires the unit's transaction to be current");
        Context(Dependency(provisioner, "_secretStore")).Should().BeSameAs(context, "the secret store joins the unit's transaction instead of committing its own");
        Context(connections).Should().BeSameAs(context);
    }

    [Fact]
    public void Separate_scopes_get_separate_contexts_for_the_connection_service()
    {
        using var provider = Services().BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Context(first.ServiceProvider.GetRequiredService<IConnectionManagementService>())
            .Should().NotBeSameAs(Context(second.ServiceProvider.GetRequiredService<IConnectionManagementService>()));
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(Mock.Of<IHostEnvironment>());
        services.AddScoped(_ => Mock.Of<IRuntimeSlotResolver>());

        // No connection is opened: the tests only compare the instances the service holds.
        services.AddApperturePersistence(_ => "Server=unused;Database=unused;TrustServerCertificate=True");
        services.AddAppertureAuditRecorder();
        services.AddAppertureSqlSecrets(protector: new UnusedProtector());
        services.AddAppertureIntegrationWorkflows();
        services.AddAppertureGovernance();
        services.AddAppertureConnectorWorkflows();
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
