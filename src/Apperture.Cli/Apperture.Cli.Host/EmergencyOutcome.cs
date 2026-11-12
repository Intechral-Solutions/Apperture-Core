using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.Cli.Host;

/// <summary>
/// The exit status of an emergency command. Scripts branch on these values, so each means one thing and none is reused for another.
/// </summary>
public static class EmergencyExitCodes
{
    /// <summary>The command finished and its effect is confirmed (for a read, the state was read).</summary>
    public const int Success = 0;

    /// <summary>
    /// The change was refused, or the operator stopped it: a last-administrator or fallback rule, a missing prerequisite, an unauthorised
    /// request. Nothing was changed. Repeating the same command gives the same answer until the cause is fixed.
    /// </summary>
    public const int Refused = 1;

    /// <summary>The command line or an input was not valid (unknown command or option, a wrong or missing code, a rejected password). Nothing was changed.</summary>
    public const int InvalidUsage = 2;

    /// <summary>Another change was in the way, or the change moved while it ran. Nothing was changed; the same command may be run again.</summary>
    public const int Retryable = 3;

    /// <summary>The configuration evidence a decision needed could not be established, so the change was not made. Nothing was changed.</summary>
    public const int EvidenceUnavailable = 4;

    /// <summary>
    /// The result of a change is not confirmed to the operator: the commit could not be established either way, or the change is known to have
    /// been applied but what it returned could not be read back. Check the state before doing anything else; do not repeat the command blindly.
    /// </summary>
    public const int OutcomeUnknown = 5;

    /// <summary>The command stopped on an unexpected error (a wiring or environment defect, a defect in the operation).</summary>
    public const int InternalFailure = 6;
}

/// <summary>What the operator is told about a command that did not succeed, and the exit status that says so.</summary>
/// <param name="ExitCode">One of <see cref="EmergencyExitCodes"/>.</param>
/// <param name="Message">What happened, in the operator's terms. Never carries a secret, a code or a connection detail.</param>
/// <param name="NextStep">What the operator should do next, when there is something specific to do.</param>
/// <param name="Code">The stable machine code of the outcome, when the failure carries one.</param>
public sealed record EmergencyOutcome(int ExitCode, string Message, string? NextStep = null, string? Code = null);

/// <summary>
/// Maps what the services throw to an <see cref="EmergencyOutcome"/>. A business outcome is read from its type and stable code, never
/// from a message. A failure this map does not know is an internal failure, never a refusal and never a success.
/// </summary>
public static class EmergencyOutcomeMapper
{
    private const string RetryHint = "Nothing was changed. You can run the command again.";
    private const string StartAgainHint = "Nothing was changed and the current authenticator is still active. Run 'emergency reset-mfa' again to start a new replacement.";

    public static EmergencyOutcome Map(Exception exception)
    {
        switch (exception)
        {
            // An unknown commit: neither a commit nor a rollback could be established. Never reported as failed or as unchanged.
            case AuditOutcomeUnknownException unknown:
                return new EmergencyOutcome(
                    EmergencyExitCodes.OutcomeUnknown,
                    $"The outcome could not be confirmed: the change may or may not have been applied. Operation: {unknown.OperationEventId}",
                    "Do not repeat the command yet. Run 'emergency status' to see what is in place, then decide.",
                    AuditOutcomeUnknownException.Code);

            // Applied, but the result could not be built back from committed state. The change stands; what it returned cannot be shown.
            case AuditCommittedResultUnavailableException committed:
                return new EmergencyOutcome(
                    EmergencyExitCodes.OutcomeUnknown,
                    $"The change was applied, but its result could not be read back, so nothing it returned can be shown. Operation: {committed.OperationEventId}",
                    "Run 'emergency status'. If recovery codes were being issued, run 'emergency regenerate-codes' to get a fresh set.",
                    AuditCommittedResultUnavailableException.Code);

            // The commit failed and verification confirmed that nothing was committed.
            case AuditOperationNotCompletedException notCompleted:
                return new EmergencyOutcome(
                    EmergencyExitCodes.Retryable,
                    $"{notCompleted.Message} Operation: {notCompleted.OperationEventId}",
                    "You can run the command again.");

            case PlatformAdministratorSetBusyException busy:
                return new EmergencyOutcome(EmergencyExitCodes.Retryable, busy.Message, RetryHint, busy.Code);
            case GovernanceBusyException:
                return new EmergencyOutcome(EmergencyExitCodes.Retryable, exception.Message, RetryHint, GovernanceBusyException.Code);

            // The lock call failed for a reason that is not contention: not safe to present as a retry.
            case PlatformAdministratorSetGateException gate:
                return new EmergencyOutcome(
                    EmergencyExitCodes.InternalFailure,
                    gate.Message,
                    "Nothing was changed. This is not contention: check the database connection and the installation, then try again.",
                    gate.Code);

            case GovernanceConflictException conflict:
                return MapConflict(conflict);

            case AuthorizationRefusalException refusal:
                return new EmergencyOutcome(EmergencyExitCodes.Refused, refusal.Message, "Nothing was changed.", refusal.Code);

            case EmergencyAccessException emergency:
                return MapEmergency(emergency);

            case GovernanceNotFoundException notFound:
                return new EmergencyOutcome(EmergencyExitCodes.InvalidUsage, notFound.Message, "Check the user and try again.", notFound.Code);
            case GovernanceValidationException validation:
                return new EmergencyOutcome(EmergencyExitCodes.InvalidUsage, validation.Message, null, validation.Code);

            case NativeMfaCodeNotValidException:
                return new EmergencyOutcome(
                    EmergencyExitCodes.InvalidUsage,
                    exception.Message,
                    "Nothing was changed. Enter the current code from the authenticator app.",
                    "authenticator_code_not_valid");

            case PasswordPolicyException policy:
                return new EmergencyOutcome(EmergencyExitCodes.InvalidUsage, policy.Message, string.Join(' ', policy.Evaluation.Suggestions));

            // A defect of an audited write: never a business refusal, whatever it derives from.
            case AuditInvariantException invariant:
                return new EmergencyOutcome(
                    EmergencyExitCodes.InternalFailure,
                    $"The operation stopped on an internal error and nothing was committed: {invariant.Message}",
                    "Do not retry blindly. Report this with the details above.");

            // The services state a few plain conditions this way (no pending enrolment, already enrolled, a wiring defect). Their messages are
            // literals written in the services and carry no input, so the message is shown.
            case InvalidOperationException invalid:
                return new EmergencyOutcome(
                    EmergencyExitCodes.InternalFailure,
                    invalid.Message,
                    "Run 'emergency status' to check the state before trying again.");

            case PlatformNotSupportedException unsupported:
                return new EmergencyOutcome(EmergencyExitCodes.InternalFailure, unsupported.Message, "Nothing was changed.");

            default:
                return new EmergencyOutcome(
                    EmergencyExitCodes.InternalFailure,
                    $"The command stopped on an unexpected error ({TypeChain(exception)}).",
                    "If it was changing something, run 'emergency status' before repeating it.");
        }
    }

    private static EmergencyOutcome MapConflict(GovernanceConflictException conflict)
    {
        var code = conflict.Code;
        switch (code)
        {
            // The configuration the decision needed could not be established; the change was not made.
            case GovernanceConflictCodes.AdministratorViabilityUnavailable:
                return new EmergencyOutcome(
                    EmergencyExitCodes.EvidenceUnavailable,
                    conflict.Message,
                    "Nothing was changed. Start or restart the Management Host so it can record the sign-in configuration it is running with, then run the command again. Commands that only add (provision, enroll-mfa, regenerate-codes, set-password) do not depend on it.",
                    code);

            // Retryable: another change got in the way, or the request names a replacement that is no longer the one in play.
            case GovernanceConflictCodes.AuthenticatorChanged:
            case GovernanceConflictCodes.AdministratorAccessChanged:
                return new EmergencyOutcome(EmergencyExitCodes.Retryable, conflict.Message, RetryHint, code);
            case GovernanceConflictCodes.RecoveryAccessPostureReduced:
            case GovernanceConflictCodes.AuthenticatorReplacementNoLongerValid:
            case GovernanceConflictCodes.AuthenticatorReplacementExpired:
                return new EmergencyOutcome(EmergencyExitCodes.Retryable, conflict.Message, StartAgainHint, code);

            case GovernanceConflictCodes.AuthenticatorNotEnrolled:
                return new EmergencyOutcome(
                    EmergencyExitCodes.Refused,
                    conflict.Message,
                    "Nothing was changed. Run 'emergency enroll-mfa' to enrol an authenticator; a replacement is never turned into an enrolment or a reset.",
                    code);

            case GovernanceConflictCodes.EntraIndependentFallbackRequired:
            case GovernanceConflictCodes.LastViableAdministrator:
            case GovernanceConflictCodes.LastViableSignInMethod:
            case GovernanceConflictCodes.LastLocalCredential:
                return new EmergencyOutcome(EmergencyExitCodes.Refused, conflict.Message, "Nothing was changed.", code);

            default:
                return new EmergencyOutcome(EmergencyExitCodes.Refused, conflict.Message, "Nothing was changed.", code);
        }
    }

    private static EmergencyOutcome MapEmergency(EmergencyAccessException ex)
    {
        switch (ex.Code)
        {
            case EmergencyAccessErrorCodes.StaleUpdate:
                return new EmergencyOutcome(EmergencyExitCodes.Retryable, ex.Message, RetryHint, ex.Code);

            // What the operator typed, or left out: the request was not valid.
            case EmergencyAccessErrorCodes.ConfirmFallbackRequired:
            case EmergencyAccessErrorCodes.UserNotFound:
            case EmergencyAccessErrorCodes.DuplicateUsername:
            case EmergencyAccessErrorCodes.ReservedUsername:
                return new EmergencyOutcome(EmergencyExitCodes.InvalidUsage, ex.Message, "Nothing was changed.", ex.Code);

            default:
                return new EmergencyOutcome(EmergencyExitCodes.Refused, ex.Message, "Nothing was changed.", ex.Code);
        }
    }

    /// <summary>The type names of an unexpected exception and its causes, never a message: a message can carry what the operator typed.</summary>
    private static string TypeChain(Exception exception)
    {
        var names = new List<string>();
        for (var current = exception; current is not null && names.Count < 4; current = current.InnerException)
            names.Add(current.GetType().Name);
        return string.Join(" <- ", names);
    }
}
