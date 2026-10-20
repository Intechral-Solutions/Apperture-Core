using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>
/// What a writer of administrator viability calls inside its audited unit (IAM-10.2b). It takes the global gate and reads the
/// state before and after the writer's own mutation; it never decides what a refusal looks like, which belongs to the operation.
/// </summary>
public interface IAdministratorViabilityCoordinator
{
    /// <summary>
    /// Takes <see cref="IPlatformAdministratorSetGate"/> for the caller's open transaction. It must be the first lock the unit takes
    /// after its operation lock: before the authority row locks of <c>IRoleRepository.LockAuthoritySnapshotAsync</c> and
    /// before any other row. The gate is held until the unit's transaction ends, so a rerun after a rollback calls this again.
    /// </summary>
    /// <exception cref="PlatformAdministratorSetBusyException">Another change to administrator access holds the gate past the bound.</exception>
    /// <exception cref="PlatformAdministratorSetGateException">The lock call failed for a reason that is not contention.</exception>
    ValueTask<AdministratorViabilityScope> EnterAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// One writer's hold on the gate and the state it reads under it. A scope belongs to one attempt of one unit: it is created by
/// <see cref="IAdministratorViabilityCoordinator.EnterAsync"/> after the gate is held, and every read it issues is therefore issued
/// under the gate. The order of use is fixed: <see cref="CaptureBeforeAsync"/>, the mutation, then <see cref="VerifyAfterAsync"/>
/// (a reducer) or <see cref="AssertRaiseOnlyAsync"/> (a raiser).
/// </summary>
public sealed class AdministratorViabilityScope
{
    private readonly IAdministratorViabilityService _reads;
    private AdministratorViabilityState? _before;

    internal AdministratorViabilityScope(IAdministratorViabilityService reads) => _reads = reads;

    /// <summary>Reads the state before the mutation. Call it after the gate is held and before the mutation is applied.</summary>
    public async ValueTask CaptureBeforeAsync(CancellationToken cancellationToken = default) =>
        _before = await _reads.ReadAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Reads the state the applied mutation left, in the same transaction, and decides under both invariants and both views. A
    /// state that already violated an invariant is never a reason to refuse. Fails closed when the deployment configuration cannot
    /// be relied on (<see cref="ViabilityVerdictKind.ConfigurationUnavailable"/>). The caller refuses through its unit, which rolls
    /// the applied mutation back.
    /// </summary>
    public async ValueTask<ViabilityVerdict> VerifyAfterAsync(CancellationToken cancellationToken = default)
    {
        var before = Before();
        var after = await _reads.ReadAsync(cancellationToken).ConfigureAwait(false);
        return AdministratorViabilityGuard.Evaluate(before, after);
    }

    /// <summary>
    /// For a mutation that can only add (assign the role, enable a credential, add a link): reads the state after it and asserts that
    /// nothing was reduced. It never refuses a person. A reduction here is a defect in the writer, so it is reported as an audit
    /// invariant failure: the unit rolls back and the failure is an internal error, never a business outcome.
    /// </summary>
    public async ValueTask AssertRaiseOnlyAsync(CancellationToken cancellationToken = default)
    {
        var before = Before();
        var after = await _reads.ReadAsync(cancellationToken).ConfigureAwait(false);
        var verdict = AdministratorViabilityGuard.Detect(before, after);
        if (!verdict.IsAllowed)
        {
            throw new AuditInvariantException(
                $"A change that may only add to administrator viability reduced it ({verdict.Kind}, {verdict.View}).");
        }
    }

    private AdministratorViabilityState Before() =>
        _before ?? throw new InvalidOperationException("CaptureBeforeAsync must run before the mutation is applied.");
}

/// <inheritdoc cref="IAdministratorViabilityCoordinator" />
public sealed class AdministratorViabilityCoordinator : IAdministratorViabilityCoordinator
{
    private readonly IPlatformAdministratorSetGate _gate;
    private readonly IAdministratorViabilityService _reads;

    public AdministratorViabilityCoordinator(IPlatformAdministratorSetGate gate, IAdministratorViabilityService reads)
    {
        _gate = gate;
        _reads = reads;
    }

    /// <inheritdoc />
    public async ValueTask<AdministratorViabilityScope> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        return new AdministratorViabilityScope(_reads);
    }
}
