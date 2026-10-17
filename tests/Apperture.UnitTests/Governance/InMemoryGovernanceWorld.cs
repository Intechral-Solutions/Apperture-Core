using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using Microsoft.Extensions.Options;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The production user administration service over in-memory storage, with the real evaluator for the check made before the
/// unit and the recording audited unit. The locked snapshot is the storage's own reads (there is nothing to lock in memory); the
/// SQL suites prove the locks. Used to show which rows the rules decide to record and, over random sequences, that they hold.
/// </summary>
internal sealed class InMemoryGovernanceWorld
{
    public static readonly PermissionDescriptor Read = Perm("connection.read", "Connection");
    public static readonly PermissionDescriptor Write = Perm("connection.update", "Connection");
    public static readonly PermissionDescriptor IntegrationRead = Perm("integration.read", "Integration");
    public static readonly PermissionDescriptor Schedule = Perm("schedule.manage", "Schedule");
    public static readonly PermissionDescriptor ModuleX = Perm("workflow.execute", "WorkflowInstance", "module:X");
    public static readonly PermissionDescriptor SlotOfX = Perm("workflow.execute", "WorkflowInstance", "module:X:slot:Y");
    public static readonly PermissionDescriptor ModuleY = Perm("workflow.execute", "WorkflowInstance", "module:Y");
    public static readonly PermissionDescriptor InstanceWorkflow = Perm("workflow.execute", "WorkflowInstance");
    public static readonly PermissionDescriptor RoleManage = Perm("role.manage", "Role");
    public static readonly PermissionDescriptor PermissionAssign = Perm("permission.assign", "Role");
    public static readonly PermissionDescriptor RoleAssign = Perm("role.assign", "Role");

    /// <summary>Every permission a test hands out; the property test measures an actor's holdings over this universe.</summary>
    public static readonly IReadOnlyList<PermissionDescriptor> Universe =
        [Read, Write, IntegrationRead, Schedule, ModuleX, SlotOfX, ModuleY, InstanceWorkflow, RoleManage, PermissionAssign, RoleAssign];

    public static PermissionDescriptor Perm(string action, string type, string scope = "instance") => new(action, new ResourceDescriptor(type, scope));

    /// <summary>The sensitivity each universe permission is registered with: the three authority permissions are Critical, writes Elevated, reads Standard.</summary>
    public static PermissionSensitivity SensitivityOf(PermissionDescriptor permission) => permission.Action switch
    {
        "role.manage" or "permission.assign" or "role.assign" => PermissionSensitivity.Critical,
        "connection.update" or "schedule.manage" or "workflow.execute" => PermissionSensitivity.Elevated,
        _ => PermissionSensitivity.Standard,
    };

    private readonly MemoryRoles _roles = new();
    private readonly MemoryUsers _users;
    private readonly ScopeContainmentEvaluator _scope = new();
    private string _actorId = "actor";

    public InMemoryGovernanceWorld()
    {
        _users = new MemoryUsers(_roles);
        var registry = new InMemoryResourceDescriptorRegistry();
        foreach (var permission in Universe)
            registry.RegisterPermission(TestPermissionDefinitions.For(permission, sensitivity: SensitivityOf(permission)));

        Audit = new AuditRecording();
        var principal = new SwitchablePrincipal(this);
        var advisory = new DelegatingAuthorizationService(
            new RbacAuthorizationPolicyEvaluator(new IdentityResolver(), new SqlRoleProvider(_roles), _scope));
        Service = new UserManagementService(
            principal,
            new IdentityResolver(),
            advisory,
            _users,
            _roles,
            registry,
            new NoCredentials(),
            new NoHasher(),
            Options.Create(new LocalAuthOptions()),
            auditedUnitOfWork: Audit,
            auditEvents: Audit);
    }

    public UserManagementService Service { get; }
    public AuditRecording Audit { get; }
    public Func<IActor> ActorFactory { get; set; } = () => new HumanActor(new ActorId("actor"), "actor");

    /// <summary>Runs after the roles were first read (by the check made before the unit), once: a change between that check and the decision.</summary>
    public Action? AfterFirstActorRolesRead
    {
        set => _roles.AfterFirstRead = value;
    }

    public IReadOnlyDictionary<string, Role> Roles => _roles.Rows;
    public bool Assigned(string userId, string roleId) => _roles.Assignments.Contains((userId, roleId));

    /// <summary>Acts as <paramref name="userId"/> from now on.</summary>
    public void ActAs(string userId)
    {
        _actorId = userId;
        ActorFactory = () => new HumanActor(new ActorId(userId), userId);
    }

    public string AddRole(string id, params PermissionDescriptor[] grants) => AddRole(id, system: false, readable: true, grants);

    public string AddRole(string id, bool system, bool readable, params PermissionDescriptor[] grants)
    {
        _roles.Rows[id] = new Role(new RoleId(id), id, null, readable ? grants : [], system, readable);
        return id;
    }

    public string AddUser(string id, params string[] roleIds)
    {
        _users.Rows[id] = new User(new UserId(id), id, [], [], IsBreakGlassEligible: false) { Username = id };
        foreach (var roleId in roleIds)
            _roles.Assignments.Add((id, roleId));
        return id;
    }

    /// <summary>A user the platform manages itself (the designated Recovery Access account).</summary>
    public string AddSystemManagedUser(string id, params string[] roleIds)
    {
        _users.Rows[id] = new User(
            new UserId(id),
            EmergencyAccessIdentity.DisplayName,
            [new IdentityLink(IdentitySource.Local, EmergencyAccessIdentity.LocalUsername, DateTimeOffset.UtcNow)],
            [],
            IsBreakGlassEligible: true) { Username = EmergencyAccessIdentity.LocalUsername };
        foreach (var roleId in roleIds)
            _roles.Assignments.Add((id, roleId));
        return id;
    }

    public void Unassign(string userId, string roleId) => _roles.Assignments.Remove((userId, roleId));

    /// <summary>The actor's holdings over <see cref="Universe"/>, by the one definition of "holds".</summary>
    public IReadOnlySet<string> Holdings(string userId)
    {
        var roles = _roles.Assignments.Where(a => a.User == userId).Select(a => _roles.Rows[a.Role]).ToList();
        var set = EffectivePermissionSet.FromRoles(roles, _scope);
        return Universe.Where(set.Holds).Select(PermissionSetChange.Identity).ToHashSet(StringComparer.Ordinal);
    }

    private sealed class SwitchablePrincipal(InMemoryGovernanceWorld world) : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(world.ActorFactory());
    }

    private sealed class IdentityResolver : IPrincipalToUserResolver
    {
        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<UserId?>(new UserId(actor.Id.Value));
    }

    private sealed class MemoryRoles : IRoleRepository
    {
        public Dictionary<string, Role> Rows { get; } = new(StringComparer.Ordinal);
        public HashSet<(string User, string Role)> Assignments { get; } = [];

        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Rows.TryGetValue(roleId.Value, out var role) ? role : null);

        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>(Rows.Values.ToList());

        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default)
        {
            var result = Assignments.Where(a => a.User == userId.Value).Select(a => Rows[a.Role]).ToList();
            var hook = AfterFirstRead;
            AfterFirstRead = null;
            hook?.Invoke();
            return ValueTask.FromResult<IReadOnlyList<Role>>(result);
        }

        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Assignments.Count(a => a.Role == roleId.Value));

        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
        {
            Assignments.Add((userId.Value, roleId.Value));
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
        {
            Assignments.Remove((userId.Value, roleId.Value));
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default)
        {
            Rows[roleId.Value] = Rows[roleId.Value] with { Permissions = permissions, PermissionsReadable = true };
            return ValueTask.CompletedTask;
        }

        public ValueTask CreateAsync(Role role, CancellationToken cancellationToken = default)
        {
            if (Rows.Values.Any(r => string.Equals(r.Name, role.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A role with this name already exists.");
            Rows[role.Id.Value] = role;
            return ValueTask.CompletedTask;
        }

        public ValueTask<RoleUnassignedDeleteResult> TryDeleteUnassignedAsync(RoleId roleId, CancellationToken cancellationToken = default)
        {
            if (!Rows.TryGetValue(roleId.Value, out var role))
                return ValueTask.FromResult(new RoleUnassignedDeleteResult(RoleUnassignedDeleteOutcome.NotFound));
            var count = Assignments.Count(a => a.Role == roleId.Value);
            if (count > 0)
                return ValueTask.FromResult(new RoleUnassignedDeleteResult(RoleUnassignedDeleteOutcome.AssignmentsRemain, count, role.Name));
            Rows.Remove(roleId.Value);
            return ValueTask.FromResult(new RoleUnassignedDeleteResult(RoleUnassignedDeleteOutcome.Deleted, 0, role.Name));
        }

        /// <summary>Runs once, after the first read of any user's roles (the check made before the unit): a change in between.</summary>
        public Action? AfterFirstRead { get; set; }
    }

    private sealed class MemoryUsers(MemoryRoles roles) : IUserRepository
    {
        public Dictionary<string, User> Rows { get; } = new(StringComparer.Ordinal);

        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default)
        {
            if (!Rows.TryGetValue(id.Value, out var user))
                return ValueTask.FromResult<User?>(null);
            var assigned = roles.Assignments.Where(a => a.User == id.Value).Select(a => new RoleId(a.Role)).ToList();
            return ValueTask.FromResult<User?>(user with { AssignedRoleIds = assigned });
        }

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Rows.Count);
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(null);
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<(IReadOnlyList<User>, int)>((Rows.Values.Skip(skip).Take(take).ToList(), Rows.Count));
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class NoCredentials : ILocalCredentialRepository
    {
        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<LocalCredentialData?>(null);
        public ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class NoHasher : ILocalPasswordHasher
    {
        public string AlgorithmName => "stub";
        public (string Hash, string Salt) HashPassword(string password) => ("h", "s");
        public bool VerifyPassword(string password, string hash, string salt) => false;
    }
}
