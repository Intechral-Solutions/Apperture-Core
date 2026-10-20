using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// The reading of every <c>sp_getapplock</c> return code and the typed results of the administrator-set gate (A10.2b.15). The
/// codes that cannot be produced on demand on a server (cancelled, a failed call) are proven here, as a pure function; the real
/// timeout, deadlock and cancellation are proven on SQL Server.
/// </summary>
public sealed class PlatformAdministratorSetLockTests
{
    [Theory]
    [InlineData(0, PlatformAdministratorSetLockOutcome.Granted)]
    [InlineData(1, PlatformAdministratorSetLockOutcome.GrantedAfterWaiting)]
    [InlineData(-1, PlatformAdministratorSetLockOutcome.TimedOut)]
    [InlineData(-2, PlatformAdministratorSetLockOutcome.Cancelled)]
    [InlineData(-3, PlatformAdministratorSetLockOutcome.DeadlockVictim)]
    [InlineData(-999, PlatformAdministratorSetLockOutcome.Failed)]
    [InlineData(2, PlatformAdministratorSetLockOutcome.Failed)]
    [InlineData(-4, PlatformAdministratorSetLockOutcome.Failed)]
    public void Each_return_code_is_read_as_specified(int code, PlatformAdministratorSetLockOutcome expected)
    {
        PlatformAdministratorSetLock.Interpret(code).Should().Be(expected);
    }

    [Fact]
    public void A_missing_return_code_is_a_failed_call_not_a_grant()
    {
        PlatformAdministratorSetLock.Interpret(null).Should().Be(PlatformAdministratorSetLockOutcome.Failed);
    }

    [Theory]
    [InlineData(PlatformAdministratorSetLockOutcome.Granted, true)]
    [InlineData(PlatformAdministratorSetLockOutcome.GrantedAfterWaiting, true)]
    [InlineData(PlatformAdministratorSetLockOutcome.TimedOut, false)]
    [InlineData(PlatformAdministratorSetLockOutcome.Cancelled, false)]
    [InlineData(PlatformAdministratorSetLockOutcome.DeadlockVictim, false)]
    [InlineData(PlatformAdministratorSetLockOutcome.Failed, false)]
    public void Only_the_two_granted_outcomes_mean_the_gate_is_held(PlatformAdministratorSetLockOutcome outcome, bool held)
    {
        PlatformAdministratorSetLock.IsHeld(outcome).Should().Be(held);
    }

    [Fact]
    public void The_resource_and_the_bound_are_the_specified_ones()
    {
        PlatformAdministratorSetLock.ResourceName.Should().Be("apperture:platform-administrator-set");
        PlatformAdministratorSetLock.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void The_busy_result_carries_the_governance_busy_code_and_which_contention_it_was()
    {
        var busy = new PlatformAdministratorSetBusyException(PlatformAdministratorSetLockOutcome.DeadlockVictim);

        busy.Code.Should().Be("governance_operation_busy").And.Be(GovernanceBusyException.Code);
        busy.Outcome.Should().Be(PlatformAdministratorSetLockOutcome.DeadlockVictim);
        busy.Message.Should().NotContain("sp_getapplock", "an operator sees a transient conflict, never a lock call");
    }

    [Fact]
    public void An_infrastructure_failure_is_a_different_type_with_its_own_code_and_is_not_a_governance_outcome()
    {
        var failure = new PlatformAdministratorSetGateException(-999);

        failure.Code.Should().Be("administrator_set_gate_failed");
        failure.ReturnCode.Should().Be(-999);
        failure.Should().NotBeAssignableTo<InvalidOperationException>();
        GovernanceFailureResults.IsGovernanceOutcome(failure).Should().BeFalse(
            "a failed lock call is an internal failure (500), never a retry hint or a refusal");
    }

    [Fact]
    public void The_busy_result_maps_to_409_with_the_stable_code_and_a_retry_hint()
    {
        var busy = new PlatformAdministratorSetBusyException(PlatformAdministratorSetLockOutcome.TimedOut);

        GovernanceFailureResults.IsGovernanceOutcome(busy).Should().BeTrue();
        GovernanceFailureResults.CodeOf(busy).Should().Be("governance_operation_busy");

        var result = GovernanceFailureResults.Map(busy);
        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.StatusCode.Should().Be(409);
        var body = System.Text.Json.JsonSerializer.Serialize(conflict.Value);
        body.Should().Contain("governance_operation_busy").And.Contain("\"retry\":true");
    }
}
