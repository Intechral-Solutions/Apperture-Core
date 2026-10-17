using System.Reflection;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The generated inputs of the differential equivalence tests: role shapes, the permissions that are asked for, the
/// ways a target scope can be supplied, and the actors. Everything is written out as literals on purpose, so the
/// inputs do not move when a production constant does.
/// </summary>
internal static class AuthorizationEquivalenceFixtures
{
    public const string AdminRoleId = "role-platform-administrator";
    public const string UserKey = "user-1";

    /// <summary>
    /// The ten resource and action combinations of the Platform Administrator floor, as (action, resource type):
    /// rows 1 to 10 of the IAM-10.0 gate section 2.2.
    /// </summary>
    public static readonly IReadOnlyList<(string Action, string ResourceType)> FloorCombinations =
    [
        ("user.read", "Role"),
        ("permission.assign", "Role"),
        ("role.assign", "Role"),
        ("auth.configuration.read", "InstanceConfiguration"),
        ("auth.ui_policy.manage", "InstanceConfiguration"),
        ("auth.entra.manage", "InstanceConfiguration"),
        ("user.read", "User"),
        ("user.manage", "User"),
        ("role.assign", "User"),
        ("credential.manage", "User"),
    ];

    private static readonly string[] ScopeKeys =
    [
        "instance", "module:X", "module:X:slot:Y", "module:Y", "module:XX", "connection:abc", "Instance", "",
    ];

    // ---------------------------------------------------------------------------------------------------------------
    // Grants (what a role can store)
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>The stored grants roles are built from, including shapes the API cannot create but a database can hold.</summary>
    public static readonly IReadOnlyList<PermissionDescriptor> GrantPool = BuildGrantPool();

    private static List<PermissionDescriptor> BuildGrantPool()
    {
        var grants = new List<PermissionDescriptor>();

        foreach (var (action, type) in FloorCombinations)
        {
            foreach (var scope in new[] { "instance", "module:X", "module:X:slot:Y", "module:Y" })
                grants.Add(new PermissionDescriptor(action, new ResourceDescriptor(type, scope)));

            // A grant with no resource matches any resource type at instance scope.
            grants.Add(new PermissionDescriptor(action));
            // Case differences: the action and the type compare ignoring case.
            grants.Add(new PermissionDescriptor(action.ToUpperInvariant(), new ResourceDescriptor(type.ToLowerInvariant(), "instance")));
            // The same action on a type that is not the one asked for.
            grants.Add(new PermissionDescriptor(action, new ResourceDescriptor("Connection", "instance")));
        }

        // The grant the implication is keyed on, and every way of missing it.
        grants.Add(new PermissionDescriptor("role.manage", new ResourceDescriptor("Role", "instance")));
        grants.Add(new PermissionDescriptor("ROLE.MANAGE", new ResourceDescriptor("ROLE", "instance")));
        grants.Add(new PermissionDescriptor("role.manage", new ResourceDescriptor("Role", "module:X")));
        grants.Add(new PermissionDescriptor("role.manage", new ResourceDescriptor("Role", "module:X:slot:Y")));
        grants.Add(new PermissionDescriptor("role.manage", new ResourceDescriptor("User", "instance")));
        grants.Add(new PermissionDescriptor("role.manage", new ResourceDescriptor("InstanceConfiguration", "instance")));
        grants.Add(new PermissionDescriptor("role.manage"));
        grants.Add(new PermissionDescriptor("role.manage", new ResourceDescriptor("Role", "Instance")));

        // Unrelated grants, so a role is not always about governance.
        grants.Add(new PermissionDescriptor("connection.read", new ResourceDescriptor("Connection", "instance")));
        grants.Add(new PermissionDescriptor("connection.read", new ResourceDescriptor("Connection", "connection:abc")));
        grants.Add(new PermissionDescriptor("workflow.execute", new ResourceDescriptor("WorkflowInstance", "instance")));
        grants.Add(new PermissionDescriptor("workflow.execute", new ResourceDescriptor("WorkflowInstance", "module:X")));
        grants.Add(new PermissionDescriptor("workflow.execute", new ResourceDescriptor("WorkflowInstance", "module:X:slot:Y")));
        grants.Add(new PermissionDescriptor("workflow.execute"));

        // Shapes a hand-edited or damaged database could hold. None of these can be created through the API.
        grants.Add(new PermissionDescriptor(null!, new ResourceDescriptor("Role", "instance")));
        grants.Add(new PermissionDescriptor("", new ResourceDescriptor("Role", "instance")));
        grants.Add(new PermissionDescriptor("user.read", new ResourceDescriptor("Role", null!)));
        grants.Add(new PermissionDescriptor("user.read", new ResourceDescriptor("Role", "")));
        grants.Add(new PermissionDescriptor("user.read", new ResourceDescriptor(null!, "instance")));
        grants.Add(new PermissionDescriptor("user.read", new ResourceDescriptor("", "instance")));
        grants.Add(new PermissionDescriptor("role.manage", new ResourceDescriptor("Role", null!)));
        grants.Add(new PermissionDescriptor("role.manage", new ResourceDescriptor(null!, "instance")));

        return grants;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Roles
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>One role as stored: an id and either already-read grants or the JSON the repository would read them from.</summary>
    internal sealed record RoleSpec(string Name, string Id, IReadOnlyList<PermissionDescriptor>? Grants, string? Json = null)
    {
        /// <summary>The role as the repository hands it out, or the exception the repository would throw while reading it.</summary>
        public Role? TryBuild(out Exception? loaderFailure)
        {
            loaderFailure = null;
            if (Json is null)
                return new Role(new RoleId(Id), Name, null, Grants!, false);

            try
            {
                return new Role(new RoleId(Id), Name, null, ReadLikeTheRepository(Json), false);
            }
            catch (Exception ex)
            {
                loaderFailure = ex;
                return null;
            }
        }
    }

    private static readonly MethodInfo RepositoryParser =
        typeof(SqlRoleRepository).GetMethod("DeserializePermissions", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("SqlRoleRepository.DeserializePermissions was not found; update the equivalence fixtures.");

    /// <summary>Reads stored role JSON with the repository's own parser, so that malformed data is read the way it really is.</summary>
    public static IReadOnlyList<PermissionDescriptor> ReadLikeTheRepository(string json)
    {
        try
        {
            return (IReadOnlyList<PermissionDescriptor>)RepositoryParser.Invoke(null, [json])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    /// <summary>Stored role JSON as found in a database, readable and not.</summary>
    public static readonly IReadOnlyList<(string Name, string Json)> StoredJson =
    [
        ("json-empty-string", ""),
        ("json-whitespace", "   "),
        ("json-empty-array", "[]"),
        ("json-not-json", "{not json"),
        ("json-wrong-shape", "{\"action\":\"user.read\"}"),
        ("json-json-null", "null"),
        ("json-array-of-scalars", "[1,2]"),
        ("json-null-element", "[null]"),
        ("json-null-action", "[{\"action\":null,\"resource\":{\"resourceType\":\"Role\",\"scopeKey\":\"instance\"}}]"),
        ("json-no-action", "[{\"resource\":{\"resourceType\":\"Role\",\"scopeKey\":\"instance\"}}]"),
        ("json-no-resource", "[{\"action\":\"user.read\"}]"),
        ("json-null-resource", "[{\"action\":\"user.read\",\"resource\":null}]"),
        ("json-null-scope", "[{\"action\":\"user.read\",\"resource\":{\"resourceType\":\"Role\",\"scopeKey\":null}}]"),
        ("json-null-type", "[{\"action\":\"user.read\",\"resource\":{\"resourceType\":null,\"scopeKey\":\"instance\"}}]"),
        ("json-role-manage", "[{\"action\":\"role.manage\",\"resource\":{\"resourceType\":\"Role\",\"scopeKey\":\"instance\"}}]"),
        ("json-pascal-case", "[{\"Action\":\"user.read\",\"Resource\":{\"ResourceType\":\"User\",\"ScopeKey\":\"instance\"}}]"),
        ("json-readable-then-null", "[{\"action\":\"connection.read\",\"resource\":{\"resourceType\":\"Connection\",\"scopeKey\":\"instance\"}},null]"),
        ("json-null-then-readable", "[null,{\"action\":\"user.read\",\"resource\":{\"resourceType\":\"Role\",\"scopeKey\":\"instance\"}}]"),
        ("json-two-grants", "[{\"action\":\"user.read\",\"resource\":{\"resourceType\":\"User\",\"scopeKey\":\"instance\"}},{\"action\":\"user.manage\",\"resource\":{\"resourceType\":\"User\",\"scopeKey\":\"module:X\"}}]"),
    ];

    private static readonly string[] RoleIds =
    [
        AdminRoleId,
        "ROLE-PLATFORM-ADMINISTRATOR",
        "Role-Platform-Administrator",
        "role-platform-administrator-2",
        " role-platform-administrator",
        "role-platform-admin",
        "role-custom-a",
        "role-custom-b",
        "",
    ];

    /// <summary>
    /// The single-role shapes of the exhaustive part: each role id that matters paired with each kind of stored content.
    /// </summary>
    public static IEnumerable<RoleSpec> SingleRoleShapes()
    {
        var contents = new List<(string Label, IReadOnlyList<PermissionDescriptor> Grants)>
        {
            ("no-grants", Array.Empty<PermissionDescriptor>()),
            ("role-manage-instance", [Find("role.manage", "Role", "instance")]),
            ("role-manage-module", [Find("role.manage", "Role", "module:X")]),
            ("role-manage-other-type", [Find("role.manage", "User", "instance")]),
            ("role-manage-no-resource", [Find("role.manage", null, null)]),
            ("role-manage-upper", [Find("ROLE.MANAGE", "ROLE", "instance")]),
            ("all-ten-instance", FloorCombinations.Select(c => Find(c.Action, c.ResourceType, "instance")).ToList()),
            ("all-ten-module-x", FloorCombinations.Select(c => Find(c.Action, c.ResourceType, "module:X")).ToList()),
            ("all-ten-no-resource", FloorCombinations.Select(c => Find(c.Action, null, null)).ToList()),
            ("all-ten-upper", FloorCombinations.Select(c => Find(c.Action.ToUpperInvariant(), c.ResourceType.ToLowerInvariant(), "instance")).ToList()),
            ("unrelated", [Find("connection.read", "Connection", "instance"), Find("workflow.execute", "WorkflowInstance", "instance")]),
            ("workflow-module", [Find("workflow.execute", "WorkflowInstance", "module:X")]),
            ("damaged-grants", GrantPool.Where(IsDamaged).ToList()),
            ("duplicated", [Find("user.read", "User", "instance"), Find("user.read", "User", "instance"), Find("role.manage", "Role", "instance"), Find("role.manage", "Role", "instance")]),
            ("everything", GrantPool),
        };

        foreach (var id in RoleIds)
            foreach (var (label, grants) in contents)
                yield return new RoleSpec($"{(id.Length == 0 ? "<empty-id>" : id)}/{label}", id, grants);

        foreach (var id in new[] { AdminRoleId, "role-custom-a" })
            foreach (var (label, json) in StoredJson)
                yield return new RoleSpec($"{id}/{label}", id, null, json);
    }

    private static bool IsDamaged(PermissionDescriptor grant) =>
        grant.Action is null or "" || grant.Resource is { ScopeKey: null or "" } || grant.Resource is { ResourceType: null or "" };

    private static PermissionDescriptor Find(string action, string? type, string? scope) =>
        GrantPool.FirstOrDefault(g => g.Action == action && g.Resource?.ResourceType == type && g.Resource?.ScopeKey == scope)
        ?? (type is null ? new PermissionDescriptor(action) : new PermissionDescriptor(action, new ResourceDescriptor(type, scope!)));

    // ---------------------------------------------------------------------------------------------------------------
    // Questions (what is asked for, and against which target)
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>The permissions a check can ask for.</summary>
    public static IReadOnlyList<PermissionDescriptor> RequiredPermissions { get; } = BuildRequiredPermissions();

    private static List<PermissionDescriptor> BuildRequiredPermissions()
    {
        var required = new List<PermissionDescriptor>();
        var actions = FloorCombinations.Select(c => c.Action).Concat(["role.manage"]).Distinct().ToList();
        var types = new[] { "User", "Role", "InstanceConfiguration", "Connection", "user", "ROLE" };

        foreach (var action in actions)
        {
            required.Add(new PermissionDescriptor(action));
            required.Add(new PermissionDescriptor(action.ToUpperInvariant(), new ResourceDescriptor("User", "instance")));
            foreach (var type in types)
                required.Add(new PermissionDescriptor(action, new ResourceDescriptor(type, "instance")));
        }

        // The scope the permission names is irrelevant to the target: ask with several.
        foreach (var scope in new[] { "module:X", "module:Y", "connection:abc", "" })
            required.Add(new PermissionDescriptor("user.read", new ResourceDescriptor("User", scope)));

        required.Add(new PermissionDescriptor("connection.read", new ResourceDescriptor("Connection", "instance")));
        required.Add(new PermissionDescriptor("workflow.execute", new ResourceDescriptor("WorkflowInstance", "instance")));
        required.Add(new PermissionDescriptor("workflow.execute", new ResourceDescriptor("WorkflowInstance", "module:X")));
        required.Add(new PermissionDescriptor("workflow.execute"));
        required.Add(new PermissionDescriptor(null!, new ResourceDescriptor("User", "instance")));
        required.Add(new PermissionDescriptor("", new ResourceDescriptor("User", "instance")));
        required.Add(new PermissionDescriptor("user.read", new ResourceDescriptor(null!, "instance")));

        // Every check production source actually makes (the corpus), so a new check widens the inputs by being added there.
        required.AddRange(ProductionAuthorizationCheckCorpus.Permissions());
        return required.Distinct().ToList();
    }

    /// <summary>A way of supplying the target scope: the <c>resource</c> argument, the <c>resourceIdentity</c> argument, both, or neither.</summary>
    internal sealed record TargetSpec(string Name, ResourceDescriptor? Resource, GovernedIdentity? Identity);

    internal sealed record GovernedIdentity(string? ScopeKey) : IGovernedResourceIdentity
    {
        public string ResourceType => "Role";
        public string ResourceId => "r-1";
        public string? OwnerId => null;
    }

    public static IReadOnlyList<TargetSpec> Targets { get; } = BuildTargets();

    private static List<TargetSpec> BuildTargets()
    {
        var targets = new List<TargetSpec> { new("neither", null, null) };
        foreach (var scope in ScopeKeys)
        {
            targets.Add(new($"resource:{scope}", new ResourceDescriptor("Role", scope), null));
            targets.Add(new($"identity:{scope}", null, new GovernedIdentity(scope)));
        }

        targets.Add(new("identity:null", null, new GovernedIdentity(null)));
        targets.Add(new("resource:module:Y+identity:module:X", new ResourceDescriptor("Role", "module:Y"), new GovernedIdentity("module:X")));
        targets.Add(new("resource:module:X+identity:null", new ResourceDescriptor("Role", "module:X"), new GovernedIdentity(null)));
        targets.Add(new("resource:instance+identity:module:X", new ResourceDescriptor("Role", "instance"), new GovernedIdentity("module:X")));
        targets.Add(new("resource:null-scope", new ResourceDescriptor("Role", null!), null));
        return targets;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Actors
    // ---------------------------------------------------------------------------------------------------------------

    internal sealed class OtherActor(ActorKind kind) : IActor
    {
        public ActorId Id { get; } = new("someone");
        public ActorKind Kind { get; } = kind;
        public string? DisplayName => null;
    }

    /// <summary>An actor and whether the identity resolver resolves it to a user.</summary>
    internal sealed record ActorSpec(string Name, IActor? Actor, bool Resolves)
    {
        public bool IsUnmarkedResolvedHuman => Actor is { Kind: ActorKind.Human } and not HumanActor { IsBreakGlassSession: true } && Resolves;
    }

    public static IReadOnlyList<ActorSpec> Actors { get; } =
    [
        new("null", null, false),
        new("human-resolved", new HumanActor(new ActorId(UserKey), "H"), true),
        new("human-unresolved", new HumanActor(new ActorId(UserKey), "H"), false),
        new("human-other-implementation-resolved", new OtherActor(ActorKind.Human), true),
        new("human-other-implementation-unresolved", new OtherActor(ActorKind.Human), false),
        new("marker-resolved", new HumanActor(new ActorId(UserKey), "H", isBreakGlassSession: true), true),
        new("marker-unresolved", new HumanActor(new ActorId(UserKey), "H", isBreakGlassSession: true), false),
        new("machine-resolver-would-resolve", new OtherActor(ActorKind.Machine), true),
        new("system-resolver-would-resolve", new OtherActor(ActorKind.System), true),
        new("remote-instance-resolver-would-resolve", new OtherActor(ActorKind.RemoteInstance), true),
    ];

    // ---------------------------------------------------------------------------------------------------------------
    // Multi-role generation
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>A reproducible random role set (0 to 4 roles) of mixed ids and contents, including stored JSON.</summary>
    public static IReadOnlyList<RoleSpec> RandomRoles(Random random)
    {
        var count = random.Next(0, 5);
        var roles = new List<RoleSpec>(count);
        for (var i = 0; i < count; i++)
        {
            var id = random.Next(3) == 0 ? AdminRoleId : RoleIds[random.Next(RoleIds.Length)];
            if (random.Next(4) == 0)
            {
                var (name, json) = StoredJson[random.Next(StoredJson.Count)];
                roles.Add(new RoleSpec($"{id}/{name}", id, null, json));
                continue;
            }

            var grants = new List<PermissionDescriptor>();
            var grantCount = random.Next(0, 6);
            for (var g = 0; g < grantCount; g++)
                grants.Add(GrantPool[random.Next(GrantPool.Count)]);
            roles.Add(new RoleSpec($"{id}/random-{grantCount}", id, grants));
        }

        return roles;
    }
}
