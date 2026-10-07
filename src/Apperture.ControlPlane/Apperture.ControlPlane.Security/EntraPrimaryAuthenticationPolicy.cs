using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
/// <remarks>
/// AUD-1.2: the policy acts on its own, as <c>system:entra-primary-policy</c>, after a user's Microsoft link is proven; the
/// user is the target. The checks and the disable commit with their row in one audited unit of work. Leaving local sign-in
/// enabled (Recovery Access, or the last Entra-independent platform administrator) changes nothing, so those outcomes are W3
/// attempt rows written after the transaction (D6).
/// </remarks>
public sealed class EntraPrimaryAuthenticationPolicy : IEntraPrimaryAuthenticationPolicy
{
    /// <summary>The automatic policy, recorded as a named subsystem.</summary>
    internal static readonly AuditActor PolicyActor = AuditActor.System("entra-primary-policy");

    private readonly IUserRepository _users;
    private readonly ILocalCredentialRepository _credentials;
    private readonly IRoleRepository _roles;
    private readonly ILogger<EntraPrimaryAuthenticationPolicy> _logger;
    private readonly IEmergencyAccessService? _emergencyAccess;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;

    public EntraPrimaryAuthenticationPolicy(
        IUserRepository users,
        ILocalCredentialRepository credentials,
        IRoleRepository roles,
        ILogger<EntraPrimaryAuthenticationPolicy> logger,
        IEmergencyAccessService? emergencyAccess = null,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null)
    {
        _users = users;
        _credentials = credentials;
        _roles = roles;
        _logger = logger;
        _emergencyAccess = emergencyAccess;
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("The Entra-primary policy requires the audited unit of work.");

    private IAuditEventWriter Events =>
        _auditEvents ?? throw new InvalidOperationException("The Entra-primary policy requires the audit event writer.");

    /// <inheritdoc />
    public async ValueTask<EntraPrimaryLocalDisableOutcome> ApplyAfterEntraProvenAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        AuditEvent? kept = null;
        var outcome = await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.LocalDisabledEntraPrimary, PolicyActor),
            async (transaction, ct) =>
            {
                kept = null;
                var user = await _users.GetByIdAsync(userId, ct);
                var credential = user is null ? null : await _credentials.GetByUserIdAsync(userId, ct);
                if (user is null || credential is null || credential.IsDisabled)
                {
                    transaction.DeclareNoOp();
                    return credential is { IsDisabled: true }
                        ? EntraPrimaryLocalDisableOutcome.AlreadyDisabled
                        : EntraPrimaryLocalDisableOutcome.NoLocalCredential;
                }

                if (user.IsBreakGlassEligible)
                {
                    kept = AuditEvents.RecoveryAccessLocalProtected(userId);
                    transaction.DeclareNoOp();
                    return EntraPrimaryLocalDisableOutcome.ProtectedEmergencyAccount;
                }

                // Every condition the decision depends on is read in this transaction.
                var emergencyHealthy = _emergencyAccess is not null
                    && (await _emergencyAccess.GetPostureAsync(ct)).IsHealthy;
                if (!emergencyHealthy
                    && IsPlatformAdministrator(user)
                    && await CountOtherEntraIndependentPlatformAdministratorsAsync(userId, ct) == 0)
                {
                    kept = AuditEvents.LocalDisableDeferredLastAdmin(userId);
                    transaction.DeclareNoOp();
                    return EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator;
                }

                var hashBefore = credential.PasswordHash;
                await _credentials.SetDisabledAsync(userId, disabled: true, ct);

                var after = await _credentials.GetByUserIdAsync(userId, ct);
                if (after is not null && !string.Equals(after.PasswordHash, hashBefore, StringComparison.Ordinal))
                {
                    _logger.LogError(
                        "Local credential material changed while disabling local authentication for {UserId}; this must never happen.",
                        userId.Value);
                }

                await transaction.StageAsync(AuditEvents.LocalDisabledEntraPrimary(userId, emergencyHealthy), ct);
                return EntraPrimaryLocalDisableOutcome.Disabled;
            },
            (_, _) => Task.FromResult(EntraPrimaryLocalDisableOutcome.Disabled),
            cancellationToken);

        if (kept is not null)
            await Events.TryWriteAsync(kept, PolicyActor, CancellationToken.None);
        return outcome;
    }

    /// <inheritdoc />
    public async ValueTask<LocalAuthenticationPresentation> DescribeAsync(
        User user,
        LocalCredentialData? credential,
        CancellationToken cancellationToken = default)
    {
        var entra = DescribeEntra(user);
        if (credential is null || !user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
        {
            return new LocalAuthenticationPresentation(
                entra,
                AuthenticationMethodStatuses.LocalNone,
                RecoverySafeguardActive: false,
                EmergencyAccessAccount: user.IsBreakGlassEligible);
        }

        if (!credential.IsDisabled)
        {
            if (user.IsBreakGlassEligible)
            {
                return new LocalAuthenticationPresentation(
                    entra,
                    AuthenticationMethodStatuses.LocalEmergencyAccess,
                    RecoverySafeguardActive: false,
                    EmergencyAccessAccount: true);
            }

            var emergencyHealthy = _emergencyAccess is not null
                && (await _emergencyAccess.GetPostureAsync(cancellationToken)).IsHealthy;
            var safeguard = !emergencyHealthy
                && IsPlatformAdministrator(user)
                && entra == AuthenticationMethodStatuses.EntraProven
                && await CountOtherEntraIndependentPlatformAdministratorsAsync(user.Id, cancellationToken) == 0;
            return new LocalAuthenticationPresentation(
                entra,
                safeguard ? AuthenticationMethodStatuses.LocalRecoverySafeguard : AuthenticationMethodStatuses.LocalEnabled,
                safeguard,
                EmergencyAccessAccount: false);
        }

        return new LocalAuthenticationPresentation(
            entra,
            AuthenticationMethodStatuses.LocalDisabled,
            RecoverySafeguardActive: false,
            EmergencyAccessAccount: user.IsBreakGlassEligible);
    }

    private static string DescribeEntra(User user)
    {
        var link = user.IdentityLinks.FirstOrDefault(l => l.Source == IdentitySource.Entra);
        if (link is null)
            return AuthenticationMethodStatuses.EntraNone;
        return link.IsProven ? AuthenticationMethodStatuses.EntraProven : AuthenticationMethodStatuses.EntraPending;
    }

    private static bool IsPlatformAdministrator(User user) =>
        user.AssignedRoleIds.Any(r =>
            string.Equals(r.Value, BootstrapConstants.PlatformAdminRoleId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Other Platform Administrators who can sign in with an enabled local credential independently of Entra.
    /// The designated emergency account is excluded: it is not a complete recovery path until MFA is enrolled.
    /// </summary>
    private async ValueTask<int> CountOtherEntraIndependentPlatformAdministratorsAsync(
        UserId exceptUserId,
        CancellationToken cancellationToken)
    {
        var adminIds = await _roles.ListUserIdsWithRoleAsync(
            new RoleId(BootstrapConstants.PlatformAdminRoleId),
            cancellationToken);
        var count = 0;
        foreach (var adminId in adminIds)
        {
            if (string.Equals(adminId.Value, exceptUserId.Value, StringComparison.Ordinal))
                continue;

            var admin = await _users.GetByIdAsync(adminId, cancellationToken);
            if (admin is null || admin.IsBreakGlassEligible || !admin.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
                continue;

            var cred = await _credentials.GetByUserIdAsync(adminId, cancellationToken);
            if (cred is { IsDisabled: false })
                count++;
        }

        return count;
    }
}
