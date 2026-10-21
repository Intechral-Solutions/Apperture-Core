using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
/// <remarks>
/// <para>AUD-1.2: the policy acts on its own, as <c>system:entra-primary-policy</c>, after a user's Microsoft link is proven; the
/// user is the target. The checks and the disable commit with their row in one audited unit of work. Leaving local sign-in
/// enabled (Recovery Access, or an administrator whose Local sign-in the instance still needs) changes nothing, so those outcomes
/// are W3 attempt rows written after the transaction (D6).</para>
/// <para>IAM-10.2b: the credential is an input of administrator viability, so the decision is the shared one. Inside the unit,
/// the operation lock is taken first, then the global administrator-set gate, then the target and its credential are read; the
/// state is captured under the gate, the credential is disabled, and the state the change produced is read in the same
/// transaction and judged by <see cref="AdministratorViabilityGuard"/> (I-VIABLE and I-FALLBACK in both views, with the safe
/// post-state proof). A change that would violate either is rolled back and deferred. There is no second predicate here.</para>
/// <para>A busy gate is not a decision: nothing is read or written and the policy runs again at the next Entra sign-in.</para>
/// </remarks>
public sealed class EntraPrimaryAuthenticationPolicy : IEntraPrimaryAuthenticationPolicy
{
    /// <summary>The automatic policy, recorded as a named subsystem.</summary>
    internal static readonly AuditActor PolicyActor = AuditActor.System("entra-primary-policy");

    private readonly IUserRepository _users;
    private readonly ILocalCredentialRepository _credentials;
    private readonly ILogger<EntraPrimaryAuthenticationPolicy> _logger;
    private readonly IAdministratorViabilityCoordinator? _viability;
    private readonly IAdministratorViabilityService? _viabilityReads;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;

    public EntraPrimaryAuthenticationPolicy(
        IUserRepository users,
        ILocalCredentialRepository credentials,
        ILogger<EntraPrimaryAuthenticationPolicy> logger,
        IAdministratorViabilityCoordinator? viability = null,
        IAdministratorViabilityService? viabilityReads = null,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null)
    {
        _users = users;
        _credentials = credentials;
        _logger = logger;
        _viability = viability;
        _viabilityReads = viabilityReads;
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("The Entra-primary policy requires the audited unit of work.");

    private IAuditEventWriter Events =>
        _auditEvents ?? throw new InvalidOperationException("The Entra-primary policy requires the audit event writer.");

    private IAdministratorViabilityCoordinator Viability =>
        _viability ?? throw new InvalidOperationException("The Entra-primary policy requires the administrator-viability coordinator.");

    private IAdministratorViabilityService ViabilityReads =>
        _viabilityReads ?? throw new InvalidOperationException("The Entra-primary policy requires the administrator-viability reads.");

    /// <inheritdoc />
    public async ValueTask<EntraPrimaryLocalDisableOutcome> ApplyAfterEntraProvenAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        // Every successful Entra sign-in applies the policy, and in the steady state the credential is already disabled or there is
        // none. Those two answers change nothing, so they are given without the global gate and without a unit: whatever a
        // concurrent writer does to the credential after this read is met again at the next sign-in.
        if (await NothingToDisableAsync(userId, cancellationToken) is { } nothingToDo)
            return nothingToDo;

        AuditEvent? kept = null;
        try
        {
            var outcome = await Unit.ExecuteAsync(
                new AuditedOperation(AuditActionNames.LocalDisabledEntraPrimary, PolicyActor),
                async (transaction, ct) =>
                {
                    kept = null;

                    // The administrator-set gate first: the credential is a viability input, and what is decided about it is decided
                    // on what a concurrent writer committed. A rerun after a rollback is a new transaction and takes it again.
                    var viability = await Viability.EnterAsync(ct);

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

                    // Apply, then verify (P-9): the state before is read under the gate and before the write, the credential is
                    // disabled in this transaction, and the state the change produced is read back and decided.
                    await viability.CaptureBeforeAsync(ct);

                    var hashBefore = credential.PasswordHash;
                    await _credentials.SetDisabledAsync(userId, disabled: true, ct);

                    var after = await _credentials.GetByUserIdAsync(userId, ct);
                    if (after is not null && !string.Equals(after.PasswordHash, hashBefore, StringComparison.Ordinal))
                    {
                        _logger.LogError(
                            "Local credential material changed while disabling local authentication for {UserId}; this must never happen.",
                            userId.Value);
                    }

                    var verdict = await viability.VerifyAfterAsync(ct);
                    if (!verdict.IsAllowed)
                    {
                        // The unit rolls the disable back. A deferral of a proven violation is an attempt row; a configuration that
                        // could not be established proves nothing about the administrators, so it is not recorded as one.
                        kept = verdict.Kind == ViabilityVerdictKind.ConfigurationUnavailable ? null : AuditEvents.LocalDisableDeferredLastAdmin(userId);
                        transaction.DeclareNoOp();
                        return Deferred(verdict);
                    }

                    var recoveryHealthy = viability.Captured?.RecoveryHealthy ?? false;
                    await transaction.StageAsync(AuditEvents.LocalDisabledEntraPrimary(userId, recoveryHealthy), ct);
                    return EntraPrimaryLocalDisableOutcome.Disabled;
                },
                (_, _) => Task.FromResult(EntraPrimaryLocalDisableOutcome.Disabled),
                cancellationToken);

            if (kept is not null)
                await Events.TryWriteAsync(kept, PolicyActor, CancellationToken.None);
            return outcome;
        }
        catch (PlatformAdministratorSetBusyException)
        {
            // Contention, not a decision: the gate could not be taken in time, so nothing was read or written. It is the only
            // exception this method turns into an outcome; infrastructure failures, cancellation and unknown outcomes propagate.
            return EntraPrimaryLocalDisableOutcome.NotAppliedBusy;
        }
    }

    private static EntraPrimaryLocalDisableOutcome Deferred(ViabilityVerdict verdict) => verdict.Kind switch
    {
        ViabilityVerdictKind.LastViableAdministrator => EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator,
        ViabilityVerdictKind.EntraIndependentFallbackRequired => EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator,
        _ => EntraPrimaryLocalDisableOutcome.DeferredAdministratorViabilityUnavailable,
    };

    /// <summary>
    /// The outcome when there is no credential to disable or it is disabled already, read without any lock. It only ever answers
    /// "nothing to do", never "go ahead": the decision to disable is made inside the unit, under the gate.
    /// </summary>
    private async ValueTask<EntraPrimaryLocalDisableOutcome?> NothingToDisableAsync(UserId userId, CancellationToken cancellationToken)
    {
        var credential = await _credentials.GetByUserIdAsync(userId, cancellationToken);
        if (credential is null)
            return EntraPrimaryLocalDisableOutcome.NoLocalCredential;
        return credential.IsDisabled ? EntraPrimaryLocalDisableOutcome.AlreadyDisabled : null;
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

            var safeguard = IsPlatformAdministrator(user)
                && entra == AuthenticationMethodStatuses.EntraProven
                && await WouldDeferAsync(user.Id, cancellationToken);
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

    /// <summary>
    /// Whether the policy would keep this administrator's Local sign-in enabled now: the decision it would take, over the state as
    /// it is read now (a display takes no gate), with the post-state derived by the same definitions. A state that cannot be
    /// established, or whose facts are not available, is shown as kept, because the policy would not disable it either.
    /// </summary>
    private async ValueTask<bool> WouldDeferAsync(UserId userId, CancellationToken cancellationToken)
    {
        var state = await ViabilityReads.ReadAsync(cancellationToken);
        var after = AdministratorViabilitySimulation.AfterLocalCredentialDisabled(state, userId.Value);
        return after is null || !AdministratorViabilityGuard.Evaluate(state, after).IsAllowed;
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
}
