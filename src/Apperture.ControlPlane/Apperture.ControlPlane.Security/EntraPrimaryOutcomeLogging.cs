using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// What a caller of <see cref="IEntraPrimaryAuthenticationPolicy"/> says about an outcome that did not change the credential and
/// left no row of its own. Every other outcome is already recorded (the change, or the attempt row of a deferral or a protected
/// account) or needs no record (nothing to disable), so the callers add nothing and the policy logs nothing: one place says it. The
/// sign-in or association that called the policy succeeds either way; the policy runs again at the next Entra sign-in.
/// </summary>
internal static class EntraPrimaryOutcomeLogging
{
    public static void Log(ILogger logger, UserId userId, EntraPrimaryLocalDisableOutcome outcome)
    {
        switch (outcome)
        {
            case EntraPrimaryLocalDisableOutcome.NotAppliedBusy:
                logger.LogWarning(
                    "The Entra-primary local-authentication policy was not applied for {UserId}: another change to administrator access is in progress. It is applied again at the next Entra sign-in.",
                    userId.Value);
                break;

            case EntraPrimaryLocalDisableOutcome.DeferredAdministratorViabilityUnavailable:
                logger.LogWarning(
                    "The Entra-primary local-authentication policy was deferred for {UserId}: the sign-in configuration could not be established, so whether disabling local sign-in is safe is not known. Local sign-in stays enabled and the policy is applied again at the next Entra sign-in.",
                    userId.Value);
                break;

            case EntraPrimaryLocalDisableOutcome.Disabled:
            case EntraPrimaryLocalDisableOutcome.AlreadyDisabled:
            case EntraPrimaryLocalDisableOutcome.NoLocalCredential:
            case EntraPrimaryLocalDisableOutcome.ProtectedEmergencyAccount:
            case EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator:
            case EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator:
                break;
        }
    }
}
