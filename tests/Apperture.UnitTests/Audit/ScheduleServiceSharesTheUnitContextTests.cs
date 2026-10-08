using System.Reflection;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.4 slice 3: the schedule delete removes the graph and stages its audit row in one transaction only because the audited
/// unit, the schedule repository, the ownership gate and the integration repository all use the request's one scoped
/// <see cref="AppertureDbContext"/>. These tests resolve the service the Management host registers, with the production
/// registration extensions, and follow each participant's own dependency to the context it actually holds, so a lifetime change
/// in any registration cannot move part of a delete outside the unit's transaction. The row's atomicity itself is proven against
/// SQL Server in <c>ScheduleDeleteAuditSqlServerTests</c>.
/// </summary>
public sealed class ScheduleServiceSharesTheUnitContextTests
{
    [Fact]
    public void In_one_scope_the_unit_the_repository_the_gate_and_the_integration_repository_hold_the_same_context()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var schedules = scope.ServiceProvider.GetRequiredService<IScheduleManagementService>()
            .Should().BeOfType<SqlScheduleManagementService>().Subject;

        Dependency(schedules, "_auditedUnitOfWork").Should().BeOfType<SqlAuditedUnitOfWork>();
        Dependency(schedules, "_repo").Should().BeOfType<SqlScheduleRepository>();
        Dependency(schedules, "_ownershipGate").Should().BeOfType<SqlResourceOwnershipMutationGate>();
        Dependency(schedules, "_integrationRepository").Should().BeOfType<SqlIntegrationRepository>();

        Context(Dependency(schedules, "_auditedUnitOfWork")).Should().BeSameAs(context, "the unit opens the transaction on the scope's context");
        Context(Dependency(schedules, "_repo")).Should().BeSameAs(context, "the graph removal joins the unit's transaction");
        Context(Dependency(schedules, "_ownershipGate")).Should().BeSameAs(context, "the gate requires the unit's transaction to be current");
        Context(Dependency(schedules, "_integrationRepository")).Should().BeSameAs(context);
        Context(schedules).Should().BeSameAs(context);
    }

    [Fact]
    public void The_refusal_writer_is_the_recorder_writer_on_its_own_context_so_it_never_joins_or_flushes_the_deletes_transaction()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();

        var schedules = (SqlScheduleManagementService)scope.ServiceProvider.GetRequiredService<IScheduleManagementService>();

        // A W3 row is its own short transaction on a context built from options (gate §13.4), not the request's context.
        var writer = Dependency(schedules, "_auditEvents").Should().BeOfType<SqlAuditEventWriter>().Subject;
        writer.GetType().GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic).Should().BeNull();
        writer.GetType().GetField("_contextOptions", BindingFlags.Instance | BindingFlags.NonPublic).Should().NotBeNull();
    }

    [Fact]
    public void Separate_scopes_get_separate_contexts_for_the_schedule_service()
    {
        using var provider = Services().BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Context(first.ServiceProvider.GetRequiredService<IScheduleManagementService>())
            .Should().NotBeSameAs(Context(second.ServiceProvider.GetRequiredService<IScheduleManagementService>()));
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Mock.Of<ISchedulerClaimOptions>());

        // No connection is opened: the tests only compare the instances the service holds.
        services.AddApperturePersistence(_ => "Server=unused;Database=unused;TrustServerCertificate=True");
        services.AddAppertureAuditRecorder();
        services.AddSchedulePersistence();
        services.AddAppertureIntegrationWorkflows();
        services.AddAppertureGovernance();
        return services;
    }

    private static object Dependency(object service, string field) =>
        service.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;

    private static AppertureDbContext Context(object service) => (AppertureDbContext)Dependency(service, "_db");
}
