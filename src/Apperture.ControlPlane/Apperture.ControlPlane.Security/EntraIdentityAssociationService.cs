using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Stable failure codes for Entra association. Safe to surface in a redirect query string.
/// </summary>
public static class EntraLinkFailureCodes
{
    /// <summary>The token's directory does not match the active registration, or contradicts the stored link.</summary>
    public const string TenantMismatch = "tenant_mismatch";

    /// <summary>The Microsoft identity is already linked to a different platform user.</summary>
    public const string Collision = "collision";

    /// <summary>The user already has a different Microsoft identity linked.</summary>
    public const string AlreadyLinked = "already_linked";

    /// <summary>The Microsoft identity is not linked to any platform user.</summary>
    public const string AccountNotLinked = "account_not_linked";

    /// <summary>Entra is not configured well enough to be used.</summary>
    public const string NotConfigured = "not_configured";

    /// <summary>The token did not carry a usable oid + tid pair.</summary>
    public const string InvalidIdentity = "invalid_identity";

    /// <summary>The initiating platform user no longer exists.</summary>
    public const string UserNotFound = "user_not_found";
}

/// <summary>
/// Audit action names for Entra identity linking (auth category).
/// </summary>
public static class EntraLinkAuditActionNames
{
    public const string Pending = "identity_link.entra.pending";
    public const string Linked = "identity_link.entra.linked";
    public const string Proven = "identity_link.entra.proven";
    public const string Collision = "identity_link.entra.collision";
    public const string TenantMismatch = "identity_link.entra.tenant_mismatch";
    public const string LegacyTenantCompleted = "identity_link.entra.legacy_tenant_completed";
    public const string LinkFailed = "identity_link.entra.link_failed";
    public const string RejectedUnlinked = "auth.entra.login.rejected_unlinked";
}

/// <inheritdoc />
public sealed class EntraIdentityAssociationService : IEntraIdentityAssociationService
{
    private const string AuditCategory = "auth";

    private readonly IUserRepository _userRepository;
    private readonly IEntraInstanceRegistrationRepository _registrations;
    private readonly IEntraDirectoryDiscoveryService _directoryDiscovery;
    private readonly ISecretResolver? _secretResolver;
    private readonly IConfiguration _configuration;
    private readonly IAuditService _audit;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EntraIdentityAssociationService> _logger;
    private readonly IEntraPrimaryAuthenticationPolicy? _entraPrimaryPolicy;

    public EntraIdentityAssociationService(
        IUserRepository userRepository,
        IEntraInstanceRegistrationRepository registrations,
        IEntraDirectoryDiscoveryService directoryDiscovery,
        IConfiguration configuration,
        IAuditService audit,
        TimeProvider timeProvider,
        ILogger<EntraIdentityAssociationService> logger,
        ISecretResolver? secretResolver = null,
        IEntraPrimaryAuthenticationPolicy? entraPrimaryPolicy = null)
    {
        _userRepository = userRepository;
        _registrations = registrations;
        _directoryDiscovery = directoryDiscovery;
        _configuration = configuration;
        _audit = audit;
        _timeProvider = timeProvider;
        _logger = logger;
        _secretResolver = secretResolver;
        _entraPrimaryPolicy = entraPrimaryPolicy;
    }

    /// <inheritdoc />
    public async ValueTask<EntraAssociationOutcome> CompleteSignInAsync(
        EntraIdentityAssertion assertion,
        CancellationToken cancellationToken = default)
    {
        var objectId = EntraTokenClaims.Normalize(assertion?.ObjectId);
        var tokenTenantId = EntraTokenClaims.Normalize(assertion?.TenantId);
        if (!EntraTokenClaims.IsValidObjectId(objectId) || !EntraTokenClaims.IsValidObjectId(tokenTenantId))
            return EntraAssociationOutcome.Failure(EntraLinkFailureCodes.InvalidIdentity);

        var registration = await GetActiveRegistrationAsync(cancellationToken);
        var activeTenantId = ResolveActiveTenantId(registration);
        if (!EntraTokenClaims.TenantMatches(tokenTenantId, activeTenantId))
        {
            await AuditAsync(EntraLinkAuditActionNames.TenantMismatch, objectId!, null, $"tokenTenant={tokenTenantId}", cancellationToken);
            return EntraAssociationOutcome.Failure(EntraLinkFailureCodes.TenantMismatch);
        }

        var owner = await _userRepository.FindIdentityLinkAsync(IdentitySource.Entra, objectId!, cancellationToken);
        if (owner is null)
        {
            await AuditAsync(EntraLinkAuditActionNames.RejectedUnlinked, objectId!, null, "reason=no_link", cancellationToken);
            return EntraAssociationOutcome.Failure(EntraLinkFailureCodes.AccountNotLinked);
        }

        if (!string.IsNullOrEmpty(owner.Link.TenantId) &&
            !EntraTokenClaims.TenantMatches(tokenTenantId, owner.Link.TenantId))
        {
            await AuditAsync(EntraLinkAuditActionNames.TenantMismatch, objectId!, owner.UserId.Value, "reason=stored_tenant_differs", cancellationToken);
            return EntraAssociationOutcome.Failure(EntraLinkFailureCodes.TenantMismatch);
        }

        var wasPending = !owner.Link.IsProven;
        var legacyTenantCompleted = string.IsNullOrEmpty(owner.Link.TenantId);

        await _userRepository.CompleteEntraLinkProofAsync(
            objectId!,
            _timeProvider.GetUtcNow(),
            tokenTenantId,
            registration?.Id,
            assertion!.Subject,
            cancellationToken);

        if (wasPending)
            await AuditAsync(EntraLinkAuditActionNames.Proven, objectId!, owner.UserId.Value, null, cancellationToken);
        if (legacyTenantCompleted)
            await AuditAsync(EntraLinkAuditActionNames.LegacyTenantCompleted, objectId!, owner.UserId.Value, $"tenantId={tokenTenantId}", cancellationToken);

        if (wasPending)
            await ApplyEntraPrimaryPolicyAsync(owner.UserId, cancellationToken);

        return EntraAssociationOutcome.Success(owner.UserId);
    }

    /// <inheritdoc />
    public async ValueTask<EntraAssociationOutcome> CompleteLinkAsync(
        UserId initiatingUserId,
        EntraIdentityAssertion assertion,
        CancellationToken cancellationToken = default)
    {
        var objectId = EntraTokenClaims.Normalize(assertion?.ObjectId);
        var tokenTenantId = EntraTokenClaims.Normalize(assertion?.TenantId);
        if (!EntraTokenClaims.IsValidObjectId(objectId) || !EntraTokenClaims.IsValidObjectId(tokenTenantId))
            return EntraAssociationOutcome.Failure(EntraLinkFailureCodes.InvalidIdentity);

        var user = await _userRepository.GetByIdAsync(initiatingUserId, cancellationToken);
        if (user is null)
            return EntraAssociationOutcome.Failure(EntraLinkFailureCodes.UserNotFound);

        var registration = await GetActiveRegistrationAsync(cancellationToken);
        var activeTenantId = ResolveActiveTenantId(registration);
        if (!EntraTokenClaims.TenantMatches(tokenTenantId, activeTenantId))
        {
            await AuditAsync(EntraLinkAuditActionNames.TenantMismatch, objectId!, initiatingUserId.Value, $"tokenTenant={tokenTenantId}", cancellationToken);
            return EntraAssociationOutcome.Failure(EntraLinkFailureCodes.TenantMismatch);
        }

        var existing = await _userRepository.FindIdentityLinkAsync(IdentitySource.Entra, objectId!, cancellationToken);
        if (existing is not null && existing.UserId.Value != initiatingUserId.Value)
        {
            await AuditAsync(EntraLinkAuditActionNames.Collision, objectId!, initiatingUserId.Value, "reason=oid_owned_by_other_user", cancellationToken);
            return EntraAssociationOutcome.Failure(EntraLinkFailureCodes.Collision);
        }

        var otherEntraLink = user.IdentityLinks.FirstOrDefault(
            l => l.Source == IdentitySource.Entra && !string.Equals(l.ExternalId, objectId, StringComparison.OrdinalIgnoreCase));
        if (otherEntraLink is not null)
        {
            await AuditAsync(EntraLinkAuditActionNames.LinkFailed, objectId!, initiatingUserId.Value, "reason=user_already_has_entra_link", cancellationToken);
            return EntraAssociationOutcome.Failure(EntraLinkFailureCodes.AlreadyLinked);
        }

        var now = _timeProvider.GetUtcNow();
        if (existing is null)
        {
            var link = new IdentityLink(
                IdentitySource.Entra,
                objectId!,
                LastAuthenticatedAt: now,
                TenantId: tokenTenantId,
                RegistrationId: registration?.Id,
                LinkedAt: now,
                ProviderSubject: assertion!.Subject,
                DisplayNameSnapshot: Trim(assertion.DisplayName),
                UserPrincipalNameSnapshot: Trim(assertion.UserPrincipalName),
                MailSnapshot: Trim(assertion.Mail));

            try
            {
                await _userRepository.AddIdentityLinkAsync(initiatingUserId, link, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Entra self-link could not be persisted for user {UserId}.", initiatingUserId.Value);
                await AuditAsync(EntraLinkAuditActionNames.Collision, objectId!, initiatingUserId.Value, "reason=concurrent_insert", cancellationToken);
                return EntraAssociationOutcome.Failure(EntraLinkFailureCodes.Collision);
            }

            await AuditAsync(EntraLinkAuditActionNames.Linked, objectId!, initiatingUserId.Value, null, cancellationToken);
            await ApplyEntraPrimaryPolicyAsync(initiatingUserId, cancellationToken);
            return EntraAssociationOutcome.Success(initiatingUserId);
        }

        // Already this user's identity: prove it (admin-created pending link) and stay idempotent.
        var wasPending = !existing.Link.IsProven;
        await _userRepository.CompleteEntraLinkProofAsync(
            objectId!,
            now,
            tokenTenantId,
            registration?.Id,
            assertion!.Subject,
            cancellationToken);

        await AuditAsync(
            wasPending ? EntraLinkAuditActionNames.Proven : EntraLinkAuditActionNames.Linked,
            objectId!,
            initiatingUserId.Value,
            wasPending ? "reason=self_link_proved_pending" : "reason=already_linked_idempotent",
            cancellationToken);

        if (wasPending)
            await ApplyEntraPrimaryPolicyAsync(initiatingUserId, cancellationToken);

        return EntraAssociationOutcome.Success(initiatingUserId);
    }

    /// <inheritdoc />
    public async ValueTask<IdentityLink> BuildPendingAdminLinkAsync(
        string objectId,
        CancellationToken cancellationToken = default)
    {
        var normalized = EntraTokenClaims.Normalize(objectId);
        if (!EntraTokenClaims.IsValidObjectId(normalized))
        {
            throw new IdentityLinkException(
                IdentityLinkErrorCodes.InvalidEntraExternalId,
                "Entra external id must be the directory object id (oid) as a GUID, not an email, UPN, or token subject.");
        }

        var registration = await GetActiveRegistrationAsync(cancellationToken);
        var tenantId = ResolveActiveTenantId(registration);
        var snapshot = await TryGetDirectorySnapshotAsync(registration, normalized!, cancellationToken);

        return new IdentityLink(
            IdentitySource.Entra,
            normalized!,
            LastAuthenticatedAt: null,
            TenantId: tenantId,
            RegistrationId: registration?.Id,
            LinkedAt: _timeProvider.GetUtcNow(),
            ProviderSubject: null,
            DisplayNameSnapshot: Trim(snapshot?.DisplayName),
            UserPrincipalNameSnapshot: Trim(snapshot?.UserPrincipalName),
            MailSnapshot: Trim(snapshot?.Mail));
    }

    private async ValueTask<EntraInstanceRegistration?> GetActiveRegistrationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _registrations.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Active Entra registration could not be read; tenant validation will reject.");
            return null;
        }
    }

    /// <summary>
    /// The directory the instance currently trusts. Falls back to the authority's tenant segment so an
    /// operator who configured only a tenant-specific authority still gets tenant validation.
    /// </summary>
    private static string? ResolveActiveTenantId(EntraInstanceRegistration? registration) =>
        registration is null ? null : EntraTokenClaims.Normalize(EntraDirectoryTenantResolver.TryResolveGraphTenantId(registration));

    /// <summary>
    /// Snapshots are convenience only: when Graph is unavailable the association still proceeds with oid + tid.
    /// </summary>
    private async ValueTask<EntraDirectoryUser?> TryGetDirectorySnapshotAsync(
        EntraInstanceRegistration? registration,
        string objectId,
        CancellationToken cancellationToken)
    {
        if (registration is null)
            return null;

        try
        {
            var secret = await EntraClientSecretAccess
                .TryResolveAsync(
                    registration,
                    _secretResolver,
                    _configuration[EntraAuthOptions.SectionName + ":" + nameof(EntraAuthOptions.ClientSecret)],
                    cancellationToken)
                .ConfigureAwait(false) ?? "";
            var tenantId = EntraDirectoryTenantResolver.TryResolveGraphTenantId(registration) ?? "";
            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(secret))
                return null;

            var context = new EntraDirectoryDiscoveryContext(tenantId, registration.ClientId, secret, registration.Id);
            var result = await _directoryDiscovery
                .GetUserByIdAsync(context, objectId, cancellationToken)
                .ConfigureAwait(false);
            return result.Success ? result.Data : null;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Directory snapshot unavailable for Entra association; continuing with object id only.");
            return null;
        }
    }

    private async ValueTask ApplyEntraPrimaryPolicyAsync(UserId userId, CancellationToken cancellationToken)
    {
        if (_entraPrimaryPolicy is null)
            return;
        try
        {
            await _entraPrimaryPolicy.ApplyAfterEntraProvenAsync(userId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entra-primary local-authentication policy could not be applied for {UserId}.", userId.Value);
        }
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async ValueTask AuditAsync(
        string action,
        string objectId,
        string? userId,
        string? extraDetails,
        CancellationToken cancellationToken)
    {
        var details = userId is null ? $"oid={objectId}" : $"oid={objectId} userId={userId}";
        if (!string.IsNullOrEmpty(extraDetails))
            details += " " + extraDetails;

        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId(objectId),
            action,
            new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance),
            AuditCategory,
            _timeProvider.GetUtcNow(),
            new CorrelationId(null),
            details);

        try
        {
            await _audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Auditing must never turn a rejected sign-in into a server error.
            _logger.LogWarning(ex, "Failed to append {AuditAction} audit record.", action);
        }
    }
}
