using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
/// <remarks>
/// AUD-1.2 (audit hardening design gate §13): every Recovery Access change runs as one audited unit of work, so the change
/// and its row commit together. The actor is who ran it: the emergency command line, or the platform when the host applies
/// the canonical identity at startup. Rows written before AUD-1.2 recorded the affected account or always the command line.
/// </remarks>
public sealed class EmergencyAccessService : IEmergencyAccessService
{
    public const string CliActorId = ActorLiterals.EmergencyCli;

    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly ILocalCredentialRepository _credentials;
    private readonly ILocalPasswordHasher _hasher;
    private readonly INativeMfaService _mfa;
    private readonly TimeProvider _time;
    private readonly ILogger<EmergencyAccessService> _logger;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public EmergencyAccessService(
        IUserRepository users,
        IRoleRepository roles,
        ILocalCredentialRepository credentials,
        ILocalPasswordHasher hasher,
        INativeMfaService mfa,
        TimeProvider time,
        ILogger<EmergencyAccessService> logger,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _auditedUnitOfWork = auditedUnitOfWork;
        _users = users;
        _roles = roles;
        _credentials = credentials;
        _hasher = hasher;
        _mfa = mfa;
        _time = time;
        _logger = logger;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Recovery Access administration requires the audited unit of work.");

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
        // The demotion of the previous account, the designation, the canonical identity and their rows commit together.
        await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.RecoveryAccessDesignated, AuditActorFor(actorId)),
            async (transaction, ct) =>
            {
                var user = await _users.GetByIdAsync(userId, ct)
                    ?? throw new EmergencyAccessException(EmergencyAccessErrorCodes.UserNotFound, "User not found.");

                EnsureEligibleForDesignation(user, await _credentials.GetByUserIdAsync(userId, ct));
                await EnsureCanTakeCanonicalUsernameAsync(user, ct);

                var previous = await _users.FindDesignatedEmergencyUserAsync(ct);
                var replaced = previous is not null && !string.Equals(previous.Id.Value, user.Id.Value, StringComparison.Ordinal)
                    ? previous
                    : null;
                if (replaced is not null)
                    await SetDesignationAsync(replaced, designated: false, ct);

                var wasDesignated = user.IsBreakGlassEligible;
                if (!wasDesignated)
                    await SetDesignationAsync(user, designated: true, ct);

                var canonicalized = await CanonicalizeAsync(transaction, user with { IsBreakGlassEligible = true }, ct);

                // Re-designating the designated, canonical account changes nothing and records nothing.
                if (replaced is null && wasDesignated && !canonicalized)
                {
                    transaction.DeclareNoOp();
                    return true;
                }

                await transaction.StageAsync(AuditEvents.RecoveryAccessDesignated(userId, replaced?.Id), ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
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
            await EnsureCanonicalIdentityAsync(AuditActorFor(actorId), cancellationToken);
            return existingDesignated.Id;
        }

        ValidatePassword(password);

        // Built once, so a rerun after a confirmed rollback creates the same account with the same credential. The salted
        // hash never leaves this method.
        var normalizedUsername = EmergencyAccessIdentity.NormalizedLocalUsername;
        var userId = new UserId(Guid.NewGuid().ToString("N")[..24]);
        var (hash, salt) = _hasher.HashPassword(password);

        // The account, its credential and the row commit together: no Recovery Access account without its password.
        return await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.RecoveryAccessProvisioned, AuditActorFor(actorId)),
            async (transaction, ct) =>
            {
                var role = await _roles.GetAsync(new RoleId(BootstrapConstants.PlatformAdminRoleId), ct)
                    ?? throw new EmergencyAccessException(
                        EmergencyAccessErrorCodes.RoleNotFound,
                        "The Platform Administrator role is not available.");

                var existing = await _users.GetByIdentityLinkAsync(IdentitySource.Local, normalizedUsername, ct);
                var ledgerOwner = await _users.FindUsernameLedgerEntryAsync(normalizedUsername, ct);
                if (existing is not null || ledgerOwner is not null)
                    throw ProvisionUsernameConflict();

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
                    await _users.AddAsync(user, ct);
                }
                catch (UsernameException ex) when (ex.Code == UsernameErrorCodes.Taken)
                {
                    throw ProvisionUsernameConflict();
                }

                await _credentials.SetPasswordAsync(userId, hash, salt, _hasher.AlgorithmName, ct);
                await transaction.StageAsync(AuditEvents.RecoveryAccessProvisioned(userId, normalizedUsername), ct);
                return userId;
            },
            (_, _) => Task.FromResult(userId),
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask EnsureCanonicalIdentityAsync(AuditActor actor, CancellationToken cancellationToken = default)
    {
        // AUD-1.1: one audited unit of work (audit hardening design gate §13). The checks, the rename and the audit row
        // commit together, and the row records who actually ran it: the emergency command line, or the system when the
        // Management Host applies it at startup (previously always recorded as the command line).
        await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.RecoveryAccessCanonicalized, actor),
            async (transaction, ct) =>
            {
                var designated = await _users.FindDesignatedEmergencyUserAsync(ct);
                if (designated is null || !await CanonicalizeAsync(transaction, designated, ct))
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);
    }

    /// <summary>
    /// Gives the designated account the canonical Recovery Access username and display name inside the open transaction,
    /// and stages its row. False when it already has them (nothing staged).
    /// </summary>
    private async ValueTask<bool> CanonicalizeAsync(IAuditedTransaction transaction, User designated, CancellationToken cancellationToken)
    {
        if (EmergencyAccessIdentity.HasCanonicalIdentity(designated))
            return false;

        await EnsureCanTakeCanonicalUsernameAsync(designated, cancellationToken);

        if (EmergencyAccessIdentity.LocalUsernameOf(designated) is null)
        {
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.LocalCredentialRequired,
                "The emergency account must have an enabled local username and password.");
        }

        // A system rename through the shared username foundation: the previous name is retired but stays bound to this
        // user, and the Local link moves with the username in the same transaction.
        var previousUsername = designated.Username;
        try
        {
            await new UsernameService(_users).ApplyRecoveryAccessIdentityAsync(designated, cancellationToken);
        }
        catch (UsernameException ex) when (ex.Code == UsernameErrorCodes.Taken)
        {
            throw CanonicalUsernameConflict();
        }

        await transaction.StageAsync(
            AuditEvents.RecoveryAccessCanonicalized(designated.Id, previousUsername, UsernamePolicy.RecoveryAccessUsername),
            cancellationToken);
        return true;
    }

    /// <summary>The audit actor for an emergency operation passed an optional actor id (the command line passes its own).</summary>
    private static AuditActor AuditActorFor(ActorId? actorId) =>
        actorId is null || actorId.Value == CliActorId ? AuditActor.EmergencyCli : AuditActor.CurrentPrincipal;

    /// <inheritdoc />
    public async ValueTask RevokeAsync(bool confirmFallback, ActorId? actorId, CancellationToken cancellationToken = default)
    {
        if (!confirmFallback)
        {
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.ConfirmFallbackRequired,
                "Revoking emergency access returns the installation to last-administrator fallback protection. Pass --confirm-fallback to continue.");
        }

        await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.RecoveryAccessRevoked, AuditActorFor(actorId)),
            async (transaction, ct) =>
            {
                var designated = await _users.FindDesignatedEmergencyUserAsync(ct)
                    ?? throw new EmergencyAccessException(
                        EmergencyAccessErrorCodes.NotDesignated,
                        "No emergency administrator is designated.");

                await SetDesignationAsync(designated, designated: false, ct);
                await transaction.StageAsync(AuditEvents.RecoveryAccessRevoked(designated.Id), ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsDesignatedEmergencyUserAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var designated = await _users.FindDesignatedEmergencyUserAsync(cancellationToken);
        return designated is not null && string.Equals(designated.Id.Value, userId.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal raised when a change would remove the designated Recovery Access recovery path. The caller decides it
    /// inside its own transaction and records <c>emergency.auth.blocked.unsafe_operation</c> after the rollback.
    /// </summary>
    internal static EmergencyAccessException UnsafeOperationRefusal() =>
        new(
            EmergencyAccessErrorCodes.UnsafeOperation,
            "This change would remove the designated emergency administrator recovery path. Use the emergency CLI with --confirm-fallback if you intend to return to last-administrator fallback protection.");

    public async ValueTask SetLocalPasswordAsync(UserId userId, string password, ActorId? actorId, CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);

        // The salted hash is computed once and reused by a rerun; it never leaves this method.
        var (hash, salt) = _hasher.HashPassword(password);
        await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.PasswordSet, AuditActorFor(actorId)),
            async (transaction, ct) =>
            {
                var user = await _users.GetByIdAsync(userId, ct)
                    ?? throw new EmergencyAccessException(EmergencyAccessErrorCodes.UserNotFound, "User not found.");
                if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
                {
                    throw new EmergencyAccessException(
                        EmergencyAccessErrorCodes.LocalCredentialRequired,
                        "The user has no local identity.");
                }

                await _credentials.SetPasswordAsync(userId, hash, salt, _hasher.AlgorithmName, ct);
                await transaction.StageAsync(AuditEvents.PasswordSet(userId), ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);
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
}
