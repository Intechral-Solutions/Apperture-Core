using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>Stable failure codes for Entra strong-authentication step-up. Safe to surface in a redirect query string.</summary>
public static class EntraStepUpFailureCodes
{
    public const string TenantMismatch = EntraLinkFailureCodes.TenantMismatch;
    public const string InvalidIdentity = EntraLinkFailureCodes.InvalidIdentity;
    public const string NotConfigured = EntraLinkFailureCodes.NotConfigured;
    public const string UserNotFound = EntraLinkFailureCodes.UserNotFound;
    public const string IdentityMismatch = "identity_mismatch";
    public const string AuthenticationContextNotSatisfied = "authentication_context_not_satisfied";
    public const string Unproven = "unproven";

    /// <summary>The OIDC state did not carry the step-up continuation the callback requires (or carried an unknown one).</summary>
    public const string ContinuationInvalid = "continuation_invalid";

    /// <summary>The initiating session was a local one, and local sign-in is no longer available to that account (disabled instance-wide or credential disabled).</summary>
    public const string LocalSessionUnavailable = "local_session_unavailable";

    /// <summary>Step-up is not offered to this kind of session (emergency access, or a kind this build does not recognise). Initiation only.</summary>
    public const string SessionKindUnsupported = "session_kind_unsupported";
}

/// <summary>Completes an Entra strong-authentication step-up after the OIDC callback.</summary>
public interface IEntraStepUpService
{
    ValueTask<EntraAssociationOutcome> CompleteAsync(
        UserId initiatingUserId,
        EntraIdentityAssertion assertion,
        string requestedContextId,
        IReadOnlyList<string> returnedAuthenticationContexts,
        bool developmentSimulated,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
/// <remarks>
/// AUD-1.2: the actor of every row is the initiating user, carried in the sign-in handler's protected, correlation-bound
/// state and set from the authenticated session when the step-up started (D24). Refusals are W3 attempts; a satisfied step-up
/// is W2: its row commits before this method returns, and a failure to write it throws, so the stronger session is never
/// issued without it.
/// </remarks>
public sealed class EntraStepUpService : IEntraStepUpService
{
    private readonly IUserRepository _userRepository;
    private readonly IEntraInstanceRegistrationRepository _registrations;
    private readonly ILogger<EntraStepUpService> _logger;
    private readonly IAuditEventWriter? _auditEvents;

    public EntraStepUpService(
        IUserRepository userRepository,
        IEntraInstanceRegistrationRepository registrations,
        ILogger<EntraStepUpService> logger,
        IAuditEventWriter? auditEvents = null)
    {
        _userRepository = userRepository;
        _registrations = registrations;
        _logger = logger;
        _auditEvents = auditEvents;
    }

    private IAuditEventWriter Events =>
        _auditEvents ?? throw new InvalidOperationException("Step-up requires the audit event writer.");

    /// <inheritdoc />
    public async ValueTask<EntraAssociationOutcome> CompleteAsync(
        UserId initiatingUserId,
        EntraIdentityAssertion assertion,
        string requestedContextId,
        IReadOnlyList<string> returnedAuthenticationContexts,
        bool developmentSimulated,
        CancellationToken cancellationToken = default)
    {
        var actor = AuditActor.AuthenticatedUser(initiatingUserId);
        async ValueTask<EntraAssociationOutcome> RefuseAsync(AuditEvent row, string code)
        {
            await Events.TryWriteAsync(row, actor, CancellationToken.None);
            return EntraAssociationOutcome.Failure(code);
        }

        var objectId = EntraTokenClaims.Normalize(assertion?.ObjectId);
        var tokenTenantId = EntraTokenClaims.Normalize(assertion?.TenantId);
        if (!EntraTokenClaims.IsValidObjectId(objectId) || !EntraTokenClaims.IsValidObjectId(tokenTenantId))
            return await RefuseAsync(AuditEvents.StepUpFailed(initiatingUserId, "invalid_identity"), EntraStepUpFailureCodes.InvalidIdentity);

        var requested = EntraAuthenticationContextId.TryNormalize(requestedContextId);
        if (requested is null)
            return await RefuseAsync(AuditEvents.StepUpFailed(initiatingUserId, "not_configured", tokenTenantId, objectId), EntraStepUpFailureCodes.NotConfigured);

        // Read strictly: a failed read fails the step-up (no stronger session), never a recorded tenant mismatch.
        var registration = await _registrations.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        var activeTenantId = registration is null
            ? null
            : EntraTokenClaims.Normalize(EntraDirectoryTenantResolver.TryResolveGraphTenantId(registration));
        if (!EntraTokenClaims.TenantMatches(tokenTenantId, activeTenantId))
            return await RefuseAsync(AuditEvents.StepUpTenantMismatch(initiatingUserId, tokenTenantId!, objectId!), EntraStepUpFailureCodes.TenantMismatch);

        var owner = await _userRepository.FindIdentityLinkAsync(
            ProviderIdentityKey.Entra(tokenTenantId!, objectId!),
            cancellationToken);
        if (owner is null)
            return await RefuseAsync(AuditEvents.StepUpIdentityMismatch(initiatingUserId, tokenTenantId!, objectId!, otherUser: null), EntraStepUpFailureCodes.IdentityMismatch);

        if (!string.Equals(owner.UserId.Value, initiatingUserId.Value, StringComparison.Ordinal))
            return await RefuseAsync(AuditEvents.StepUpIdentityMismatch(initiatingUserId, tokenTenantId!, objectId!, owner.UserId), EntraStepUpFailureCodes.IdentityMismatch);

        if (!owner.Link.IsProven)
            return await RefuseAsync(AuditEvents.StepUpFailed(initiatingUserId, "unproven", tokenTenantId, objectId), EntraStepUpFailureCodes.Unproven);

        var contextSatisfied = returnedAuthenticationContexts.Any(v =>
            string.Equals(EntraAuthenticationContextId.TryNormalize(v), requested, StringComparison.OrdinalIgnoreCase));
        if (!contextSatisfied && !developmentSimulated)
            return await RefuseAsync(AuditEvents.StepUpContextNotSatisfied(initiatingUserId, requested), EntraStepUpFailureCodes.AuthenticationContextNotSatisfied);

        // W2: committed before the caller issues the stronger session; a failure here withholds it.
        await Events.WriteAsync(
            AuditEvents.StepUpSatisfied(initiatingUserId, requested, tokenTenantId!, objectId!, developmentSimulated),
            actor,
            cancellationToken);
        return EntraAssociationOutcome.Success(initiatingUserId);
    }

}
