using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
public sealed class EmergencyAccessService : IEmergencyAccessService
{
    public const string CliActorId = "emergency-cli";
    private const string AuditCategory = "auth";

    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly ILocalCredentialRepository _credentials;
    private readonly ILocalPasswordHasher _hasher;
    private readonly INativeMfaService _mfa;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<EmergencyAccessService> _logger;

    public EmergencyAccessService(
        IUserRepository users,
        IRoleRepository roles,
        ILocalCredentialRepository credentials,
        ILocalPasswordHasher hasher,
        INativeMfaService mfa,
        IAuditService audit,
        TimeProvider time,
        ILogger<EmergencyAccessService> logger)
    {
        _users = users;
        _roles = roles;
        _credentials = credentials;
        _hasher = hasher;
        _mfa = mfa;
        _audit = audit;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<EmergencyAccessPosture> GetPostureAsync(CancellationToken cancellationToken = default)
    {
        var designated = await _users.FindDesignatedEmergencyUserAsync(cancellationToken);
        if (designated is null)
        {
            return new EmergencyAccessPosture(
                IsHealthy: false,
                FallbackSafeguardRequired: true,
                DesignatedUserId: null,
                DesignatedDisplayName: null,
                DesignatedIsPlatformAdministrator: false,
                LocalCredentialEnabled: false,
                NativeMfaEnrolled: false,
                UnusedRecoveryCodeCount: 0);
        }

        var isAdmin = IsPlatformAdministrator(designated);
        var hasLocalLink = designated.IdentityLinks.Any(l => l.Source == IdentitySource.Local);
        var credential = await _credentials.GetByUserIdAsync(designated.Id, cancellationToken);
        var localEnabled = hasLocalLink && credential is { IsDisabled: false };
        var mfaEnrolled = await _mfa.IsEnrolledAsync(designated.Id, cancellationToken);
        var unusedCodes = mfaEnrolled
            ? await _mfa.CountUnusedRecoveryCodesAsync(designated.Id, cancellationToken)
            : 0;
        var healthy = isAdmin && localEnabled && mfaEnrolled && unusedCodes > 0;
        var localUsername = EmergencyAccessIdentity.LocalUsernameOf(designated);
        return new EmergencyAccessPosture(
            healthy,
            FallbackSafeguardRequired: !healthy,
            designated.Id.Value,
            designated.DisplayName,
            isAdmin,
            localEnabled,
            mfaEnrolled,
            unusedCodes,
            localUsername,
            CanonicalIdentityApplied: EmergencyAccessIdentity.HasCanonicalIdentity(designated),
            CanonicalUsernameConflict: await HasCanonicalUsernameConflictAsync(designated, cancellationToken),
            HasEntraIdentityLink: EmergencyAccessIdentity.HasEntraIdentityLink(designated));
    }

    /// <inheritdoc />
    public async ValueTask DesignateAsync(UserId userId, ActorId? actorId, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new EmergencyAccessException(EmergencyAccessErrorCodes.UserNotFound, "User not found.");

        EnsureEligibleForDesignation(user, await _credentials.GetByUserIdAsync(userId, cancellationToken));
        await EnsureCanTakeCanonicalUsernameAsync(user, cancellationToken);

        var previous = await _users.FindDesignatedEmergencyUserAsync(cancellationToken);
        if (previous is not null && !string.Equals(previous.Id.Value, user.Id.Value, StringComparison.Ordinal))
            await SetDesignationAsync(previous, designated: false, cancellationToken);

        if (!user.IsBreakGlassEligible)
            await SetDesignationAsync(user, designated: true, cancellationToken);

        await EnsureCanonicalIdentityAsync(cancellationToken);

        await AuditAsync(
            EmergencyAccessAuditActionNames.Designated,
            actorId?.Value ?? userId.Value,
            $"userId={userId.Value}",
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<UserId> ProvisionAsync(
        string password,
        ActorId? actorId,
        CancellationToken cancellationToken = default)
    {
        var existingDesignated = await _users.FindDesignatedEmergencyUserAsync(cancellationToken);
        if (existingDesignated is not null)
        {
            await EnsureCanonicalIdentityAsync(cancellationToken);
            return existingDesignated.Id;
        }

        ValidatePassword(password);

        var role = await _roles.GetAsync(new RoleId(BootstrapConstants.PlatformAdminRoleId), cancellationToken)
            ?? throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.RoleNotFound,
                "The Platform Administrator role is not available.");

        var normalizedUsername = EmergencyAccessIdentity.NormalizedLocalUsername;
        var existing = await _users.GetByIdentityLinkAsync(IdentitySource.Local, normalizedUsername, cancellationToken);
        var ledgerOwner = await _users.FindUsernameLedgerEntryAsync(normalizedUsername, cancellationToken);
        if (existing is not null || ledgerOwner is not null)
            throw ProvisionUsernameConflict();

        var userId = new UserId(Guid.NewGuid().ToString("N")[..24]);
        var user = new User(
            userId,
            EmergencyAccessIdentity.DisplayName,
            [new IdentityLink(IdentitySource.Local, normalizedUsername, LastAuthenticatedAt: null)],
            [role.Id],
            IsBreakGlassEligible: true)
        {
            Username = normalizedUsername
        };

        try
        {
            await _users.AddAsync(user, cancellationToken);
        }
        catch (UsernameException ex) when (ex.Code == UsernameErrorCodes.Taken)
        {
            throw ProvisionUsernameConflict();
        }
        var (hash, salt) = _hasher.HashPassword(password);
        await _credentials.SetPasswordAsync(userId, hash, salt, _hasher.AlgorithmName, cancellationToken);

        await AuditAsync(
            EmergencyAccessAuditActionNames.Provisioned,
            actorId?.Value ?? CliActorId,
            $"userId={userId.Value}",
            cancellationToken);
        return userId;
    }

    /// <inheritdoc />
    public async ValueTask EnsureCanonicalIdentityAsync(CancellationToken cancellationToken = default)
    {
        var designated = await _users.FindDesignatedEmergencyUserAsync(cancellationToken);
        if (designated is null)
            return;
        if (EmergencyAccessIdentity.HasCanonicalIdentity(designated))
            return;

        await EnsureCanTakeCanonicalUsernameAsync(designated, cancellationToken);

        if (EmergencyAccessIdentity.LocalUsernameOf(designated) is null)
        {
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.LocalCredentialRequired,
                "The emergency account must have an enabled local username and password.");
        }

        // A system rename through the shared username foundation: the previous name is retired but stays
        // bound to this user, and the Local link moves with the username in the same transaction.
        var previousUsername = designated.Username;
        try
        {
            await new UsernameService(_users).ApplyRecoveryAccessIdentityAsync(designated, cancellationToken);
        }
        catch (UsernameException ex) when (ex.Code == UsernameErrorCodes.Taken)
        {
            throw CanonicalUsernameConflict();
        }

        await AuditAsync(
            EmergencyAccessAuditActionNames.Canonicalized,
            CliActorId,
            $"userId={designated.Id.Value} previousUsername={previousUsername}",
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask RevokeAsync(bool confirmFallback, ActorId? actorId, CancellationToken cancellationToken = default)
    {
        if (!confirmFallback)
        {
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.ConfirmFallbackRequired,
                "Revoking emergency access returns the installation to last-administrator fallback protection. Pass --confirm-fallback to continue.");
        }

        var designated = await _users.FindDesignatedEmergencyUserAsync(cancellationToken)
            ?? throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.NotDesignated,
                "No emergency administrator is designated.");

        await SetDesignationAsync(designated, designated: false, cancellationToken);
        await AuditAsync(
            EmergencyAccessAuditActionNames.Revoked,
            actorId?.Value ?? CliActorId,
            $"userId={designated.Id.Value} fallback=true",
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsDesignatedEmergencyUserAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var designated = await _users.FindDesignatedEmergencyUserAsync(cancellationToken);
        return designated is not null && string.Equals(designated.Id.Value, userId.Value, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async ValueTask EnsureDestructiveChangeAllowedAsync(
        UserId userId,
        string operation,
        CancellationToken cancellationToken = default)
    {
        if (!await IsDesignatedEmergencyUserAsync(userId, cancellationToken))
            return;

        await AuditAsync(
            EmergencyAccessAuditActionNames.BlockedUnsafe,
            userId.Value,
            $"userId={userId.Value} operation={operation}",
            cancellationToken);
        throw new EmergencyAccessException(
            EmergencyAccessErrorCodes.UnsafeOperation,
            "This change would remove the designated emergency administrator recovery path. Use the emergency CLI with --confirm-fallback if you intend to return to last-administrator fallback protection.");
    }

    public async ValueTask SetLocalPasswordAsync(UserId userId, string password, ActorId? actorId, CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new EmergencyAccessException(EmergencyAccessErrorCodes.UserNotFound, "User not found.");
        if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
        {
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.LocalCredentialRequired,
                "The user has no local identity.");
        }

        var (hash, salt) = _hasher.HashPassword(password);
        await _credentials.SetPasswordAsync(userId, hash, salt, _hasher.AlgorithmName, cancellationToken);
        await AuditAsync("credential.password_set", actorId?.Value ?? CliActorId, $"userId={userId.Value} emergency=true", cancellationToken);
    }

    private void ValidatePassword(string password)
    {
        LocalPasswordPolicy.RequireRecoveryAccess(
            password,
            LocalPasswordPolicy.ForLocalIdentity(
                EmergencyAccessIdentity.LocalUsername,
                EmergencyAccessIdentity.DisplayName));
    }

    private static void EnsureEligibleForDesignation(User user, LocalCredentialData? credential)
    {
        if (!IsPlatformAdministrator(user))
        {
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.NotPlatformAdministrator,
                "The emergency account must be a Platform Administrator.");
        }

        if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local) || credential is null || credential.IsDisabled)
        {
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.LocalCredentialRequired,
                "The emergency account must have an enabled local username and password.");
        }
    }

    private async ValueTask SetDesignationAsync(User user, bool designated, CancellationToken cancellationToken)
    {
        var updated = user with { IsBreakGlassEligible = designated };
        await _users.UpdateAsync(updated, cancellationToken);
    }

    private async ValueTask EnsureCanTakeCanonicalUsernameAsync(User user, CancellationToken cancellationToken)
    {
        if (await HasCanonicalUsernameConflictAsync(user, cancellationToken))
            throw CanonicalUsernameConflict();
    }

    /// <summary>
    /// True when another user holds the Recovery Access username as a Local sign-in name, or owns it in
    /// the username ledger (current or retired). A ledger name is never reassigned to a different user.
    /// </summary>
    private async ValueTask<bool> HasCanonicalUsernameConflictAsync(User user, CancellationToken cancellationToken)
    {
        var owner = await _users.GetByIdentityLinkAsync(
            IdentitySource.Local,
            EmergencyAccessIdentity.NormalizedLocalUsername,
            cancellationToken);
        if (owner is not null && !string.Equals(owner.Id.Value, user.Id.Value, StringComparison.Ordinal))
            return true;

        var ledger = await _users.FindUsernameLedgerEntryAsync(EmergencyAccessIdentity.NormalizedLocalUsername, cancellationToken);
        return ledger is not null && !string.Equals(ledger.UserId.Value, user.Id.Value, StringComparison.Ordinal);
    }

    private static EmergencyAccessException CanonicalUsernameConflict() =>
        new(
            EmergencyAccessErrorCodes.UsernameConflict,
            "The reserved username is already assigned to another user. The emergency account was not changed. Resolve the conflict before applying the canonical Recovery Access identity.");

    private static EmergencyAccessException ProvisionUsernameConflict() =>
        new(
            EmergencyAccessErrorCodes.UsernameConflict,
            "The reserved username is already assigned to a user that is not the emergency account. Resolve the conflict before provisioning Recovery Access.");

    private static bool IsPlatformAdministrator(User user) =>
        user.AssignedRoleIds.Any(r =>
            string.Equals(r.Value, BootstrapConstants.PlatformAdminRoleId, StringComparison.OrdinalIgnoreCase));

    private async ValueTask AuditAsync(string action, string actorId, string details, CancellationToken cancellationToken)
    {
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId(actorId),
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
