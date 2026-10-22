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
    /// Builds, outside any transaction, what the viability reads are made from (R-1). The first reading of a host builds its running
    /// Entra options, which reads the persisted registration and its secret through a connection of its own; made inside a unit that
    /// has written a registration row, that read waits for the unit's own row under locking READ COMMITTED and reads the row as it
    /// was before under READ_COMMITTED_SNAPSHOT, and the options it builds are then cached. A writer of Entra registrations calls
    /// this before it opens its audited unit, so that the first build can never happen behind the unit's own locks. A failure to
    /// build propagates: it is never turned into a reading.
    /// </summary>
    ValueTask PrepareAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes <see cref="IPlatformAdministratorSetGate"/> for the caller's open transaction. It must be the first lock the unit takes
    /// after its operation lock: before the authority row locks of <c>IRoleRepository.LockAuthoritySnapshotAsync</c> and
    /// before any other row. The gate is held until the unit's transaction ends, so a rerun after a rollback calls this again.
    /// </summary>
    /// <exception cref="PlatformAdministratorSetBusyException">Another change to administrator access holds the gate past the bound.</exception>
    /// <exception cref="PlatformAdministratorSetGateException">The lock call failed for a reason that is not contention.</exception>
    ValueTask<AdministratorViabilityScope> EnterAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the same gate, in the same place in the unit, for a writer that is proven unable to reduce administrator viability
    /// and reads no viability state: one that only creates an account that holds no administrator facts yet, or restores the
    /// administrator role to an account that does not hold it. It builds nothing and reads nothing: no deployment configuration, so
    /// no Entra or OpenID Connect options, and no administrator facts. The gate is an application lock held in the database, so
    /// the database must be reachable and migrated; what the caller does not need is the secret store or the host's authentication
    /// wiring, so a startup step that runs after the migrations can use it without causing the first build of the running Entra
    /// options (which must happen on real traffic, once the migration state is complete).
    /// </summary>
    /// <remarks>
    /// The scope it returns has no way to capture or verify a state, so it cannot be used to judge a reduction; a writer that can
    /// reduce takes <see cref="EnterAsync"/> and the shared guard. The set of callers is closed: the writer guard names each one,
    /// pins that it calls nothing that reads viability, and requires it to say why it cannot reduce. There is no parameter that
    /// switches the checks off.
    /// </remarks>
    /// <exception cref="PlatformAdministratorSetBusyException">Another change to administrator access holds the gate past the bound.</exception>
    /// <exception cref="PlatformAdministratorSetGateException">The lock call failed for a reason that is not contention.</exception>
    ValueTask<AdministratorGateOnlyScope> EnterGateOnlyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A hold on the global administrator-set gate by a writer that cannot reduce (<see cref="IAdministratorViabilityCoordinator.EnterGateOnlyAsync"/>).
/// It exists to show that the gate is held; it carries no reading and offers none, by design.
/// </summary>
public sealed class AdministratorGateOnlyScope
{
    internal AdministratorGateOnlyScope()
    {
    }
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
    private string? _affectedUserId;

    internal AdministratorViabilityScope(IAdministratorViabilityService reads) => _reads = reads;

    /// <summary>The state read before the change, by <see cref="CaptureBeforeAsync"/> or <see cref="CaptureBeforeForRaiseAsync"/>.</summary>
    public AdministratorViabilityState? Captured => _before;

    /// <summary>The state read by the last <see cref="VerifyAfterAsync"/> or <see cref="AssertRaiseOnlyAsync"/>: what the applied change left.</summary>
    public AdministratorViabilityState? After { get; private set; }

    /// <summary>
    /// For a raise-only writer: reads the state before the mutation and remembers whose viability the writer is changing. The
    /// assertion afterwards is about that user alone, under the configuration read here (see <see cref="AssertRaiseOnlyAsync"/>).
    /// </summary>
    public async ValueTask CaptureBeforeForRaiseAsync(string affectedUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(affectedUserId);
        _affectedUserId = affectedUserId;
        _before = await _reads.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the state before a reducer's mutation. Call it after the gate is held and before the mutation is applied.</summary>
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
        After = after;
        return AdministratorViabilityGuard.Evaluate(before, after);
    }

    /// <summary>
    /// For a mutation that can only add (assign the role, enable a credential, add a link): reads the state after it and asserts that
    /// <i>the writer's own change</i> reduced nothing. The assertion is attributed to the affected user: their stored facts before
    /// and after are judged under the configuration captured before the change, so a reload of the configuration, a change in
    /// Recovery Access health or a change to another administrator, none of which this writer did, cannot fail it. It never refuses
    /// a person and needs no trusted configuration. A reduction of the affected user's own contribution to VOA or EI is a defect in
    /// the writer, so it is reported as an audit invariant failure: the unit rolls back and the failure is an internal error, never
    /// a business outcome.
    /// </summary>
    public async ValueTask AssertRaiseOnlyAsync(CancellationToken cancellationToken = default)
    {
        var before = Before();
        var affected = _affectedUserId
            ?? throw new InvalidOperationException("A raise-only writer must capture the state with CaptureBeforeForRaiseAsync.");
        var after = await _reads.ReadAsync(cancellationToken).ConfigureAwait(false);
        After = after;
        if (RaiseOnlyAttribution.FindReduction(before, after, affected) is { } reduction)
            throw new AuditInvariantException($"A change that may only add to administrator viability reduced it: {reduction}.");
    }

    private AdministratorViabilityState Before() =>
        _before ?? throw new InvalidOperationException("CaptureBeforeAsync must run before the mutation is applied.");
}

/// <inheritdoc cref="IAdministratorViabilityCoordinator" />
public sealed class AdministratorViabilityCoordinator : IAdministratorViabilityCoordinator
{
    private readonly IPlatformAdministratorSetGate _gate;
    private readonly IAdministratorViabilityService _reads;
    private readonly IDeploymentAuthenticationConfiguration? _deployment;

    public AdministratorViabilityCoordinator(
        IPlatformAdministratorSetGate gate,
        IAdministratorViabilityService reads,
        IDeploymentAuthenticationConfiguration? deployment = null)
    {
        _gate = gate;
        _reads = reads;
        _deployment = deployment;
    }

    /// <inheritdoc />
    public ValueTask PrepareAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _deployment?.EnsureInitialized();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<AdministratorViabilityScope> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        return new AdministratorViabilityScope(_reads);
    }

    /// <inheritdoc />
    public async ValueTask<AdministratorGateOnlyScope> EnterGateOnlyAsync(CancellationToken cancellationToken = default)
    {
        // The gate and nothing else: no deployment configuration is read, so no Entra options are built, and no administrator
        // fact is read.
        await _gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        return new AdministratorGateOnlyScope();
    }
}
