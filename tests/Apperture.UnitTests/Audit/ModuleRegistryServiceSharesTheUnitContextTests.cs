using System.Reflection;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.4 slice 7: a module lifecycle transition takes its lock, saves its installation row and stages its audit row in one
/// transaction only because the audited unit and the registry hold the request's one scoped <see cref="AppertureDbContext"/>. This
/// resolves the registry with the registration the Management host uses and follows its dependencies to the context they hold. The
/// worker host registers the same registry without the recorder (it only reads and discovers), and that registration must keep
/// resolving. The atomicity itself is proven against SQL Server in <c>ModuleLifecycleSqlServerTests</c>.
/// </summary>
public sealed class ModuleRegistryServiceSharesTheUnitContextTests
{
    [Fact]
    public void In_one_scope_the_unit_and_the_registry_hold_the_same_context()
    {
        using var provider = ManagementServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var registry = scope.ServiceProvider.GetRequiredService<IModuleRegistryService>()
            .Should().BeOfType<SqlModuleRegistryService>().Subject;

        var unit = Dependency(registry, "_auditedUnitOfWork");
        unit.Should().BeOfType<SqlAuditedUnitOfWork>("the unit is registered, so a transition never refuses to run");
        Dependency(unit!, "_db").Should().BeSameAs(context, "the unit opens the transaction on the scope's context");
        Dependency(registry, "_dbContext").Should().BeSameAs(context, "the lock, the save and the read join the unit's transaction");
        Dependency(registry, "_auditEventWriter").Should().NotBeNull("a refused transition is recorded as an attempt");
    }

    [Fact]
    public void The_worker_host_registration_without_the_recorder_still_resolves_the_registry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApperturePersistence(_ => "Server=unused;Database=unused;TrustServerCertificate=True");
        services.AddSingleton<IEnvironmentContext>(new Environment());
        services.AddScoped<IAuditService, SqlAuditService>();
        services.AddSingleton<IEnumerable<IModule>>(Array.Empty<IModule>());
        services.AddScoped<IModuleRegistryService, SqlModuleRegistryService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
        using var scope = provider.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<IModuleRegistryService>()
            .Should().BeOfType<SqlModuleRegistryService>().Subject;
        Dependency(registry, "_auditedUnitOfWork").Should().BeNull("the worker never changes a module's lifecycle");
        Dependency(registry, "_auditEventWriter").Should().BeNull();
    }

    private static ServiceCollection ManagementServices()
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

    private static object? Dependency(object service, string field) =>
        service.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service);

    private sealed class Environment : IEnvironmentContext
    {
        public string EnvironmentId => "test";
    }

    private sealed class UnusedProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
