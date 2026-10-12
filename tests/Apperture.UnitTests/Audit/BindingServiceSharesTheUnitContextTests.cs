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
/// AUD-1.4 slice 4: a binding is created or deleted with its audit row in one transaction only because the audited unit, the
/// binding, connection and integration repositories and the ownership gate all use the request's one scoped
/// <see cref="AppertureDbContext"/>. These tests resolve the service the Management host registers with the production
/// registration extensions and follow each participant's own dependency to the context it actually holds, so a lifetime change
/// in any registration cannot move part of a binding change outside the unit's transaction. The atomicity itself is proven
/// against SQL Server in <c>BindingLifecycleAuditSqlServerTests</c>.
/// </summary>
public sealed class BindingServiceSharesTheUnitContextTests
{
    [Fact]
    public void In_one_scope_the_unit_the_repositories_and_the_gate_hold_the_same_context_and_the_unit_is_registered()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var bindings = scope.ServiceProvider.GetRequiredService<IBindingService>()
            .Should().BeOfType<BindingService>().Subject;

        Dependency(bindings, "_auditedUnitOfWork").Should().BeOfType<SqlAuditedUnitOfWork>("the unit is registered, so no mutation refuses to run");
        Dependency(bindings, "_bindingRepository").Should().BeOfType<SqlBindingRepository>();
        Dependency(bindings, "_connectionRepository").Should().BeOfType<SqlConnectionRepository>();
        Dependency(bindings, "_integrationRepository").Should().BeOfType<SqlIntegrationRepository>();
        Dependency(bindings, "_ownershipGate").Should().BeOfType<SqlResourceOwnershipMutationGate>();

        Context(Dependency(bindings, "_auditedUnitOfWork")).Should().BeSameAs(context, "the unit opens the transaction on the scope's context");
        Context(Dependency(bindings, "_bindingRepository")).Should().BeSameAs(context, "the binding row joins the unit's transaction");
        Context(Dependency(bindings, "_connectionRepository")).Should().BeSameAs(context, "the connection row lock joins the unit's transaction");
        Context(Dependency(bindings, "_integrationRepository")).Should().BeSameAs(context, "the integration row lock joins the unit's transaction");
        Context(Dependency(bindings, "_ownershipGate")).Should().BeSameAs(context, "the gate requires the unit's transaction to be current");
        Context(bindings).Should().BeSameAs(context);
    }

    [Fact]
    public void Separate_scopes_get_separate_contexts_for_the_binding_service()
    {
        using var provider = Services().BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Context(first.ServiceProvider.GetRequiredService<IBindingService>())
            .Should().NotBeSameAs(Context(second.ServiceProvider.GetRequiredService<IBindingService>()));
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
