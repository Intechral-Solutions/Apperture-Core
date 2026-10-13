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
/// AUD-1.4 slice 7: the runtime shutdown request locks the runtime identity row, inserts the queued request and stages its audit
/// row in one transaction only because the audited unit and the service hold the request's one scoped
/// <see cref="AppertureDbContext"/>. This resolves the service with the registration the Management host uses and follows its
/// dependencies to the context they hold, so a lifetime change in any registration cannot move the request outside the unit's
/// transaction. The atomicity itself is proven against SQL Server in <c>RuntimeShutdownRequestSqlServerTests</c>.
/// </summary>
public sealed class RuntimeControlServiceSharesTheUnitContextTests
{
    [Fact]
    public void In_one_scope_the_unit_and_the_service_hold_the_same_context()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var control = scope.ServiceProvider.GetRequiredService<IRuntimeControlService>()
            .Should().BeOfType<RuntimeControlService>().Subject;

        Dependency(control, "_auditedUnitOfWork").Should().BeOfType<SqlAuditedUnitOfWork>("the unit is registered, so the request never refuses to run");
        Context(Dependency(control, "_auditedUnitOfWork")).Should().BeSameAs(context, "the unit opens the transaction on the scope's context");
        Context(control).Should().BeSameAs(context, "the lock, the insert and the save join the unit's transaction");
        Dependency(control, "_runtimeIdentity").Should().BeOfType<SqlRuntimeIdentityService>();
        Context(Dependency(control, "_runtimeIdentity")).Should().BeSameAs(context, "the early existence and capability check reads on the same scope");
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

        // Registered by the Management host itself (Program.cs), beside the governance extension.
        services.AddScoped<Apperture.Core.Security.Trust.IRuntimeIdentityService, SqlRuntimeIdentityService>();
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
