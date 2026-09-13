using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
public sealed class EntraPrimaryAuthenticationPolicy : IEntraPrimaryAuthenticationPolicy
{
    private const string AuditCategory = "auth";

    private readonly IUserRepository _users;
    private readonly ILocalCredentialRepository _credentials;
    private readonly IRoleRepository _roles;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<EntraPrimaryAuthenticationPolicy> _logger;
    private readonly IEmergencyAccessService? _emergencyAccess;

    public EntraPrimaryAuthenticationPolicy(
        IUserRepository users,
        ILocalCredentialRepository credentials,
        IRoleRepository roles,
        IAuditService audit,
        TimeProvider time,
        ILogger<EntraPrimaryAuthenticationPolicy> logger,
        IEmergencyAccessService? emergencyAccess = null)
    {
        _users = users;
        _credentials = credentials;
        _roles = roles;
        _audit = audit;
        _time = time;
        _logger = logger;
        _emergencyAccess = emergencyAccess;
    }

    /// <inheritdoc />
    public async ValueTask<EntraPrimaryLocalDisableOutcome> ApplyAfterEntraProvenAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return EntraPrimaryLocalDisableOutcome.NoLocalCredential;

        var credential = await _credentials.GetByUserIdAsync(userId, cancellationToken);
        if (credential is null)
            return EntraPrimaryLocalDisableOutcome.NoLocalCredential;
        if (credential.IsDisabled)
            return EntraPrimaryLocalDisableOutcome.AlreadyDisabled;

        if (user.IsBreakGlassEligible)
        {
            await AuditAsync(
                EmergencyAccessAuditActionNames.LocalProtected,
                userId.Value,
                $"userId={userId.Value} reason=designated_emergency_account",
                cancellationToken);
            return EntraPrimaryLocalDisableOutcome.ProtectedEmergencyAccount;
        }

        var emergencyHealthy = _emergencyAccess is not null
            && (await _emergencyAccess.GetPostureAsync(cancellationToken)).IsHealthy;

        if (!emergencyHealthy
            && IsPlatformAdministrator(user)
            && await CountOtherEntraIndependentPlatformAdministratorsAsync(userId, cancellationToken) == 0)
        {
            await AuditAsync(
                StrongAuthenticationAuditActionNames.LocalDisableDeferredLastAdmin,
                userId.Value,
                $"userId={userId.Value} reason=last_entra_independent_platform_administrator",
                cancellationToken);
            return EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator;
        }

        var hashBefore = credential.PasswordHash;
        await _credentials.SetDisabledAsync(userId, disabled: true, cancellationToken);

        var after = await _credentials.GetByUserIdAsync(userId, cancellationToken);
        if (after is not null && !string.Equals(after.PasswordHash, hashBefore, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Local credential material changed while disabling local authentication for {UserId}; this must never happen.",
                userId.Value);
        }

        await AuditAsync(
            StrongAuthenticationAuditActionNames.LocalDisabledEntraPrimary,
            userId.Value,
            emergencyHealthy
                ? $"userId={userId.Value} reason=entra_proven emergency_access=healthy"
                : $"userId={userId.Value} reason=entra_proven",
            cancellationToken);
        return EntraPrimaryLocalDisableOutcome.Disabled;
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

    private async ValueTask AuditAsync(string action, string userId, string details, CancellationToken cancellationToken)
    {
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId(userId),
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
