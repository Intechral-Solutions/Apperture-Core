using System.Data;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using AuditEvent = Apperture.Core.Application.Audit.AuditEvent;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Reads the module catalog and changes a module's installation state. Install, enable, disable and uninstall are audited units
/// of work (the installation row and its audit row commit together, or neither does); discovery stays a legacy telemetry row.
/// </summary>
/// <remarks>
/// <para><b>One unit per transition.</b> The host-side descriptor lookup (no database) comes first: a module the host does not
/// discover is refused before anything opens. Inside the unit's attempt, in this order: the module lifecycle lock, then every read
/// the decision depends on (untracked), then the decision, then the write, saved inside the attempt, then the staged audit row.
/// The answer of every path is the answer the API always gave; only what is persisted and recorded changed.</para>
/// <para><b>The lifecycle lock</b> (<see cref="ModuleLifecycleLockResource"/>) is a transaction-owned application lock taken before
/// anything is read. Whether a module may be enabled depends on which other modules are enabled, so two transitions of different
/// modules must not decide against the same snapshot: they queue on the one lock, and each reads what the other committed. The
/// lock is released with the transaction.</para>
/// <para><b>Blocker evaluation works on copies.</b> The candidate state ("this module enabled") is built on detached copies of the
/// persisted rows, so nothing evaluated can ever be saved. A blocked enable therefore changes nothing: it neither enables an
/// installed module nor leaves behind the installation it would have created.</para>
/// <para><b>No-ops and refusals.</b> Installing an installed module, enabling an enabled module at the discovered version, and
/// disabling a disabled module declare a no-op: success as before, nothing written, nothing recorded, no update time. A transition
/// that is refused (contribution conflicts, persisted references, a module that is not installed) declares a no-op too, and the
/// refusal is recorded after the unit as a best-effort attempt row of the same actor, naming the blocker codes and never their
/// messages; failing to write it never changes the answer.</para>
/// <para><b>Enabling a module that is not installed</b> inserts its installation, enabled, in the same unit: a secondary install
/// row (flagged as implicit) is staged beside the primary enable row. A refused or failed enable leaves no installation.</para>
/// <para><b>Insert races.</b> Both inserts are saved inside the attempt. A writer that bypassed the lifecycle lock and committed the
/// same installation first violates <c>PK_ModuleInstallationStates</c>; that one violation is not an error: the row is detached and
/// the decision is made again, once, against the row that won (an install then finds it installed, an enable updates it). Any
/// other violation, and a second one, propagate.</para>
/// <para><b>Actor.</b> The authenticated principal when there is one; otherwise the named module lifecycle actor, for host-start
/// callers. A row records persisted state and the restart effect the transition reports, and never claims that a module is active
/// in any process: the worker that hosts a module reads the registry on its own schedule.</para>
/// <para>The worker host registers the registry without the audited unit: it only reads and discovers there, and a lifecycle
/// transition without the unit fails as an invariant failure instead of changing state unrecorded.</para>
/// </remarks>
public sealed class SqlModuleRegistryService : IModuleRegistryService
{
    /// <summary>The one module lifecycle lock name. Never parameterize or extend this.</summary>
    public const string ModuleLifecycleLockResource = "apperture:module-lifecycle";

    /// <summary>The primary key of an installation row: the constraint a lost insert race violates.</summary>
    internal const string InstallationKeyName = "PK_ModuleInstallationStates";

    private static readonly TimeSpan LifecycleLockTimeout = TimeSpan.FromSeconds(15);

    private readonly IEnumerable<IModule> _modules;
    private readonly AppertureDbContext _dbContext;
    private readonly IEnvironmentContext _environmentContext;
    private readonly IAuditService _auditService;
    private readonly IPrincipalContext? _principalContext;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEventWriter;

    public SqlModuleRegistryService(
        IEnumerable<IModule> modules,
        AppertureDbContext dbContext,
        IEnvironmentContext environmentContext,
        IAuditService auditService,
        IPrincipalContext? principalContext = null,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEventWriter = null)
    {
        _modules = modules ?? [];
        _dbContext = dbContext;
        _environmentContext = environmentContext;
        _auditService = auditService;
        _principalContext = principalContext;
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEventWriter = auditEventWriter;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new AuditInvariantException("Module lifecycle changes require the audited unit of work.");

    private IAuditEventWriter Events =>
        _auditEventWriter ?? throw new AuditInvariantException("Module lifecycle changes require the audit event writer.");

    public async ValueTask<IReadOnlyList<ModuleCatalogEntry>> ListModulesAsync(CancellationToken cancellationToken = default)
    {
        var discovered = await DiscoverDescriptorsAsync(cancellationToken).ConfigureAwait(false);
        var installs = await GetInstallationsByModuleIdAsync(cancellationToken).ConfigureAwait(false);
        var issues = BuildCrossModuleIssues(discovered, installs, onlyEnabled: true);
        var discoveredById = discovered.ToDictionary(x => x.ModuleId, StringComparer.Ordinal);
        var list = new List<ModuleCatalogEntry>(Math.Max(discovered.Count, installs.Count));

        foreach (var descriptor in discovered)
        {
            installs.TryGetValue(descriptor.ModuleId, out var install);
            list.Add(ToCatalogEntry(descriptor, install, issues));
        }

        foreach (var install in installs.Values.Where(x => !discoveredById.ContainsKey(x.ModuleId)))
        {
            list.Add(new ModuleCatalogEntry(
                CreateMissingDescriptor(install.ModuleId, install.Version),
                install.Enabled ? ModuleLifecycleState.InstalledEnabled : ModuleLifecycleState.InstalledDisabled,
                Installed: true,
                Enabled: install.Enabled,
                EffectivelyActive: false,
                ValidationIssues: [new ModuleLifecycleBlocker("descriptor_missing", $"Installed module '{install.ModuleId}' is not discoverable by this host.")],
                UpdatedAtUtc: install.UpdatedAt));
        }

        return list
            .OrderBy(x => x.Descriptor.ModuleId, StringComparer.Ordinal)
            .ToList();
    }

    public async ValueTask<ModuleCatalogEntry?> GetModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var all = await ListModulesAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(x => string.Equals(x.Descriptor.ModuleId, moduleId, StringComparison.Ordinal));
    }

    public async ValueTask<IReadOnlyList<ModuleDescriptor>> GetEnabledDescriptorsAsync(CancellationToken cancellationToken = default)
    {
        var entries = await ListModulesAsync(cancellationToken).ConfigureAwait(false);
        return entries
            .Where(x => x.EffectivelyActive)
            .Select(x => x.Descriptor)
            .ToList();
    }

    public async ValueTask<ModuleLifecycleTransitionResult> DiscoverModulesAsync(CancellationToken cancellationToken = default)
    {
        await DiscoverDescriptorsAsync(cancellationToken).ConfigureAwait(false);
        var result = new ModuleLifecycleTransitionResult(
            Succeeded: true,
            ModuleId: "*",
            Action: "discover",
            State: ModuleLifecycleState.Discovered,
            Effect: ModuleLifecycleEffect.Immediate,
            Blockers: []);
        await AppendDiscoveryAuditAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Installs a module: the installation row is inserted disabled. Installing an installed module is a no-op that reports the
    /// installation as it is. An install that would conflict with an enabled module's contributions is refused, and that refusal
    /// is recorded as a blocked attempt.
    /// </summary>
    public async ValueTask<ModuleLifecycleTransitionResult> InstallModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        var events = Events;
        var (discovered, descriptor) = await RequireDescriptorAsync(moduleId, cancellationToken).ConfigureAwait(false);
        var actor = await ResolveActorAsync(cancellationToken).ConfigureAwait(false);

        var attempt = await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ModuleInstalled, actor),
            (transaction, ct) => InstallAttemptAsync(transaction, discovered, descriptor, ct),
            (committed, _) => Task.FromResult(Reconstruct(committed, descriptor.ModuleId)),
            cancellationToken).ConfigureAwait(false);
        return await FinishAsync(events, attempt, actor).ConfigureAwait(false);
    }

    private async Task<LifecycleAttempt> InstallAttemptAsync(
        IAuditedTransaction transaction,
        IReadOnlyList<ModuleDescriptor> discovered,
        ModuleDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        await LockLifecycleAsync(cancellationToken).ConfigureAwait(false);

        for (var pass = 0; ; pass++)
        {
            var persisted = await ReadEnvironmentInstallationsAsync(cancellationToken).ConfigureAwait(false);
            var existing = persisted.FirstOrDefault(x => string.Equals(x.ModuleId, descriptor.ModuleId, StringComparison.Ordinal));
            if (existing is not null)
            {
                transaction.DeclareNoOp();
                return new LifecycleAttempt(new ModuleLifecycleTransitionResult(
                    true, descriptor.ModuleId, "install", StateOf(existing), ModuleLifecycleEffect.Immediate, []));
            }

            var conflicts = ConflictBlockers(discovered, persisted, descriptor);
            if (conflicts.Count > 0)
            {
                transaction.DeclareNoOp();
                return Refused(
                    new ModuleLifecycleTransitionResult(false, descriptor.ModuleId, "install", ModuleLifecycleState.Discovered, ModuleLifecycleEffect.Immediate, conflicts),
                    AuditEvents.ModuleInstallBlocked(descriptor.ModuleId, ModuleLifecycleState.Discovered, conflicts.Select(x => x.Code).ToList()));
            }

            var row = NewInstallation(descriptor, enabled: false);
            _dbContext.ModuleInstallationStates.Add(row);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (pass == 0 && SqlUniqueKeyViolation.IsOnIndex(ex, InstallationKeyName))
            {
                // A writer that bypassed the lock committed this installation first; ours rolled back to the savepoint. Decide
                // again, once, against the row that won.
                _dbContext.Entry(row).State = EntityState.Detached;
                continue;
            }

            await transaction.StageAsync(
                AuditEvents.ModuleInstalled(
                    descriptor.ModuleId, descriptor.Version, ModuleLifecycleState.Discovered, ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate),
                cancellationToken).ConfigureAwait(false);
            return new LifecycleAttempt(new ModuleLifecycleTransitionResult(
                true, descriptor.ModuleId, "install", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate, []));
        }
    }

    /// <summary>
    /// Enables a module at the discovered version. A module that is not installed is installed enabled in the same unit (a secondary
    /// install row is recorded beside the enable row). The contribution conflicts are decided first, whether or not the module is
    /// installed; a refusal changes nothing, not even an installation, and is recorded as a blocked attempt. Enabling a module that
    /// is enabled at the discovered version is a no-op.
    /// </summary>
    public async ValueTask<ModuleLifecycleTransitionResult> EnableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        var events = Events;
        var (discovered, descriptor) = await RequireDescriptorAsync(moduleId, cancellationToken).ConfigureAwait(false);
        var actor = await ResolveActorAsync(cancellationToken).ConfigureAwait(false);

        var attempt = await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ModuleEnabled, actor),
            (transaction, ct) => EnableAttemptAsync(transaction, discovered, descriptor, ct),
            (committed, _) => Task.FromResult(Reconstruct(committed, descriptor.ModuleId)),
            cancellationToken).ConfigureAwait(false);
        return await FinishAsync(events, attempt, actor).ConfigureAwait(false);
    }

    private async Task<LifecycleAttempt> EnableAttemptAsync(
        IAuditedTransaction transaction,
        IReadOnlyList<ModuleDescriptor> discovered,
        ModuleDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        await LockLifecycleAsync(cancellationToken).ConfigureAwait(false);

        for (var pass = 0; ; pass++)
        {
            var persisted = await ReadEnvironmentInstallationsAsync(cancellationToken).ConfigureAwait(false);
            var existing = persisted.FirstOrDefault(x => string.Equals(x.ModuleId, descriptor.ModuleId, StringComparison.Ordinal));

            var blockers = ConflictBlockers(discovered, persisted, descriptor);
            if (blockers.Count > 0)
            {
                transaction.DeclareNoOp();
                return Refused(
                    new ModuleLifecycleTransitionResult(false, descriptor.ModuleId, "enable", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate, blockers),
                    AuditEvents.ModuleEnableBlocked(descriptor.ModuleId, existing is null ? ModuleLifecycleState.Discovered : StateOf(existing), blockers.Select(x => x.Code).ToList()));
            }

            var enabled = new ModuleLifecycleTransitionResult(
                true, descriptor.ModuleId, "enable", ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart, []);

            if (existing is not null && existing.Enabled && string.Equals(existing.Version, descriptor.Version, StringComparison.Ordinal))
            {
                transaction.DeclareNoOp();
                return new LifecycleAttempt(enabled);
            }

            if (existing is null)
            {
                var row = NewInstallation(descriptor, enabled: true);
                _dbContext.ModuleInstallationStates.Add(row);
                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (DbUpdateException ex) when (pass == 0 && SqlUniqueKeyViolation.IsOnIndex(ex, InstallationKeyName))
                {
                    // A writer that bypassed the lock committed this installation first; ours rolled back to the savepoint.
                    // Decide again, once: the winner's row is then updated.
                    _dbContext.Entry(row).State = EntityState.Detached;
                    continue;
                }

                // The installation the request performed is the secondary row; the enable is the primary.
                await transaction.StageAsync(
                    AuditEvents.ModuleInstalled(
                        descriptor.ModuleId, descriptor.Version, ModuleLifecycleState.Discovered, ModuleLifecycleState.InstalledEnabled,
                        ModuleLifecycleEffect.PartialUntilRestart, implicitInstall: true),
                    cancellationToken).ConfigureAwait(false);
                await transaction.StageAsync(
                    AuditEvents.ModuleEnabled(
                        descriptor.ModuleId, descriptor.Version, null, ModuleLifecycleState.Discovered, ModuleLifecycleState.InstalledEnabled,
                        ModuleLifecycleEffect.PartialUntilRestart),
                    cancellationToken).ConfigureAwait(false);
                return new LifecycleAttempt(enabled);
            }

            var previousState = StateOf(existing);
            var previousVersion = string.Equals(existing.Version, descriptor.Version, StringComparison.Ordinal) ? null : existing.Version;
            AttachForUpdate(existing);
            existing.Enabled = true;
            existing.Version = descriptor.Version;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await transaction.StageAsync(
                AuditEvents.ModuleEnabled(
                    descriptor.ModuleId, descriptor.Version, previousVersion, previousState, ModuleLifecycleState.InstalledEnabled,
                    ModuleLifecycleEffect.PartialUntilRestart),
                cancellationToken).ConfigureAwait(false);
            return new LifecycleAttempt(enabled);
        }
    }

    /// <summary>
    /// Disables a module. Persisted workflow and operation instances that reference the module's definitions refuse the disable; a
    /// module that is not installed is refused too. Disabling a disabled module is a no-op. Both refusals are recorded as blocked
    /// attempts.
    /// </summary>
    public async ValueTask<ModuleLifecycleTransitionResult> DisableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        var events = Events;
        var (_, descriptor) = await RequireDescriptorAsync(moduleId, cancellationToken).ConfigureAwait(false);
        var actor = await ResolveActorAsync(cancellationToken).ConfigureAwait(false);

        var attempt = await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ModuleDisabled, actor),
            (transaction, ct) => DisableAttemptAsync(transaction, descriptor, ct),
            (committed, _) => Task.FromResult(Reconstruct(committed, descriptor.ModuleId)),
            cancellationToken).ConfigureAwait(false);
        return await FinishAsync(events, attempt, actor).ConfigureAwait(false);
    }

    private async Task<LifecycleAttempt> DisableAttemptAsync(
        IAuditedTransaction transaction,
        ModuleDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        await LockLifecycleAsync(cancellationToken).ConfigureAwait(false);

        var existing = await ReadInstallationAsync(descriptor.ModuleId, cancellationToken).ConfigureAwait(false);
        var currentState = existing is null ? ModuleLifecycleState.Discovered : StateOf(existing);

        var blockers = await GetDisableOrUninstallBlockersAsync(descriptor, cancellationToken).ConfigureAwait(false);
        if (blockers.Count > 0)
        {
            transaction.DeclareNoOp();
            return Refused(
                new ModuleLifecycleTransitionResult(false, descriptor.ModuleId, "disable", ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart, blockers),
                AuditEvents.ModuleDisableBlocked(descriptor.ModuleId, currentState, blockers.Select(x => x.Code).ToList()));
        }

        if (existing is null)
        {
            transaction.DeclareNoOp();
            return Refused(
                new ModuleLifecycleTransitionResult(false, descriptor.ModuleId, "disable", ModuleLifecycleState.Discovered, ModuleLifecycleEffect.Immediate, [NotInstalled()]),
                AuditEvents.ModuleDisableBlocked(descriptor.ModuleId, currentState, [NotInstalled().Code]));
        }

        var disabled = new ModuleLifecycleTransitionResult(
            true, descriptor.ModuleId, "disable", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.PartialUntilRestart, []);
        if (!existing.Enabled)
        {
            transaction.DeclareNoOp();
            return new LifecycleAttempt(disabled);
        }

        AttachForUpdate(existing);
        existing.Enabled = false;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.StageAsync(
            AuditEvents.ModuleDisabled(
                descriptor.ModuleId, existing.Version, ModuleLifecycleState.InstalledEnabled, ModuleLifecycleState.InstalledDisabled,
                ModuleLifecycleEffect.PartialUntilRestart),
            cancellationToken).ConfigureAwait(false);
        return new LifecycleAttempt(disabled);
    }

    /// <summary>
    /// Uninstalls a module: the installation row is deleted. A module that is not installed is refused, and so is one whose
    /// definitions persisted workflow or operation instances reference; both refusals are recorded as blocked attempts.
    /// </summary>
    public async ValueTask<ModuleLifecycleTransitionResult> UninstallModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        var events = Events;
        var (_, descriptor) = await RequireDescriptorAsync(moduleId, cancellationToken).ConfigureAwait(false);
        var actor = await ResolveActorAsync(cancellationToken).ConfigureAwait(false);

        var attempt = await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ModuleUninstalled, actor),
            (transaction, ct) => UninstallAttemptAsync(transaction, descriptor, ct),
            (committed, _) => Task.FromResult(Reconstruct(committed, descriptor.ModuleId)),
            cancellationToken).ConfigureAwait(false);
        return await FinishAsync(events, attempt, actor).ConfigureAwait(false);
    }

    private async Task<LifecycleAttempt> UninstallAttemptAsync(
        IAuditedTransaction transaction,
        ModuleDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        await LockLifecycleAsync(cancellationToken).ConfigureAwait(false);

        var existing = await ReadInstallationAsync(descriptor.ModuleId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            transaction.DeclareNoOp();
            return Refused(
                new ModuleLifecycleTransitionResult(false, descriptor.ModuleId, "uninstall", ModuleLifecycleState.Discovered, ModuleLifecycleEffect.Immediate, [NotInstalled()]),
                AuditEvents.ModuleUninstallBlocked(descriptor.ModuleId, ModuleLifecycleState.Discovered, [NotInstalled().Code]));
        }

        var previousState = StateOf(existing);
        var blockers = await GetDisableOrUninstallBlockersAsync(descriptor, cancellationToken).ConfigureAwait(false);
        if (blockers.Count > 0)
        {
            transaction.DeclareNoOp();
            return Refused(
                new ModuleLifecycleTransitionResult(false, descriptor.ModuleId, "uninstall", previousState, ModuleLifecycleEffect.RequiresRestart, blockers),
                AuditEvents.ModuleUninstallBlocked(descriptor.ModuleId, previousState, blockers.Select(x => x.Code).ToList()));
        }

        AttachForUpdate(existing);
        _dbContext.ModuleInstallationStates.Remove(existing);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.StageAsync(
            AuditEvents.ModuleUninstalled(
                descriptor.ModuleId, existing.Version, previousState, ModuleLifecycleState.Discovered, ModuleLifecycleEffect.RequiresRestart),
            cancellationToken).ConfigureAwait(false);
        return new LifecycleAttempt(new ModuleLifecycleTransitionResult(
            true, descriptor.ModuleId, "uninstall", ModuleLifecycleState.Discovered, ModuleLifecycleEffect.RequiresRestart, []));
    }

    /// <summary>The outcome of one attempt: the answer for the caller and, for a refusal, the attempt row to record after the unit.</summary>
    private sealed record LifecycleAttempt(ModuleLifecycleTransitionResult Result, AuditEvent? Refusal = null);

    private static LifecycleAttempt Refused(ModuleLifecycleTransitionResult result, AuditEvent refusal) => new(result, refusal);

    private static ModuleLifecycleBlocker NotInstalled() => new("not_installed", "Module is not installed.");

    private static ModuleLifecycleState StateOf(ModuleInstallationStateEntity row) =>
        row.Enabled ? ModuleLifecycleState.InstalledEnabled : ModuleLifecycleState.InstalledDisabled;

    /// <summary>
    /// Records a refusal after the unit, as a best-effort attempt row of the same actor (a failure to write it is logged by the
    /// writer and never changes the answer), and returns the answer.
    /// </summary>
    private static async ValueTask<ModuleLifecycleTransitionResult> FinishAsync(IAuditEventWriter events, LifecycleAttempt attempt, AuditActor actor)
    {
        if (attempt.Refusal is not null)
            await events.TryWriteAsync(attempt.Refusal, actor, CancellationToken.None).ConfigureAwait(false);
        return attempt.Result;
    }

    /// <summary>
    /// The answer of a transition whose commit was verified after a lost acknowledgement, built from the committed primary row:
    /// the transition from its action, the persisted state and the restart effect from its fields. Never a refusal.
    /// </summary>
    private static LifecycleAttempt Reconstruct(AuditCommittedOperation committed, string moduleId)
    {
        var transition = committed.Action switch
        {
            AuditActionNames.ModuleInstalled => "install",
            AuditActionNames.ModuleEnabled => "enable",
            AuditActionNames.ModuleDisabled => "disable",
            AuditActionNames.ModuleUninstalled => "uninstall",
            _ => throw new InvalidOperationException($"The committed module change '{committed.Action}' is not a module lifecycle transition."),
        };
        var state = committed.GetField("state") switch
        {
            "discovered" => ModuleLifecycleState.Discovered,
            "installed_disabled" => ModuleLifecycleState.InstalledDisabled,
            "installed_enabled" => ModuleLifecycleState.InstalledEnabled,
            var other => throw new InvalidOperationException($"The committed module change records no readable state ('{other}')."),
        };
        var effect = committed.GetField("restartEffect") switch
        {
            "immediate" => ModuleLifecycleEffect.Immediate,
            "partial_until_restart" => ModuleLifecycleEffect.PartialUntilRestart,
            "requires_restart" => ModuleLifecycleEffect.RequiresRestart,
            var other => throw new InvalidOperationException($"The committed module change records no readable restart effect ('{other}')."),
        };
        return new LifecycleAttempt(new ModuleLifecycleTransitionResult(true, moduleId, transition, state, effect, []));
    }

    /// <summary>
    /// The authenticated principal when there is one; otherwise the named module lifecycle actor (a host-start caller has no
    /// principal). Resolved once per call, and used for the unit and for a blocked attempt alike.
    /// </summary>
    private async ValueTask<AuditActor> ResolveActorAsync(CancellationToken cancellationToken)
    {
        if (_principalContext is not null
            && await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return AuditActor.CurrentPrincipal;
        }

        return AuditActor.System("module-lifecycle");
    }

    /// <summary>
    /// Takes the module lifecycle lock for the rest of the transaction (a <c>Transaction</c>-owned application lock). It must be
    /// the first statement of the attempt after the unit's own: whether a module may be enabled depends on every other module's
    /// state, so each transition reads only once it holds the lock. Relational providers only.
    /// </summary>
    private async Task LockLifecycleAsync(CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
            return;

        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "The module lifecycle lock must be taken inside an open transaction: a 'Transaction'-owned application lock taken outside one is released immediately and guarantees nothing.");

        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandTimeout = Math.Max((int)LifecycleLockTimeout.TotalSeconds + 5, 30);
        command.CommandText =
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = @lockTimeout;";
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        command.Parameters.Add(result);
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = ModuleLifecycleLockResource });
        command.Parameters.Add(new SqlParameter("@lockTimeout", SqlDbType.Int) { Value = (int)LifecycleLockTimeout.TotalMilliseconds });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // sp_getapplock: 0 = granted, 1 = granted after waiting, -1 = timeout, -2 = cancelled, -3 = deadlock victim.
        var code = result.Value is int value ? value : -999;
        if (code >= 0)
            return;
        throw new InvalidOperationException(
            code is -1 or -2 or -3
                ? "Another module change is in progress. Wait a moment and try again."
                : $"Failed to take the module lifecycle lock (code {code}).");
    }

    private ModuleInstallationStateEntity NewInstallation(ModuleDescriptor descriptor, bool enabled)
    {
        var now = DateTimeOffset.UtcNow;
        return new ModuleInstallationStateEntity
        {
            EnvironmentId = _environmentContext.EnvironmentId,
            ModuleId = descriptor.ModuleId,
            Version = descriptor.Version,
            Enabled = enabled,
            InstalledAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>Every installation of the environment, untracked: a decision is made on rows nothing can save.</summary>
    private async Task<List<ModuleInstallationStateEntity>> ReadEnvironmentInstallationsAsync(CancellationToken cancellationToken) =>
        await _dbContext.ModuleInstallationStates.AsNoTracking()
            .Where(x => x.EnvironmentId == _environmentContext.EnvironmentId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>One installation, untracked.</summary>
    private Task<ModuleInstallationStateEntity?> ReadInstallationAsync(string moduleId, CancellationToken cancellationToken) =>
        _dbContext.ModuleInstallationStates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EnvironmentId == _environmentContext.EnvironmentId && x.ModuleId == moduleId, cancellationToken);

    /// <summary>Starts tracking a row read untracked, as unchanged, so only the properties then set are written.</summary>
    private void AttachForUpdate(ModuleInstallationStateEntity row)
    {
        var tracked = _dbContext.ModuleInstallationStates.Local
            .FirstOrDefault(x => x.EnvironmentId == row.EnvironmentId && string.Equals(x.ModuleId, row.ModuleId, StringComparison.Ordinal));
        if (tracked is not null)
            _dbContext.Entry(tracked).State = EntityState.Detached;
        _dbContext.ModuleInstallationStates.Attach(row);
    }

    /// <summary>
    /// The blockers of enabling <paramref name="descriptor"/> now, on copies of the persisted rows: the candidate is treated as
    /// enabled whether or not it is installed, and nothing built here is ever tracked or saved.
    /// </summary>
    private IReadOnlyList<ModuleLifecycleBlocker> ConflictBlockers(
        IReadOnlyList<ModuleDescriptor> discovered,
        IReadOnlyList<ModuleInstallationStateEntity> persisted,
        ModuleDescriptor descriptor)
    {
        var installs = persisted.ToDictionary(
            x => x.ModuleId,
            x => new ModuleInstallationStateEntity
            {
                EnvironmentId = x.EnvironmentId,
                ModuleId = x.ModuleId,
                Version = x.Version,
                Enabled = x.Enabled,
                InstalledAt = x.InstalledAt,
                UpdatedAt = x.UpdatedAt
            },
            StringComparer.Ordinal);

        if (installs.TryGetValue(descriptor.ModuleId, out var candidate))
        {
            candidate.Enabled = true;
        }
        else
        {
            installs[descriptor.ModuleId] = new ModuleInstallationStateEntity
            {
                EnvironmentId = _environmentContext.EnvironmentId,
                ModuleId = descriptor.ModuleId,
                Version = descriptor.Version,
                Enabled = true
            };
        }

        var issues = BuildCrossModuleIssues(discovered, installs, onlyEnabled: true);
        return issues.TryGetValue(descriptor.ModuleId, out var blockers)
            ? blockers
            : [];
    }

    private async ValueTask<IReadOnlyList<ModuleDescriptor>> DiscoverDescriptorsAsync(CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<ModuleDescriptor>();
        foreach (var module in _modules)
        {
            var descriptor = await module.BuildDescriptorAsync(cancellationToken).ConfigureAwait(false);
            if (!seen.Add(descriptor.ModuleId))
                throw new InvalidOperationException($"Duplicate module id '{descriptor.ModuleId}' discovered.");
            list.Add(descriptor);
        }

        return list;
    }

    /// <summary>
    /// The module's descriptor and every discovered descriptor, from the host's modules (no database). A module the host does not
    /// discover is the caller's refusal, decided before any unit opens.
    /// </summary>
    private async ValueTask<(IReadOnlyList<ModuleDescriptor> Discovered, ModuleDescriptor Descriptor)> RequireDescriptorAsync(
        string moduleId, CancellationToken cancellationToken)
    {
        var discovered = await DiscoverDescriptorsAsync(cancellationToken).ConfigureAwait(false);
        var descriptor = discovered.FirstOrDefault(x => string.Equals(x.ModuleId, moduleId, StringComparison.Ordinal));
        return (discovered, descriptor ?? throw new InvalidOperationException($"Module '{moduleId}' is not discovered."));
    }

    private async ValueTask<Dictionary<string, ModuleInstallationStateEntity>> GetInstallationsByModuleIdAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.ModuleInstallationStates.AsNoTracking()
            .Where(x => x.EnvironmentId == _environmentContext.EnvironmentId)
            .ToDictionaryAsync(x => x.ModuleId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<ModuleLifecycleBlocker>> GetDisableOrUninstallBlockersAsync(ModuleDescriptor descriptor, CancellationToken cancellationToken)
    {
        var blockers = new List<ModuleLifecycleBlocker>();
        var workflowIds = descriptor.Contributions
            .OfType<WorkflowContributionDescriptor>()
            .Select(x => x.Id)
            .ToArray();
        if (workflowIds.Length > 0)
        {
            var hasWorkflowInstances = await _dbContext.WorkflowInstances.AnyAsync(x => workflowIds.Contains(x.DefinitionId), cancellationToken).ConfigureAwait(false);
            if (hasWorkflowInstances)
                blockers.Add(new("active_workflow_references", "Module contributes workflow definitions referenced by persisted workflow instances."));
        }

        var operationIds = descriptor.Contributions
            .OfType<OperationContributionDescriptor>()
            .Select(x => x.Id)
            .ToArray();
        if (operationIds.Length > 0)
        {
            var hasOperationInstances = await _dbContext.OperationInstances.AnyAsync(x => operationIds.Contains(x.DefinitionId), cancellationToken).ConfigureAwait(false);
            if (hasOperationInstances)
                blockers.Add(new("active_operation_references", "Module contributes operation definitions referenced by persisted operation instances."));
        }

        return blockers;
    }

    private static ModuleCatalogEntry ToCatalogEntry(
        ModuleDescriptor descriptor,
        ModuleInstallationStateEntity? state,
        IReadOnlyDictionary<string, IReadOnlyList<ModuleLifecycleBlocker>> issuesByModule)
    {
        if (state is null)
            return new ModuleCatalogEntry(
                descriptor,
                ModuleLifecycleState.Discovered,
                Installed: false,
                Enabled: false,
                EffectivelyActive: false,
                ValidationIssues: []);

        var issues = issuesByModule.TryGetValue(descriptor.ModuleId, out var mappedIssues)
            ? mappedIssues
            : [];
        var effectivelyActive = state.Enabled && issues.Count == 0;
        return new ModuleCatalogEntry(
            descriptor,
            state.Enabled ? ModuleLifecycleState.InstalledEnabled : ModuleLifecycleState.InstalledDisabled,
            Installed: true,
            Enabled: state.Enabled,
            EffectivelyActive: effectivelyActive,
            ValidationIssues: issues,
            UpdatedAtUtc: state.UpdatedAt);
    }

    private static Dictionary<string, IReadOnlyList<ModuleLifecycleBlocker>> BuildCrossModuleIssues(
        IReadOnlyList<ModuleDescriptor> discovered,
        IReadOnlyDictionary<string, ModuleInstallationStateEntity> installs,
        bool onlyEnabled)
    {
        var discoveredById = discovered.ToDictionary(x => x.ModuleId, StringComparer.Ordinal);
        var issues = new Dictionary<string, List<ModuleLifecycleBlocker>>(StringComparer.Ordinal);

        foreach (var install in installs.Values)
        {
            if (!discoveredById.ContainsKey(install.ModuleId))
                AddIssue(issues, install.ModuleId, "descriptor_missing", $"Installed module '{install.ModuleId}' is not discoverable by this host.");
        }

        var active = discovered.Where(d =>
        {
            if (!installs.TryGetValue(d.ModuleId, out var install))
                return !onlyEnabled;
            return !onlyEnabled || install.Enabled;
        }).ToList();

        EnforceUnique(
            active,
            issues,
            d => d.Contributions.OfType<WorkflowContributionDescriptor>().Select(c => c.Id),
            "workflow_id_conflict",
            "workflow id");
        EnforceUnique(
            active,
            issues,
            d => d.Contributions.OfType<OperationContributionDescriptor>().Select(c => c.Id),
            "operation_id_conflict",
            "operation id");
        EnforceUnique(
            active,
            issues,
            d => d.Contributions.OfType<NavigationContributionDescriptor>().Select(c => c.Id),
            "navigation_id_conflict",
            "navigation id");
        EnforceUnique(
            active,
            issues,
            d => d.Contributions.OfType<NavigationContributionDescriptor>().Select(c => c.Route),
            "navigation_route_conflict",
            "navigation route");
        // Permissions are registered under module:{moduleId} scope (see ModulePermissionRegistrar).
        // Conflict keys must include the owning module; otherwise two modules declaring the same
        // (action, resourceType) — e.g. workflow.execute:WorkflowInstance — false-positive as duplicates.
        EnforceUnique(
            active,
            issues,
            d => d.Contributions.OfType<PermissionContributionDescriptor>()
                .Select(c => $"{d.ModuleId}:{c.Action}:{c.ResourceType}"),
            "permission_conflict",
            "permission");

        return issues.ToDictionary(x => x.Key, x => (IReadOnlyList<ModuleLifecycleBlocker>)x.Value, StringComparer.Ordinal);
    }

    private static void EnforceUnique(
        IReadOnlyList<ModuleDescriptor> active,
        IDictionary<string, List<ModuleLifecycleBlocker>> issues,
        Func<ModuleDescriptor, IEnumerable<string>> selector,
        string issueCode,
        string label)
    {
        var ownerByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var descriptor in active)
        {
            foreach (var value in selector(descriptor).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (ownerByKey.TryGetValue(value, out var owner))
                {
                    // Duplicate contribution keys from the same module are ignored (descriptor bug / duplicate rows).
                    if (string.Equals(owner, descriptor.ModuleId, StringComparison.Ordinal))
                        continue;
                    AddIssue(issues, descriptor.ModuleId, issueCode, $"Conflicting {label} '{value}' already provided by enabled module '{owner}'.");
                    AddIssue(issues, owner, issueCode, $"Conflicting {label} '{value}' also provided by enabled module '{descriptor.ModuleId}'.");
                    continue;
                }
                ownerByKey[value] = descriptor.ModuleId;
            }
        }
    }

    private static void AddIssue(
        IDictionary<string, List<ModuleLifecycleBlocker>> issues,
        string moduleId,
        string code,
        string message)
    {
        if (!issues.TryGetValue(moduleId, out var list))
        {
            list = [];
            issues[moduleId] = list;
        }

        if (list.Any(x => x.Code == code && x.Message == message))
            return;
        list.Add(new ModuleLifecycleBlocker(code, message));
    }

    private static ModuleDescriptor CreateMissingDescriptor(string moduleId, string version)
        => new(moduleId, moduleId, version, "Descriptor is missing from host discovery.", null, null, []);

    /// <summary>
    /// The legacy telemetry row of module discovery (design gate W4, a later slice). It is the only legacy append left in this
    /// service: the lifecycle transitions record through the audited unit of work.
    /// </summary>
    private async ValueTask AppendDiscoveryAuditAsync(
        ModuleLifecycleTransitionResult result,
        CancellationToken cancellationToken)
    {
        const string details = "Discover modules";
        var actor = _principalContext is null
            ? null
            : await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        var actorId = actor?.Id ?? new ActorId("system:module-lifecycle");
        var blockerSummary = result.Blockers.Count == 0
            ? "none"
            : string.Join("|", result.Blockers.Select(x => $"{x.Code}:{x.Message}"));

        await _auditService.AppendAsync(
            new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                actorId,
                $"module.lifecycle.{result.Action}",
                new ResourceDescriptor(ResourceTypeConstants.Module, ScopeKeyConstants.Instance),
                result.Succeeded ? "succeeded" : "blocked",
                DateTimeOffset.UtcNow,
                new CorrelationId(null),
                $"{details}; module={result.ModuleId}; state={result.State}; effect={result.Effect}; blockers={blockerSummary}",
                ModuleId: result.ModuleId),
            cancellationToken).ConfigureAwait(false);
    }
}
