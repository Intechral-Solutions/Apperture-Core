using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
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
public sealed class EntraStepUpService : IEntraStepUpService
{
    private const string AuditCategory = "auth";

    private readonly IUserRepository _userRepository;
    private readonly IEntraInstanceRegistrationRepository _registrations;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<EntraStepUpService> _logger;

    public EntraStepUpService(
        IUserRepository userRepository,
        IEntraInstanceRegistrationRepository registrations,
        IAuditService audit,
        TimeProvider time,
        ILogger<EntraStepUpService> logger)
    {
        _userRepository = userRepository;
        _registrations = registrations;
        _audit = audit;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<EntraAssociationOutcome> CompleteAsync(
        UserId initiatingUserId,
        EntraIdentityAssertion assertion,
        string requestedContextId,
        IReadOnlyList<string> returnedAuthenticationContexts,
        bool developmentSimulated,
        CancellationToken cancellationToken = default)
    {
        var objectId = EntraTokenClaims.Normalize(assertion?.ObjectId);
        var tokenTenantId = EntraTokenClaims.Normalize(assertion?.TenantId);
        if (!EntraTokenClaims.IsValidObjectId(objectId) || !EntraTokenClaims.IsValidObjectId(tokenTenantId))
        {
            await AuditAsync(StrongAuthenticationAuditActionNames.StepUpFailed, initiatingUserId.Value, objectId, "reason=invalid_identity", cancellationToken);
            return EntraAssociationOutcome.Failure(EntraStepUpFailureCodes.InvalidIdentity);
        }

        var requested = EntraAuthenticationContextId.TryNormalize(requestedContextId);
        if (requested is null)
        {
            await AuditAsync(StrongAuthenticationAuditActionNames.StepUpFailed, initiatingUserId.Value, objectId, "reason=not_configured", cancellationToken);
            return EntraAssociationOutcome.Failure(EntraStepUpFailureCodes.NotConfigured);
        }

        var registration = await GetActiveRegistrationAsync(cancellationToken);
        var activeTenantId = registration is null
            ? null
            : EntraTokenClaims.Normalize(EntraDirectoryTenantResolver.TryResolveGraphTenantId(registration));
        if (!EntraTokenClaims.TenantMatches(tokenTenantId, activeTenantId))
        {
            await AuditAsync(StrongAuthenticationAuditActionNames.StepUpTenantMismatch, initiatingUserId.Value, objectId, $"tokenTenant={tokenTenantId}", cancellationToken);
            return EntraAssociationOutcome.Failure(EntraStepUpFailureCodes.TenantMismatch);
        }

        var owner = await _userRepository.FindIdentityLinkAsync(IdentitySource.Entra, objectId!, cancellationToken);
        if (owner is null)
        {
            await AuditAsync(StrongAuthenticationAuditActionNames.StepUpIdentityMismatch, initiatingUserId.Value, objectId, "reason=no_link", cancellationToken);
            return EntraAssociationOutcome.Failure(EntraStepUpFailureCodes.IdentityMismatch);
        }

        if (!string.Equals(owner.UserId.Value, initiatingUserId.Value, StringComparison.Ordinal))
        {
            await AuditAsync(
                StrongAuthenticationAuditActionNames.StepUpIdentityMismatch,
                initiatingUserId.Value,
                objectId,
                $"reason=different_user targetUserId={owner.UserId.Value}",
                cancellationToken);
            return EntraAssociationOutcome.Failure(EntraStepUpFailureCodes.IdentityMismatch);
        }

        if (!owner.Link.IsProven)
        {
            await AuditAsync(StrongAuthenticationAuditActionNames.StepUpFailed, initiatingUserId.Value, objectId, "reason=unproven", cancellationToken);
            return EntraAssociationOutcome.Failure(EntraStepUpFailureCodes.Unproven);
        }

        if (!string.IsNullOrEmpty(owner.Link.TenantId) &&
            !EntraTokenClaims.TenantMatches(tokenTenantId, owner.Link.TenantId))
        {
            await AuditAsync(StrongAuthenticationAuditActionNames.StepUpTenantMismatch, initiatingUserId.Value, objectId, "reason=stored_tenant_differs", cancellationToken);
            return EntraAssociationOutcome.Failure(EntraStepUpFailureCodes.TenantMismatch);
        }

        var contextSatisfied = returnedAuthenticationContexts.Any(v =>
            string.Equals(EntraAuthenticationContextId.TryNormalize(v), requested, StringComparison.OrdinalIgnoreCase));
        if (!contextSatisfied && !developmentSimulated)
        {
            await AuditAsync(
                StrongAuthenticationAuditActionNames.StepUpContextNotSatisfied,
                initiatingUserId.Value,
                objectId,
                $"requestedContext={requested}",
                cancellationToken);
            return EntraAssociationOutcome.Failure(EntraStepUpFailureCodes.AuthenticationContextNotSatisfied);
        }

        var details = developmentSimulated
            ? $"requestedContext={requested} developmentSimulated=1"
            : $"requestedContext={requested}";
        await AuditAsync(StrongAuthenticationAuditActionNames.StepUpSatisfied, initiatingUserId.Value, objectId, details, cancellationToken);
        return EntraAssociationOutcome.Success(initiatingUserId);
    }

    private async ValueTask<EntraInstanceRegistration?> GetActiveRegistrationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _registrations.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Active Entra registration could not be read; step-up tenant validation will reject.");
            return null;
        }
    }

    private async ValueTask AuditAsync(
        string action,
        string initiatingUserId,
        string? objectId,
        string extraDetails,
        CancellationToken cancellationToken)
    {
        var details = $"userId={initiatingUserId}";
        if (!string.IsNullOrEmpty(objectId))
            details += $" oid={objectId}";
        if (!string.IsNullOrEmpty(extraDetails))
            details += " " + extraDetails;

        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId(initiatingUserId),
            action,
            new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance),
            AuditCategory,
            _time.GetUtcNow(),
            new CorrelationId(null),
            details);

        try
        {
            await _audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append {AuditAction} audit record.", action);
        }
    }
}
