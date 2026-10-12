using System.Reflection;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Subscriptions;
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
/// AUD-1.4 slice 6: a notification definition, subscription or managed contact, the rows that go with it and its audit rows commit
/// or roll back together only because the audited unit and every store and repository the services use hold the request's one
/// scoped <see cref="AppertureDbContext"/>. These tests resolve the services with the registration the Management host uses and
/// follow each participant's own dependency to the context it actually holds, so a lifetime change in any registration cannot move
/// a write outside the unit's transaction. The diagnostics service must hold the audit event writer, since a live send without
/// evidence is the failure this slice removes. The atomicity itself is proven against SQL Server.
/// </summary>
public sealed class NotificationServicesShareTheUnitContextTests
{
    [Fact]
    public void In_one_scope_the_unit_and_every_notification_store_hold_the_same_context()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var definitions = scope.ServiceProvider.GetRequiredService<INotificationDefinitionService>()
            .Should().BeOfType<NotificationDefinitionService>().Subject;
        Dependency(definitions, "_auditedUnitOfWork").Should().BeOfType<SqlAuditedUnitOfWork>("the unit is registered, so no mutation refuses to run");
        Context(Dependency(definitions, "_auditedUnitOfWork")).Should().BeSameAs(context);
        Context(Dependency(definitions, "_store")).Should().BeSameAs(context, "the definition row joins the unit's transaction");
        Context(Dependency(definitions, "_integrations")).Should().BeSameAs(context, "the integration row lock is taken in the unit's transaction");

        var subscriptions = scope.ServiceProvider.GetRequiredService<INotificationSubscriptionService>()
            .Should().BeOfType<NotificationSubscriptionService>().Subject;
        Dependency(subscriptions, "_auditedUnitOfWork").Should().BeOfType<SqlAuditedUnitOfWork>();
        Context(Dependency(subscriptions, "_auditedUnitOfWork")).Should().BeSameAs(context);
        Context(Dependency(subscriptions, "_subscriptions")).Should().BeSameAs(context);
        Context(Dependency(subscriptions, "_definitions")).Should().BeSameAs(context, "the definition row lock is taken in the unit's transaction");
        Context(Dependency(subscriptions, "_integrations")).Should().BeSameAs(context);
        var provisioner = Dependency(subscriptions, "_managedContactProvisioner");
        Context(Dependency(provisioner, "_contacts")).Should().BeSameAs(context, "a contact created for a subscription commits with it");

        var contacts = scope.ServiceProvider.GetRequiredService<IManagedContactManagementService>()
            .Should().BeOfType<ManagedContactManagementService>().Subject;
        Dependency(contacts, "_auditedUnitOfWork").Should().BeOfType<SqlAuditedUnitOfWork>();
        Context(Dependency(contacts, "_auditedUnitOfWork")).Should().BeSameAs(context);
        Context(Dependency(contacts, "_contacts")).Should().BeSameAs(context);
        Context(Dependency(contacts, "_groups")).Should().BeSameAs(context);
    }

    [Fact]
    public void The_diagnostics_service_holds_the_audit_event_writer()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();

        var diagnostics = scope.ServiceProvider.GetRequiredService<INotificationPipelineTestService>()
            .Should().BeOfType<NotificationPipelineTestService>().Subject;

        Dependency(diagnostics, "_auditEvents").Should().BeAssignableTo<IAuditEventWriter>().And.BeOfType<SqlAuditEventWriter>();
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

        // No connection is opened: the tests only compare the instances the services hold.
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
