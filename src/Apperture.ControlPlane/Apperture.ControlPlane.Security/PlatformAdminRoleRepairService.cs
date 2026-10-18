using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Startup repair of the Platform Administrator role (IAM-10 gate P-5, P-7, P-30(d); IAM-10.2a slice 4). It runs after the
/// platform catalog is seeded and the module permissions are registered, so the registry it reads is complete, and before the
/// first administrator is assigned. It makes the stored permissions of the role equal what
/// <see cref="PlatformAdminRolePermissionRepair.Plan"/> says: every administrator default of the catalog, plus the fixed grants,
/// minus the approved obsolete ones.
/// </summary>
/// <remarks>
/// <para>It is one audited unit of work. The role row is read and locked through the same authority snapshot an assignment of that
/// role takes (<see cref="IRoleRepository.LockAuthoritySnapshotAsync"/>, <c>UPDLOCK, HOLDLOCK</c>), and the change is decided from
/// the row as locked, so the role cannot change between the decision and the commit, under either isolation mode. An assignment of
/// the administrator role in flight and the repair therefore serialize in either order. The stored set and its single summary row
/// commit together; a failed audit insert rolls the repair back, and a start that finds the role already correct writes nothing
/// at all, so repeated and concurrent starts are quiet.</para>
/// <para>The repair is a named exempt system writer of the role's permissions (the system actor <c>administrator-repair</c>, never
/// an ordinary actor): the anti-escalation rules constrain the people who change roles, not the platform keeping its own
/// administrator role current. It does not take the administrator-set lock, which belongs to the viability invariants.</para>
/// <para>Stored permissions that cannot be read are never overwritten. There is no approved recovery path for replacing data the
/// repair cannot interpret, so the repair stops with <see cref="PlatformAdminRoleRepairException"/> and the host does not start.
/// Every other failure is likewise not caught: a repair that did not complete must fail the start, not leave the role
/// inconsistent.</para>
/// </remarks>
public sealed class PlatformAdminRoleRepairService : IHostedService
{
    /// <summary>The system actor the audit row records: the platform acting on its own role.</summary>
    internal static readonly AuditActor RepairActor = AuditActor.System("administrator-repair");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlatformAdminRoleRepairService>? _logger;

    public PlatformAdminRoleRepairService(IServiceScopeFactory scopeFactory, ILogger<PlatformAdminRoleRepairService>? logger = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var unit = scope.ServiceProvider.GetRequiredService<IAuditedUnitOfWork>();
        var roleRepository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        var registry = scope.ServiceProvider.GetRequiredService<IResourceDescriptorRegistry>();

        PlatformAdminRoleRepairResult result;
        try
        {
            result = await RepairAsync(unit, roleRepository, registry, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogError(
                ex,
                "The Platform Administrator role could not be repaired; the host will not start. Nothing was changed by the failed attempt. Correct the cause and start it again.");
            throw;
        }

        if (result.Outcome == PlatformAdminRoleRepairOutcome.Repaired)
        {
            _logger?.LogInformation(
                "Platform Administrator role repaired: {Added} permission(s) added, {Removed} removed.",
                result.PermissionsAdded,
                result.PermissionsRemoved);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Repairs the role in one audited unit. Idempotent: it changes and records something only when the stored set differs from
    /// the plan, decided from the locked row.
    /// </summary>
    public static Task<PlatformAdminRoleRepairResult> RepairAsync(
        IAuditedUnitOfWork unit,
        IRoleRepository roles,
        IResourceDescriptorRegistry registry,
        CancellationToken cancellationToken = default)
    {
        var roleId = new RoleId(BootstrapConstants.PlatformAdminRoleId);

        return unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.AdministratorRoleRepaired, RepairActor),
            async (transaction, ct) =>
            {
                // The same lock an assignment of this role takes on the role row: the only way to read it here.
                var snapshot = await roles.LockAuthoritySnapshotAsync(actorUserId: null, roleId, targetUserId: null, ct).ConfigureAwait(false);
                var role = snapshot.TargetRole;
                if (role is null)
                {
                    transaction.DeclareNoOp();
                    return PlatformAdminRoleRepairResult.RoleMissing;
                }

                if (!role.PermissionsReadable)
                    throw PlatformAdminRoleRepairException.Unreadable(roleId);

                var plan = PlatformAdminRolePermissionRepair.Plan(role.Permissions, registry);
                if (plan.IsNoOp)
                {
                    transaction.DeclareNoOp();
                    return PlatformAdminRoleRepairResult.NoChange;
                }

                await roles.UpdatePermissionsAsync(roleId, plan.Target, ct).ConfigureAwait(false);
                await transaction.StageAsync(
                    AuditEvents.AdministratorRoleRepaired(roleId, new PermissionSetChange(plan.Added, plan.Removed), plan.Target.Count),
                    ct).ConfigureAwait(false);
                return new PlatformAdminRoleRepairResult(PlatformAdminRoleRepairOutcome.Repaired, plan.Added.Count, plan.Removed.Count);
            },
            // A commit whose acknowledgement was lost and was verified: the repair happened, and the committed row has the counts.
            (committed, ct) => Task.FromResult(new PlatformAdminRoleRepairResult(
                PlatformAdminRoleRepairOutcome.Repaired,
                committed.GetCount("grantsAdded") ?? 0,
                committed.GetCount("grantsRemoved") ?? 0)),
            cancellationToken);
    }
}

/// <summary>What one repair did.</summary>
public enum PlatformAdminRoleRepairOutcome
{
    /// <summary>The role does not exist (nothing to repair).</summary>
    RoleMissing,

    /// <summary>The stored set already equals the plan: nothing was written.</summary>
    NoChange,

    /// <summary>The stored set changed and one summary row was committed with it.</summary>
    Repaired,
}

/// <summary>The outcome of one repair and the exact counts of what it changed.</summary>
public sealed record PlatformAdminRoleRepairResult(PlatformAdminRoleRepairOutcome Outcome, int PermissionsAdded, int PermissionsRemoved)
{
    public static PlatformAdminRoleRepairResult RoleMissing { get; } = new(PlatformAdminRoleRepairOutcome.RoleMissing, 0, 0);

    public static PlatformAdminRoleRepairResult NoChange { get; } = new(PlatformAdminRoleRepairOutcome.NoChange, 0, 0);
}

/// <summary>
/// The repair cannot complete and the host must not start. Not an <see cref="InvalidOperationException"/>: it is neither a
/// business refusal nor an audit programming error.
/// </summary>
public sealed class PlatformAdminRoleRepairException : Exception
{
    /// <summary>The stored permissions of the administrator role could not be read, so the repair will not replace them.</summary>
    public const string UnreadablePermissionsCode = "platform_administrator_role_permissions_unreadable";

    private PlatformAdminRoleRepairException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }

    internal static PlatformAdminRoleRepairException Unreadable(RoleId role) =>
        new(
            UnreadablePermissionsCode,
            $"The stored permissions of the role '{role.Value}' could not be read, so the startup repair will not replace them: "
            + "replacing data it cannot interpret would discard it silently. Restore the role's permissions from a backup, "
            + "or correct the stored value, and start the host again.");
}
