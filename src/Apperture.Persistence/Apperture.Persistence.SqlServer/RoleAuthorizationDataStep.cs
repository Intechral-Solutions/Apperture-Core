using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// The startup data step that goes with the removal of the <c>role.manage</c> implication and the three permission switches
/// (IAM-10 gate P-32, P-33, P-17, P-18, P-19). It runs on every start, after the migrations, in one audited transaction: it reads
/// every role under <c>UPDLOCK, HOLDLOCK</c>, plans the changes below from the rows as locked, writes them, and stages one
/// summary audit row. A start that finds nothing to change writes nothing, so a second start (or a second host starting at the
/// same time) is a silent no-op. A failure anywhere rolls the whole step back and fails the host start.
/// </summary>
/// <remarks>
/// For each custom (non-system) role, in this order, over its stored grants:
/// <list type="number">
/// <item>A grant of <c>role.manage</c> on Role at instance scope used to imply eight catalog permissions. The role is given
/// each of the eight it does not already hold as an explicit instance-scope grant, so that what the role can do is unchanged
/// once the implication is gone. Nothing is added for <c>user.read</c> on Role (its three consumers check <c>user.read</c> on User
/// now) or for <c>role.assign</c> on User (nothing asks for it).</item>
/// <item>A holder of <c>schedule.manage</c> on Schedule at instance scope is given <c>operational_team.manage</c>.</item>
/// <item>A holder of <c>auth.configuration.read</c> or <c>auth.entra.manage</c> on InstanceConfiguration at instance scope
/// (including the ones step 1 just gave) is given <c>auth.directory.search</c>; a holder of <c>connection.read</c> on Connection
/// at instance scope is given <c>connection.verify</c>.</item>
/// <item>The obsolete <c>user.read</c> on Role is removed.</item>
/// </list>
/// A role "holds" a grant when it stores a grant with the same action and resource type (compared ignoring case) and the instance
/// scope. A role whose stored permissions cannot be read is listed and left exactly as it is: it contributes no grants to any
/// check, so there is nothing to preserve. A grant with no resource (a broad grant) is never changed; its role is listed in the
/// summary so the operator can review it, and the role still receives what the steps above give it. System roles are never
/// touched here: the Platform Administrator role is repaired by the administrator repair at startup.
/// </remarks>
public static class RoleAuthorizationDataStep
{
    private static readonly PermissionDescriptor RoleManageOnRole = Grant(ActionConstants.RoleManage, ResourceTypeConstants.Role);

    /// <summary>The eight catalog permissions the removed implication gave, as explicit grants (IAM-10 gate section 2.2, rows 2 to 8 and 10).</summary>
    public static readonly IReadOnlyList<PermissionDescriptor> PreservedImpliedGrants =
    [
        Grant(ActionConstants.PermissionAssign, ResourceTypeConstants.Role),
        Grant(ActionConstants.RoleAssign, ResourceTypeConstants.Role),
        Grant(ActionConstants.AuthConfigurationRead, ResourceTypeConstants.InstanceConfiguration),
        Grant(ActionConstants.AuthUiPolicyManage, ResourceTypeConstants.InstanceConfiguration),
        Grant(ActionConstants.AuthEntraManage, ResourceTypeConstants.InstanceConfiguration),
        Grant(ActionConstants.UserRead, ResourceTypeConstants.User),
        Grant(ActionConstants.UserManage, ResourceTypeConstants.User),
        Grant(ActionConstants.CredentialManage, ResourceTypeConstants.User),
    ];

    private static readonly PermissionDescriptor ScheduleManageOnSchedule = Grant(ActionConstants.ScheduleManage, ResourceTypeConstants.Schedule);
    private static readonly PermissionDescriptor OperationalTeamManage = Grant(ActionConstants.OperationalTeamManage, ResourceTypeConstants.OperationalTeam);
    private static readonly PermissionDescriptor AuthDirectorySearch = Grant(ActionConstants.AuthDirectorySearch, ResourceTypeConstants.InstanceConfiguration);
    private static readonly PermissionDescriptor ConnectionReadOnConnection = Grant(ActionConstants.ConnectionRead, ResourceTypeConstants.Connection);
    private static readonly PermissionDescriptor ConnectionVerify = Grant(ActionConstants.ConnectionVerify, ResourceTypeConstants.Connection);

    private static readonly PermissionDescriptor AuthConfigurationReadHolder = Grant(ActionConstants.AuthConfigurationRead, ResourceTypeConstants.InstanceConfiguration);
    private static readonly PermissionDescriptor AuthEntraManageHolder = Grant(ActionConstants.AuthEntraManage, ResourceTypeConstants.InstanceConfiguration);

    /// <summary>The grant no check asks for any more. Removed from custom roles wherever it is found, at any scope.</summary>
    private static readonly PermissionDescriptor ObsoleteUserReadOnRole = Grant(ActionConstants.UserRead, ResourceTypeConstants.Role);

    private static PermissionDescriptor Grant(string action, string resourceType) =>
        new(action, new ResourceDescriptor(resourceType, ScopeKeyConstants.Instance));

    /// <summary>What the step decided for one role.</summary>
    public sealed record RoleGrantChange(
        string RoleId,
        string PermissionsJson,
        IReadOnlyList<PermissionDescriptor> Added,
        IReadOnlyList<PermissionDescriptor> Removed);

    /// <summary>What the step would do to a set of roles.</summary>
    public sealed record Plan(
        IReadOnlyList<RoleGrantChange> Changes,
        IReadOnlyList<string> UnreadableRoleIds,
        IReadOnlyList<string> BroadGrantRoleIds)
    {
        public bool IsEmpty => Changes.Count == 0;
        public int GrantsAdded => Changes.Sum(c => c.Added.Count);
        public int GrantsRemoved => Changes.Sum(c => c.Removed.Count);
    }

    /// <summary>The outcome of one run.</summary>
    /// <param name="Committed">True when the run changed roles and committed them with their summary row.</param>
    public sealed record Report(
        bool Committed,
        int RolesChanged,
        int GrantsAdded,
        int GrantsRemoved,
        IReadOnlyList<string> UnreadableRoleIds,
        IReadOnlyList<string> BroadGrantRoleIds)
    {
        public static Report Nothing { get; } = new(false, 0, 0, 0, [], []);
    }

    /// <summary>Decides the changes for <paramref name="roles"/> (id, system flag, stored permissions JSON). Pure: reads and writes nothing.</summary>
    public static Plan PlanChanges(IEnumerable<(string Id, bool IsSystemRole, string? PermissionsJson)> roles)
    {
        var changes = new List<RoleGrantChange>();
        var unreadable = new List<string>();
        var broad = new List<string>();

        foreach (var (id, isSystemRole, json) in roles.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (isSystemRole)
                continue;

            var reading = StoredRolePermissions.Read(json);
            if (!reading.IsReadable)
            {
                unreadable.Add(id);
                continue;
            }

            var grants = reading.Permissions.ToList();
            if (grants.Any(g => g.Resource is null))
                broad.Add(id);

            var added = new List<PermissionDescriptor>();

            // 1. The grants the removed implication gave.
            if (grants.Any(g => Holds(g, RoleManageOnRole)))
            {
                foreach (var implied in PreservedImpliedGrants)
                    AddIfMissing(grants, added, implied);
            }

            // 2. Team management, which no longer rides on schedule.manage.
            if (grants.Any(g => Holds(g, ScheduleManageOnSchedule)))
                AddIfMissing(grants, added, OperationalTeamManage);

            // 3. Directory search and test-connect, for the roles that could do them. Step 1 may just have given the first holders.
            if (grants.Any(g => Holds(g, AuthConfigurationReadHolder) || Holds(g, AuthEntraManageHolder)))
                AddIfMissing(grants, added, AuthDirectorySearch);
            if (grants.Any(g => Holds(g, ConnectionReadOnConnection)))
                AddIfMissing(grants, added, ConnectionVerify);

            // 4. The grant nothing checks any more.
            var removed = grants.Where(IsObsolete).ToList();
            grants.RemoveAll(IsObsolete);

            if (added.Count == 0 && removed.Count == 0)
                continue;

            changes.Add(new RoleGrantChange(id, StoredRolePermissions.Serialize(grants), added, removed));
        }

        return new Plan(changes, unreadable, broad);
    }

    /// <summary>A stored grant holds a combination when action and resource type match (ignoring case) at exactly the instance scope.</summary>
    private static bool Holds(PermissionDescriptor stored, PermissionDescriptor wanted) =>
        stored.Resource is not null
        && string.Equals(stored.Action, wanted.Action, StringComparison.OrdinalIgnoreCase)
        && string.Equals(stored.Resource.ResourceType, wanted.Resource!.ResourceType, StringComparison.OrdinalIgnoreCase)
        && string.Equals(stored.Resource.ScopeKey, ScopeKeyConstants.Instance, StringComparison.Ordinal);

    private static bool IsObsolete(PermissionDescriptor stored) =>
        stored.Resource is not null
        && string.Equals(stored.Action, ObsoleteUserReadOnRole.Action, StringComparison.OrdinalIgnoreCase)
        && string.Equals(stored.Resource.ResourceType, ObsoleteUserReadOnRole.Resource!.ResourceType, StringComparison.OrdinalIgnoreCase);

    private static void AddIfMissing(List<PermissionDescriptor> grants, List<PermissionDescriptor> added, PermissionDescriptor wanted)
    {
        if (grants.Any(g => Holds(g, wanted)))
            return;
        grants.Add(wanted);
        added.Add(wanted);
    }

    /// <summary>
    /// Runs the step against <paramref name="db"/>. Idempotent and safe to run from several hosts at once: the role rows are read
    /// under update locks, so the second run sees the first run's result and changes nothing. Throws if it cannot complete.
    /// </summary>
    public static async Task<Report> RunAsync(AppertureDbContext db, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        logger ??= NullLogger.Instance;
        var unit = new SqlAuditedUnitOfWork(db);

        var report = await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.AuthorizationGrantsMigrated, AuditActor.System("data-step")),
            async (transaction, ct) =>
            {
                var rows = await db.Roles
                    .FromSqlRaw("SELECT * FROM [Roles] WITH (UPDLOCK, HOLDLOCK)")
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var plan = PlanChanges(rows.Select(r => (r.Id, r.IsSystemRole, (string?)r.PermissionsJson)));
                if (plan.IsEmpty)
                {
                    transaction.DeclareNoOp();
                    return Report.Nothing with { UnreadableRoleIds = plan.UnreadableRoleIds, BroadGrantRoleIds = plan.BroadGrantRoleIds };
                }

                foreach (var change in plan.Changes)
                    rows.Single(r => string.Equals(r.Id, change.RoleId, StringComparison.Ordinal)).PermissionsJson = change.PermissionsJson;

                var changes = plan.Changes
                    .SelectMany(c => c.Added.Select(p => $"{c.RoleId}|+{PermissionSetChange.Identity(p)}")
                        .Concat(c.Removed.Select(p => $"{c.RoleId}|-{PermissionSetChange.Identity(p)}")))
                    .ToList();

                await transaction.StageAsync(
                    AuditEvents.AuthorizationGrantsMigrated(
                        plan.Changes.Count,
                        plan.GrantsAdded,
                        plan.GrantsRemoved,
                        plan.Changes.Select(c => c.RoleId).ToList(),
                        changes,
                        plan.UnreadableRoleIds,
                        plan.BroadGrantRoleIds),
                    ct).ConfigureAwait(false);

                return new Report(true, plan.Changes.Count, plan.GrantsAdded, plan.GrantsRemoved, plan.UnreadableRoleIds, plan.BroadGrantRoleIds);
            },
            // A commit whose acknowledgement was lost and was verified: the step ran. Its details are in the committed row.
            (committed, ct) => Task.FromResult(Report.Nothing with { Committed = true }),
            cancellationToken).ConfigureAwait(false);

        if (report.Committed)
        {
            logger.LogInformation(
                "Authorization data step changed {Roles} role(s): {Added} grant(s) added, {Removed} removed. {Unreadable} role(s) with unreadable permissions and {Broad} with broad grants were left unchanged.",
                report.RolesChanged,
                report.GrantsAdded,
                report.GrantsRemoved,
                report.UnreadableRoleIds.Count,
                report.BroadGrantRoleIds.Count);
        }

        return report;
    }
}
