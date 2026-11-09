using Apperture.ControlPlane.Security.AdministratorViability;
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
/// <para>AUD-1.2 (audit hardening design gate §13): every Recovery Access change runs as one audited unit of work, so the change
/// and its row commit together. The actor is who ran it: the emergency command line, or the platform when the host applies
/// the canonical identity at startup. Rows written before AUD-1.2 recorded the affected account or always the command line.</para>
/// <para>IAM-10.2b: a designation and a strict revocation decide who counts as an ordinary administrator and whether Recovery Access can
/// stand in for one, so each is a reducer of administrator viability. Each takes the global administrator-set gate first in its unit,
/// reads the state before under it, applies its writes, and is decided last, on the state its own writes left, under both invariants:
/// a refusal rolls everything back and is recorded after the rollback with the class of configuration evidence the decision used.
/// Provisioning adds a new account that holds nothing before it, so it is a gated raiser: it takes the same gate and asserts that it
/// reduced nothing. Each operation names the affected account by the id of the row it loaded, never by the caller's spelling of it.</para>
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
    private readonly IAdministratorViabilityCoordinator? _viability;
    private readonly IAuditEventWriter? _auditEvents;

    public EmergencyAccessService(
        IUserRepository users,
        IRoleRepository roles,
        ILocalCredentialRepository credentials,
        ILocalPasswordHasher hasher,
        INativeMfaService mfa,
        TimeProvider time,
        ILogger<EmergencyAccessService> logger,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAdministratorViabilityCoordinator? viability = null,
        IAuditEventWriter? auditEvents = null)
    {
        _auditedUnitOfWork = auditedUnitOfWork;
        _users = users;
        _roles = roles;
        _credentials = credentials;
        _hasher = hasher;
        _mfa = mfa;
        _time = time;
        _logger = logger;
        _viability = viability;
        _auditEvents = auditEvents;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Recovery Access administration requires the audited unit of work.");

    /// <summary>
    /// The global administrator-set gate and the viability reads. Absent means the service was built without them, which is a wiring
    /// defect: a change to the designation is never made without them, so it fails here.
    /// </summary>
    private IAdministratorViabilityCoordinator Viability =>
        _viability ?? throw new InvalidOperationException("Recovery Access administration requires the administrator viability coordinator.");

    /// <summary>Writes the refusal row after the rollback. Absent is a wiring defect, as for <see cref="Viability"/>.</summary>
    private IAuditEventWriter Events =>
        _auditEvents ?? throw new InvalidOperationException("Recovery Access administration requires the audit event writer.");

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
        // The demotion of the previous account, the designation, the canonical identity and their rows commit together, and only when
        // the designation leaves administrator viability intact: the account designated stops counting as an ordinary administrator.
        await Unit.ExecuteRefusableAsync(
            Events,
            new AuditedOperation(AuditActionNames.RecoveryAccessDesignated, AuditActorFor(actorId)),
            async (transaction, refusals, ct) =>
            {
                // The administrator-set gate first, before any row: every other change to administrator access waits for this one.
                var viability = await Viability.EnterAsync(ct);

                var user = await _users.GetByIdAsync(userId, ct)
                    ?? throw new EmergencyAccessException(EmergencyAccessErrorCodes.UserNotFound, "User not found.");

                // From here on the account is named by the id of the row just loaded, never by the caller's spelling of it: the database
                // finds a user without regard to case, and the decision compares ids ordinally.
                var target = user.Id;
                EnsureEligibleForDesignation(user, await _credentials.GetByUserIdAsync(target, ct));
                await EnsureCanTakeCanonicalUsernameAsync(user, ct);

                var previous = await _users.FindDesignatedEmergencyUserAsync(ct);
                var replaced = previous is not null && !string.Equals(previous.Id.Value, target.Value, StringComparison.Ordinal)
                    ? previous
                    : null;
                var wasDesignated = user.IsBreakGlassEligible;

                // Re-designating the designated, canonical account changes nothing and records nothing; it is decided before the invariants.
                if (replaced is null && wasDesignated && EmergencyAccessIdentity.HasCanonicalIdentity(user))
                {
                    transaction.DeclareNoOp();
                    return true;
                }

                // The state before, under the gate; then every write of the designation.
                await viability.CaptureBeforeAsync(ct);
                if (replaced is not null)
                    await SetDesignationAsync(replaced, designated: false, ct);
                if (!wasDesignated)
                    await SetDesignationAsync(user, designated: true, ct);
                var canonicalized = await RenameToCanonicalAsync(user with { IsBreakGlassEligible = true }, ct);
                await RequireDesignationTransitionAsync(target, ct);

                // Last, after every write: the decision is made on the state the demotion, the designation and the rename left.
                var decision = await viability.VerifyDesignationAsync(ct);
                if (!decision.IsAllowed)
                    return refusals.Refuse<bool>(transaction, ViabilityRefusalAudit.Blocked(target, decision), decision.Refusal());

                if (canonicalized is not null)
                    await transaction.StageAsync(canonicalized, ct);
                await transaction.StageAsync(AuditEvents.RecoveryAccessDesignated(target, replaced?.Id, decision.Evidence), ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);
    }

    /// <summary>
    /// The designation must be the transition it claims: after its writes, the account it loaded is the one designated account. The
    /// conditional setter already refuses a stored value it did not expect; this reads the result back in the same transaction, so a
    /// designation that changed some other account, or none, is an invariant failure and nothing it did is kept.
    /// </summary>
    private async ValueTask RequireDesignationTransitionAsync(UserId target, CancellationToken cancellationToken)
    {
        var designated = await _users.FindDesignatedEmergencyUserAsync(cancellationToken);
        if (designated is null || !string.Equals(designated.Id.Value, target.Value, StringComparison.Ordinal))
            throw new AuditInvariantException("The Recovery Access designation did not change to the account it was asked for; it was not applied.");
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
                // The administrator-set gate first: the account is a Platform Administrator and becomes the Recovery Access account, so
                // provisioning is serialised with every other change to administrator access. It can only add: the account is new and
                // holds nothing before this unit, and no designation is replaced (one that exists under the gate refuses it below).
                var viability = await Viability.EnterAsync(ct);

                // Decided again under the gate: the check before the unit is advice. A designation that committed since would otherwise
                // meet the filtered unique index as a database failure instead of this refusal.
                if (await _users.FindDesignatedEmergencyUserAsync(ct) is not null)
                {
                    throw new EmergencyAccessException(
                        EmergencyAccessErrorCodes.AlreadyDesignated,
                        "A Recovery Access account was designated while this request ran, so no account was created. Check the Recovery Access status.");
                }

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

                await viability.CaptureBeforeForRaiseAsync(userId.Value, ct);
                try
                {
                    await _users.AddAsync(user, ct);
                }
                catch (UsernameException ex) when (ex.Code == UsernameErrorCodes.Taken)
                {
                    throw ProvisionUsernameConflict();
                }

                await _credentials.SetPasswordAsync(userId, hash, salt, _hasher.AlgorithmName, ct);
                await viability.AssertRaiseOnlyAsync(ct);
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
                var renamed = designated is null ? null : await RenameToCanonicalAsync(designated, ct);
                if (renamed is null)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                await transaction.StageAsync(renamed, ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);
    }

    /// <summary>
    /// Gives the designated account the canonical Recovery Access username and display name inside the open transaction, and returns
    /// the row that records it for the caller to stage (a designation stages it only once the designation has been decided). Null when
    /// the account already has them: nothing was written.
    /// </summary>
    private async ValueTask<Apperture.Core.Application.Audit.AuditEvent?> RenameToCanonicalAsync(User designated, CancellationToken cancellationToken)
    {
        if (EmergencyAccessIdentity.HasCanonicalIdentity(designated))
            return null;

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

        return AuditEvents.RecoveryAccessCanonicalized(designated.Id, previousUsername, UsernamePolicy.RecoveryAccessUsername);
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

        // Strict revocation: whatever the state before, it stands only when another ordinary administrator who can sign in without
        // Microsoft remains in each view, and the confirmation above does not replace that.
        await Unit.ExecuteRefusableAsync(
            Events,
            new AuditedOperation(AuditActionNames.RecoveryAccessRevoked, AuditActorFor(actorId)),
            async (transaction, refusals, ct) =>
            {
                // The administrator-set gate first, before any row.
                var viability = await Viability.EnterAsync(ct);

                var designated = await _users.FindDesignatedEmergencyUserAsync(ct)
                    ?? throw new EmergencyAccessException(
                        EmergencyAccessErrorCodes.NotDesignated,
                        "No emergency administrator is designated.");

                // The account is named by the id of the row just loaded, and only that account's designation is changed.
                var revoked = designated.Id;
                await viability.CaptureBeforeAsync(ct);
                await SetDesignationAsync(designated, designated: false, ct);
                await RequireRevocationTransitionAsync(revoked, ct);

                // Last, after the write: the strict rule on the state the revocation left.
                var decision = await viability.VerifyRevocationAsync(revoked.Value, ct);
                RequireDecidedOnTheRevocation(viability, revoked);
                if (!decision.IsAllowed)
                    return refusals.Refuse<bool>(transaction, ViabilityRefusalAudit.Blocked(revoked, decision), decision.Refusal());

                await transaction.StageAsync(AuditEvents.RecoveryAccessRevoked(revoked, decision.Evidence), ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);
    }

    /// <summary>
    /// The revocation must be the transition it claims, read back in its own transaction: the account it read as designated is no longer
    /// designated, and no account is. The strict rule does not look at the state before and would not notice a revocation that changed
    /// nothing or changed another account, so the operation checks it itself; anything else is an invariant failure and nothing is kept.
    /// </summary>
    private async ValueTask RequireRevocationTransitionAsync(UserId revoked, CancellationToken cancellationToken)
    {
        var stored = await _users.GetByIdAsync(revoked, cancellationToken);
        var stillDesignated = await _users.FindDesignatedEmergencyUserAsync(cancellationToken);
        if (stored is null || stored.IsBreakGlassEligible || stillDesignated is not null)
            throw new AuditInvariantException("The Recovery Access designation was not removed from the account it was read on; the revocation was not applied.");
    }

    /// <summary>
    /// The decision must be about this revocation: the state it captured had the revoked account designated, and the state it judged has
    /// nobody designated. A state read from the database always carries these facts; a decision over states of another change is not a
    /// decision about this one, so it is an invariant failure, never a refusal or a success.
    /// </summary>
    private static void RequireDecidedOnTheRevocation(AdministratorViabilityScope viability, UserId revoked)
    {
        if (viability.Captured?.Recovery is not { } before || viability.After?.Recovery is not { } after)
            return;
        if (!string.Equals(before.DesignatedUserId, revoked.Value, StringComparison.Ordinal) || after.DesignatedUserId is not null)
            throw new AuditInvariantException("The revocation was judged on a state that does not show the designation it removed; it was not applied.");
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

                // The credential row is keyed by the id of the row just loaded: a row stored under the caller's spelling of the id would
                // be found by the database (which ignores case) but not by the viability reads, which compare ids ordinally.
                await _credentials.SetPasswordAsync(user.Id, hash, salt, _hasher.AlgorithmName, ct);
                await transaction.StageAsync(AuditEvents.PasswordSet(user.Id), ct);
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

    /// <summary>
    /// The only writer of the designation: a conditional set that changes the row only while it still holds the value this unit
    /// read. A different stored value means another change to the designation committed in between, which this unit's decisions
    /// were not made on, so the unit fails as an invariant failure and nothing it did is kept.
    /// </summary>
    private async ValueTask SetDesignationAsync(User user, bool designated, CancellationToken cancellationToken)
    {
        if (!await _users.TrySetDesignationAsync(user.Id, expected: user.IsBreakGlassEligible, designated, cancellationToken))
        {
            throw new AuditInvariantException(
                "The Recovery Access designation changed during the operation; it was not applied.");
        }
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
