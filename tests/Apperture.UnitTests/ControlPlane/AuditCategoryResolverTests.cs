using Apperture.ControlPlane.Security.Audit;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class AuditCategoryResolverTests
{
    [Theory]
    [InlineData("execution.workflow_start.blocked", AuditCategoryResolver.CategoryLifecycle)]
    [InlineData("execution.operation_start.blocked", AuditCategoryResolver.CategoryLifecycle)]
    [InlineData("runtime.composition.invalid_at_startup", AuditCategoryResolver.CategoryLifecycle)]
    public void Resolve_LifecycleActions_ReturnsLifecycle(string action, string expected)
    {
        AuditCategoryResolver.Resolve(action).Should().Be(expected);
    }

    [Theory]
    [InlineData("scheduler.schedule.dispatched", AuditCategoryResolver.CategoryScheduler)]
    [InlineData("scheduler.schedule.blocked_by_lifecycle", AuditCategoryResolver.CategoryScheduler)]
    [InlineData("scheduler.schedule.failed", AuditCategoryResolver.CategoryScheduler)]
    public void Resolve_SchedulerActions_ReturnsScheduler(string action, string expected)
    {
        AuditCategoryResolver.Resolve(action).Should().Be(expected);
    }

    [Theory]
    [InlineData("recovery.blocked_work.retry_requested", AuditCategoryResolver.CategoryRecovery)]
    [InlineData("recovery.schedules.run_cycle_requested", AuditCategoryResolver.CategoryRecovery)]
    public void Resolve_RecoveryActions_ReturnsRecovery(string action, string expected)
    {
        AuditCategoryResolver.Resolve(action).Should().Be(expected);
    }

    [Theory]
    [InlineData("module.lifecycle.enable", AuditCategoryResolver.CategoryModuleLifecycle)]
    [InlineData("module.lifecycle.disable", AuditCategoryResolver.CategoryModuleLifecycle)]
    public void Resolve_ModuleLifecycleActions_ReturnsModuleLifecycle(string action, string expected)
    {
        AuditCategoryResolver.Resolve(action).Should().Be(expected);
    }

    [Theory]
    [InlineData("governance.role.assignment.created", AuditCategoryResolver.CategoryGovernance)]
    [InlineData("governance.runtime.shutdown.request.created", AuditCategoryResolver.CategoryGovernance)]
    public void Resolve_GovernanceActions_ReturnsGovernance(string action, string expected)
    {
        AuditCategoryResolver.Resolve(action).Should().Be(expected);
    }

    [Theory]
    [InlineData("auth.local.login.success", AuditCategoryResolver.CategoryAuth)]
    [InlineData("user.created", AuditCategoryResolver.CategoryAuth)]
    public void Resolve_AuthActions_ReturnsAuth(string action, string expected)
    {
        AuditCategoryResolver.Resolve(action).Should().Be(expected);
    }

    [Fact]
    public void Resolve_UnknownAction_ReturnsOther()
    {
        AuditCategoryResolver.Resolve("unknown.action").Should().Be(AuditCategoryResolver.CategoryOther);
    }

    [Fact]
    public void Resolve_EmptyOrNull_ReturnsOther()
    {
        AuditCategoryResolver.Resolve("").Should().Be(AuditCategoryResolver.CategoryOther);
    }
}
