using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
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

    /// <summary>Emergency Access cannot be associated with Entra.</summary>
    public const string EmergencyAccount = "emergency_account";

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
/// <remarks>
/// <para>AUD-1.2. Every caller holds an identity the OpenID Connect handler has validated (signature, audience, lifetime,
/// nonce, and the correlation-bound protected state) before <c>OnTokenValidated</c>; this service then checks the tenant.
/// The tenant id and object id are therefore a verified external identity (D1): a sign-in this service refuses (tenant
/// mismatch, no link) is recorded as that <c>external</c> triple. An unusable oid or tid is refused unrecorded here; the
/// remote-failure path records those as <c>anonymous</c>.</para>
/// <para>Sign-in: proving a pending link is one audited unit of work of the user being signed in. Self-link: the initiating
/// user comes from the handler's protected state, set at challenge time from the authenticated session; every check and the
/// link or proof run in one unit as that user, and a refusal decided in it is written after it rolled back. Refreshing an
/// already proven link's sign-in time is bookkeeping and writes no row.</para>
/// </remarks>
public sealed class EntraIdentityAssociationService : IEntraIdentityAssociationService
{
    private readonly IUserRepository _userRepository;
    private readonly IEntraInstanceRegistrationRepository _registrations;
    private readonly IEntraDirectoryDiscoveryService _directoryDiscovery;
    private readonly ISecretResolver? _secretResolver;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EntraIdentityAssociationService> _logger;
    private readonly IEntraPrimaryAuthenticationPolicy? _entraPrimaryPolicy;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;

    public EntraIdentityAssociationService(
        IUserRepository userRepository,
        IEntraInstanceRegistrationRepository registrations,
        IEntraDirectoryDiscoveryService directoryDiscovery,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<EntraIdentityAssociationService> logger,
        ISecretResolver? secretResolver = null,
        IEntraPrimaryAuthenticationPolicy? entraPrimaryPolicy = null,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null)
    {
        _userRepository = userRepository;
        _registrations = registrations;
        _directoryDiscovery = directoryDiscovery;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
        _secretResolver = secretResolver;
        _entraPrimaryPolicy = entraPrimaryPolicy;
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Microsoft identity association requires the audited unit of work.");

    private IAuditEventWriter Events =>
        _auditEvents ?? throw new InvalidOperationException("Microsoft identity association requires the audit event writer.");

    /// <inheritdoc />
    public async ValueTask<EntraAssociationOutcome> CompleteSignInAsync(
        EntraIdentityAssertion assertion,
        CancellationToken cancellationToken = default)
    {
        var objectId = EntraTokenClaims.Normalize(assertion?.ObjectId);
        var tokenTenantId = EntraTokenClaims.Normalize(assertion?.TenantId);
        if (!EntraTokenClaims.IsValidObjectId(objectId) || !EntraTokenClaims.IsValidObjectId(tokenTenantId))
            return EntraAssociationOutcome.Failure(EntraLinkFailureCodes.InvalidIdentity);

        // The token is validated and its oid and tid are GUIDs: a verified external identity (D1).
        var external = AuditActor.ValidatedExternalIdentity(
            new AuditExternalActor(AuditExternalActor.EntraProvider, tokenTenantId!, objectId!));

        var registration = await _registrations.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        var activeTenantId = ResolveActiveTenantId(registration);
        if (!EntraTokenClaims.TenantMatches(tokenTenantId, activeTenantId))
        {
            await Events.TryWriteAsync(AuditEvents.EntraTenantMismatch(tokenTenantId!, objectId!, linkingUser: null), external, CancellationToken.None);
            return EntraAssociationOutcome.Failure(EntraLinkFailureCodes.TenantMismatch);
        }

        var key = ProviderIdentityKey.Entra(tokenTenantId!, objectId!);
        var owner = await _userRepository.FindIdentityLinkAsync(key, cancellationToken);
        if (owner is null)
        {
            await Events.TryWriteAsync(AuditEvents.EntraSignInRejectedUnlinked(tokenTenantId!, objectId!), external, CancellationToken.None);
            return EntraAssociationOutcome.Failure(EntraLinkFailureCodes.AccountNotLinked);
        }

        if (owner.Link.IsProven)
        {
            // Sign-in bookkeeping on a proven link: no security state changes, so no row.
            await _userRepository.CompleteEntraLinkProofAsync(
                tokenTenantId!, objectId!, _timeProvider.GetUtcNow(), registration?.Id, assertion!.Subject, cancellationToken);
        }
        else
        {
            await ProvePendingAsync(owner.UserId, key, registration?.Id, assertion!.Subject, cancellationToken);
        }

        // Re-evaluate on every successful Entra authentication. A previously deferred last-admin
        // disable must not stick once another Entra-independent Platform Administrator exists.
        await ApplyEntraPrimaryPolicyAsync(owner.UserId, cancellationToken);

        return EntraAssociationOutcome.Success(owner.UserId);
    }

    /// <summary>
    /// Proves <paramref name="key"/>'s pending link for its owner, in one unit as that user. Changes nothing (and records
    /// nothing) when, inside the transaction, the link is gone, belongs to someone else, or is already proven.
    /// </summary>
    private async Task ProvePendingAsync(
        UserId userId,
        ProviderIdentityKey key,
        string? registrationId,
        string? subject,
        CancellationToken cancellationToken)
    {
        var provenAt = _timeProvider.GetUtcNow();
        await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.EntraLinkProven, AuditActor.AuthenticatedUser(userId)),
            async (transaction, ct) =>
            {
                var current = await _userRepository.FindIdentityLinkAsync(key, ct);
                if (current is null || current.UserId != userId || current.Link.IsProven)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                await _userRepository.CompleteEntraLinkProofAsync(key.TenantId!, key.ExternalId, provenAt, registrationId, subject, ct);
                await transaction.StageAsync(
                    AuditEvents.EntraLinkProven(userId, RequireLinkId(current.Link), key.TenantId!, key.ExternalId), ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);
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

        // The initiating user is established by the protected sign-in flow (see remarks), never by request input.
        var actor = AuditActor.AuthenticatedUser(initiatingUserId);
        var key = ProviderIdentityKey.Entra(tokenTenantId!, objectId!);
        var now = _timeProvider.GetUtcNow();
        var newLinkId = IdentityLink.NewLinkId();
        AuditEvent? refusal = null;
        var proven = false;

        EntraAssociationOutcome outcome;
        try
        {
            outcome = await Unit.ExecuteAsync(
                new AuditedOperation(AuditActionNames.EntraLinked, actor, [AuditActionNames.EntraLinkProven]),
                async (transaction, ct) =>
                {
                    refusal = null;
                    proven = false;
                    EntraAssociationOutcome Refuse(AuditEvent? row, string code)
                    {
                        refusal = row;
                        transaction.DeclareNoOp();
                        return EntraAssociationOutcome.Failure(code);
                    }

                    // Every condition is read in this transaction (clarification 6).
                    var user = await _userRepository.GetByIdAsync(initiatingUserId, ct);
                    if (user is null)
                        return Refuse(null, EntraLinkFailureCodes.UserNotFound);
                    if (EmergencyAccessIdentity.IsSystemManaged(user))
                        return Refuse(AuditEvents.EntraLinkRefused(initiatingUserId, key.TenantId!, objectId!, "emergency_account"), EntraLinkFailureCodes.EmergencyAccount);

                    // Read strictly: a failed read fails the attempt (the unit may retry it), never a tenant-mismatch refusal.
                    var registration = await _registrations.GetActiveAsync(ct).ConfigureAwait(false);
                    if (!EntraTokenClaims.TenantMatches(tokenTenantId, ResolveActiveTenantId(registration)))
                        return Refuse(AuditEvents.EntraTenantMismatch(tokenTenantId!, objectId!, initiatingUserId), EntraLinkFailureCodes.TenantMismatch);

                    var existing = await _userRepository.FindIdentityLinkAsync(key, ct);
                    if (existing is not null && existing.UserId.Value != initiatingUserId.Value)
                        return Refuse(AuditEvents.EntraLinkRefused(initiatingUserId, key.TenantId!, objectId!, "owned_by_other_user"), EntraLinkFailureCodes.Collision);

                    var otherEntraLink = user.IdentityLinks.FirstOrDefault(
                        l => l.Source == IdentitySource.Entra && !string.Equals(l.ExternalId, objectId, StringComparison.OrdinalIgnoreCase));
                    if (otherEntraLink is not null)
                        return Refuse(AuditEvents.EntraLinkRefused(initiatingUserId, key.TenantId!, objectId!, "user_already_has_entra_link"), EntraLinkFailureCodes.AlreadyLinked);

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
                            MailSnapshot: Trim(assertion.Mail),
                            LinkId: newLinkId);
                        await _userRepository.AddIdentityLinkAsync(initiatingUserId, link, ct);
                        await transaction.StageAsync(AuditEvents.EntraLinked(initiatingUserId, newLinkId, key.TenantId!, objectId!), ct);
                        proven = true;
                        return EntraAssociationOutcome.Success(initiatingUserId);
                    }

                    if (existing.Link.IsProven)
                    {
                        // Already this user's proven identity: nothing changes (D7); the sign-in time is refreshed below.
                        transaction.DeclareNoOp();
                        return EntraAssociationOutcome.Success(initiatingUserId);
                    }

                    // An administrator's pending link for this user: the user's own sign-in proves it.
                    await _userRepository.CompleteEntraLinkProofAsync(key.TenantId!, objectId!, now, registration?.Id, assertion!.Subject, ct);
                    await transaction.StageAsync(
                        AuditEvents.EntraLinkProven(initiatingUserId, RequireLinkId(existing.Link), key.TenantId!, objectId!), ct);
                    proven = true;
                    return EntraAssociationOutcome.Success(initiatingUserId);
                },
                (_, _) => Task.FromResult(EntraAssociationOutcome.Success(initiatingUserId)),
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Another request linked the same identity between this transaction's read and its insert; nothing committed.
            _logger.LogWarning(ex, "Entra self-link could not be persisted for user {UserId}.", initiatingUserId.Value);
            refusal = AuditEvents.EntraLinkRefused(initiatingUserId, key.TenantId!, objectId!, "concurrent_insert");
            outcome = EntraAssociationOutcome.Failure(EntraLinkFailureCodes.Collision);
        }

        if (refusal is not null)
            await Events.TryWriteAsync(refusal, actor, CancellationToken.None);
        if (!outcome.Succeeded)
            return outcome;

        if (proven)
        {
            await ApplyEntraPrimaryPolicyAsync(initiatingUserId, cancellationToken);
        }
        else
        {
            var registration = await GetActiveRegistrationAsync(cancellationToken);
            await _userRepository.CompleteEntraLinkProofAsync(key.TenantId!, objectId!, now, registration?.Id, assertion!.Subject, cancellationToken);
        }

        return outcome;
    }

    private static string RequireLinkId(IdentityLink link) =>
        link.LinkId ?? throw new InvalidOperationException("A persisted identity link has an id.");

    /// <inheritdoc />
    public async ValueTask<IdentityLink> BuildPendingAdminLinkAsync(
        string objectId,
        CancellationToken cancellationToken = default)
    {
        var registration = await GetActiveRegistrationAsync(cancellationToken);
        return await BuildPendingAdminLinkAsync(objectId, registration, cancellationToken);
    }

    public async ValueTask<IdentityLink> BuildPendingAdminLinkAsync(
        string objectId,
        EntraInstanceRegistration? registration,
        CancellationToken cancellationToken = default)
    {
        var normalized = EntraTokenClaims.Normalize(objectId);
        if (!EntraTokenClaims.IsValidObjectId(normalized))
        {
            throw new IdentityLinkException(
                IdentityLinkErrorCodes.InvalidEntraExternalId,
                "Entra external id must be the directory object id (oid) as a GUID, not an email, UPN, or token subject.");
        }

        registration ??= await GetActiveRegistrationAsync(cancellationToken);
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

    /// <summary>
    /// The active registration for provenance only (a link's registration id and snapshot). Tenant checks read it strictly:
    /// a failed read there fails the sign-in or link instead of being recorded as a tenant mismatch (AUD-1.2 review).
    /// </summary>
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
        catch (AuditOutcomeUnknownException ex)
        {
            // It runs again at the next sign-in; until then the change may or may not have been applied.
            _logger.LogError(ex, "Entra-primary local-authentication policy for {UserId} has an unknown outcome (operation {OperationEventId}); it may have been applied.", userId.Value, ex.OperationEventId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entra-primary local-authentication policy could not be applied for {UserId}.", userId.Value);
        }
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
