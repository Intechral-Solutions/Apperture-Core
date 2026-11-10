using System.Reflection;
using Apperture.Cli.Host;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Cli;

/// <summary>
/// The typed-outcome map of the emergency command line: what each thing a service throws becomes, as an exit status and a message. The map
/// reads types and stable codes, never messages; the codes of the exit statuses are pinned, because scripts branch on them.
/// </summary>
public sealed class EmergencyOutcomeMapperTests
{
    [Fact]
    public void The_exit_statuses_are_the_documented_values()
    {
        EmergencyExitCodes.Success.Should().Be(0);
        EmergencyExitCodes.Refused.Should().Be(1);
        EmergencyExitCodes.InvalidUsage.Should().Be(2);
        EmergencyExitCodes.Retryable.Should().Be(3);
        EmergencyExitCodes.EvidenceUnavailable.Should().Be(4);
        EmergencyExitCodes.OutcomeUnknown.Should().Be(5);
        EmergencyExitCodes.InternalFailure.Should().Be(6);
    }

    public static IEnumerable<object[]> Typed() =>
    [
        // An unknown commit, a committed change whose result is unreadable: neither is a failure or a success.
        [new AuditOutcomeUnknownException("op-1"), EmergencyExitCodes.OutcomeUnknown],
        [new AuditCommittedResultUnavailableException("op-1", new InvalidOperationException("inner")), EmergencyExitCodes.OutcomeUnknown],
        // A confirmed rollback after the bounded reruns, and contention.
        [new AuditOperationNotCompletedException("op-1"), EmergencyExitCodes.Retryable],
        [new PlatformAdministratorSetBusyException(PlatformAdministratorSetLockOutcome.TimedOut), EmergencyExitCodes.Retryable],
        [new PlatformAdministratorSetBusyException(PlatformAdministratorSetLockOutcome.DeadlockVictim), EmergencyExitCodes.Retryable],
        [new GovernanceBusyException(), EmergencyExitCodes.Retryable],
        // The gate failing for a reason that is not contention is not a retry.
        [new PlatformAdministratorSetGateException(-999), EmergencyExitCodes.InternalFailure],
        // Evidence.
        [new GovernanceConflictException(GovernanceConflictCodes.AdministratorViabilityUnavailable, "The change could not be checked."), EmergencyExitCodes.EvidenceUnavailable],
        // Refusals.
        [new GovernanceConflictException(GovernanceConflictCodes.EntraIndependentFallbackRequired, "m"), EmergencyExitCodes.Refused],
        [new GovernanceConflictException(GovernanceConflictCodes.LastViableAdministrator, "m"), EmergencyExitCodes.Refused],
        [new AuthorizationRefusalException(AuthorizationRefusalCodes.GrantExceedsAuthority, "m"), EmergencyExitCodes.Refused],
        // Input.
        [new NativeMfaCodeNotValidException(), EmergencyExitCodes.InvalidUsage],
        [new GovernanceNotFoundException(GovernanceNotFoundKinds.User, "usr-x"), EmergencyExitCodes.InvalidUsage],
        [new GovernanceValidationException(GovernanceValidationCodes.RoleIdRequired, "m"), EmergencyExitCodes.InvalidUsage],
        [new EmergencyAccessException(EmergencyAccessErrorCodes.ConfirmFallbackRequired, "m"), EmergencyExitCodes.InvalidUsage],
        [new EmergencyAccessException(EmergencyAccessErrorCodes.UserNotFound, "m"), EmergencyExitCodes.InvalidUsage],
        // Emergency refusals and the one retryable one.
        [new EmergencyAccessException(EmergencyAccessErrorCodes.UnsafeOperation, "m"), EmergencyExitCodes.Refused],
        [new EmergencyAccessException(EmergencyAccessErrorCodes.AlreadyDesignated, "m"), EmergencyExitCodes.Refused],
        [new EmergencyAccessException(EmergencyAccessErrorCodes.StaleUpdate, "m"), EmergencyExitCodes.Retryable],
        // A defect of an audited write is never a refusal, though it derives from InvalidOperationException.
        [new AuditInvariantException("a precondition failed"), EmergencyExitCodes.InternalFailure],
        [new InvalidOperationException("Native MFA administration requires the audit event writer."), EmergencyExitCodes.InternalFailure],
        [new PlatformNotSupportedException("DPAPI"), EmergencyExitCodes.InternalFailure],
        [new TimeoutException(), EmergencyExitCodes.InternalFailure],
    ];

    [Theory]
    [MemberData(nameof(Typed))]
    public void A_service_outcome_maps_to_its_exit_status(Exception thrown, int expected)
    {
        EmergencyOutcomeMapper.Map(thrown).ExitCode.Should().Be(expected);
    }

    /// <summary>The expected status of every governance conflict code. A code added later must be decided here, not fall through.</summary>
    private static readonly Dictionary<string, int> ConflictCodes = new(StringComparer.Ordinal)
    {
        [GovernanceConflictCodes.RolePermissionsUnreadable] = EmergencyExitCodes.Refused,
        [GovernanceConflictCodes.LastViableAdministrator] = EmergencyExitCodes.Refused,
        [GovernanceConflictCodes.LastViableSignInMethod] = EmergencyExitCodes.Refused,
        [GovernanceConflictCodes.LastLocalCredential] = EmergencyExitCodes.Refused,
        [GovernanceConflictCodes.EntraIndependentFallbackRequired] = EmergencyExitCodes.Refused,
        [GovernanceConflictCodes.AdministratorViabilityUnavailable] = EmergencyExitCodes.EvidenceUnavailable,
        [GovernanceConflictCodes.RecoveryAccessPostureReduced] = EmergencyExitCodes.Retryable,
        [GovernanceConflictCodes.AdministratorAccessChanged] = EmergencyExitCodes.Retryable,
        [GovernanceConflictCodes.AuthenticatorChanged] = EmergencyExitCodes.Retryable,
        [GovernanceConflictCodes.AuthenticatorNotEnrolled] = EmergencyExitCodes.Refused,
        [GovernanceConflictCodes.AuthenticatorReplacementNoLongerValid] = EmergencyExitCodes.Retryable,
        [GovernanceConflictCodes.AuthenticatorReplacementExpired] = EmergencyExitCodes.Retryable,
    };

    [Fact]
    public void Every_governance_conflict_code_has_a_decided_exit_status()
    {
        var declared = typeof(GovernanceConflictCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        // Role lifecycle codes live elsewhere; this class holds exactly the codes the emergency commands can meet.
        declared.Should().BeEquivalentTo(ConflictCodes.Keys, "a new conflict code is a decision for the operator: refused, retryable or unavailable");

        foreach (var (code, expected) in ConflictCodes)
            EmergencyOutcomeMapper.Map(new GovernanceConflictException(code, "m")).ExitCode.Should().Be(expected, code);
    }

    [Fact]
    public void An_unknown_commit_is_neither_a_refusal_nor_an_internal_failure_and_says_to_check_before_repeating()
    {
        var outcome = EmergencyOutcomeMapper.Map(new AuditOutcomeUnknownException("op-9"));

        outcome.ExitCode.Should().Be(EmergencyExitCodes.OutcomeUnknown);
        outcome.Message.Should().Contain("may or may not have been applied").And.Contain("op-9");
        outcome.NextStep.Should().Contain("emergency status").And.Contain("Do not repeat");
        outcome.Code.Should().Be(AuditOutcomeUnknownException.Code);
    }

    [Fact]
    public void A_committed_result_that_cannot_be_read_says_the_change_stands()
    {
        var outcome = EmergencyOutcomeMapper.Map(new AuditCommittedResultUnavailableException("op-9", new Exception("x")));

        outcome.Message.Should().Contain("was applied");
        outcome.NextStep.Should().Contain("regenerate-codes");
    }

    [Fact]
    public void A_confirmed_rollback_says_nothing_was_changed_and_invites_a_repeat()
    {
        var outcome = EmergencyOutcomeMapper.Map(new AuditOperationNotCompletedException("op-2"));

        outcome.ExitCode.Should().Be(EmergencyExitCodes.Retryable);
        outcome.Message.Should().Contain("Nothing was changed");
        outcome.NextStep.Should().Contain("again");
    }

    [Fact]
    public void Evidence_unavailable_tells_the_operator_to_restore_the_host_and_names_the_commands_that_do_not_depend_on_it()
    {
        var outcome = EmergencyOutcomeMapper.Map(
            new GovernanceConflictException(GovernanceConflictCodes.AdministratorViabilityUnavailable, "The change could not be checked."));

        outcome.NextStep.Should().Contain("Management Host").And.Contain("Nothing was changed");
        outcome.Code.Should().Be(GovernanceConflictCodes.AdministratorViabilityUnavailable);
    }

    [Fact]
    public void A_replacement_that_is_no_longer_valid_or_expired_says_the_active_authenticator_stands_and_to_start_again()
    {
        foreach (var code in new[] { GovernanceConflictCodes.AuthenticatorReplacementNoLongerValid, GovernanceConflictCodes.AuthenticatorReplacementExpired })
        {
            var outcome = EmergencyOutcomeMapper.Map(new GovernanceConflictException(code, "m"));

            outcome.NextStep.Should().Contain("still active").And.Contain("reset-mfa");
        }
    }

    [Fact]
    public void A_replacement_of_nothing_is_a_refusal_that_points_to_enrolment_and_is_never_turned_into_one()
    {
        var outcome = EmergencyOutcomeMapper.Map(new GovernanceConflictException(GovernanceConflictCodes.AuthenticatorNotEnrolled, "m"));

        outcome.ExitCode.Should().Be(EmergencyExitCodes.Refused);
        outcome.NextStep.Should().Contain("enroll-mfa");
    }

    [Fact]
    public void A_password_the_policy_rejects_is_invalid_input()
    {
        var thrown = () => LocalPasswordPolicy.RequireRecoveryAccess(
            "short",
            LocalPasswordPolicy.ForLocalIdentity(EmergencyAccessIdentity.LocalUsername, EmergencyAccessIdentity.DisplayName));
        var policy = thrown.Should().Throw<PasswordPolicyException>().Which;

        EmergencyOutcomeMapper.Map(policy).ExitCode.Should().Be(EmergencyExitCodes.InvalidUsage);
    }

    [Fact]
    public void An_unexpected_failure_reports_type_names_and_never_its_message()
    {
        var outcome = EmergencyOutcomeMapper.Map(new ArgumentException("the value 482913 was rejected", new FormatException("secret-in-inner")));

        outcome.ExitCode.Should().Be(EmergencyExitCodes.InternalFailure);
        outcome.Message.Should().Contain("ArgumentException").And.Contain("FormatException");
        outcome.Message.Should().NotContain("482913").And.NotContain("secret-in-inner");
    }

    [Fact]
    public void Operator_facing_text_names_no_development_label()
    {
        var thrown = new Exception[]
        {
            new AuditOutcomeUnknownException("op"),
            new AuditCommittedResultUnavailableException("op", new Exception()),
            new AuditOperationNotCompletedException("op"),
            new GovernanceConflictException(GovernanceConflictCodes.AdministratorViabilityUnavailable, "m"),
            new GovernanceConflictException(GovernanceConflictCodes.AuthenticatorReplacementExpired, "m"),
            new GovernanceConflictException(GovernanceConflictCodes.AuthenticatorNotEnrolled, "m"),
            new NativeMfaCodeNotValidException(),
            new Exception("x"),
        };

        foreach (var outcome in thrown.Select(EmergencyOutcomeMapper.Map))
        {
            var text = $"{outcome.Message} {outcome.NextStep}";
            text.Should().NotMatchRegex(@"\b(WP\d|SC-\d|SR-\d|IAM-\d|WP4B)\b");
        }
    }
}
