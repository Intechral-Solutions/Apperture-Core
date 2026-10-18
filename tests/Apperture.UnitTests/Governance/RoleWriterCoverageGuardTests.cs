using System.Text.RegularExpressions;
using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The writer guard of the anti-escalation rules (IAM-10 gate P-7, A10.2.16). Every production place that changes who holds a role,
/// what a role contains, or creates a user carrying role ids, is in one of three groups: <b>ordinary</b> (the rules apply and the
/// source shows the authority lock set being taken before the write), <b>exempt</b> (a named system actor that is approved to
/// write without the rules), or a <b>mechanism</b> (the repository and adapters that carry out a write someone else asked for). A
/// writer in none of them fails here, so a new writer cannot appear unclassified.
/// </summary>
public sealed class RoleWriterCoverageGuardTests
{
    private enum Writes
    {
        /// <summary>A call to assign or remove a role: <c>AssignToUserAsync</c> or <c>RemoveFromUserAsync</c>.</summary>
        RoleAssignmentCall,

        /// <summary>A call through the role provider's assign or remove (an adapter nothing calls).</summary>
        RoleProviderAdapterCall,

        /// <summary>A call that writes a role's permission JSON: replace, update or create.</summary>
        RolePermissionJsonCall,

        /// <summary>A <c>new User(...)</c> whose role ids are not empty: the user is created holding roles.</summary>
        UserCreatedWithRoleIds,

        /// <summary>A <c>new User(...)</c> with no role ids.</summary>
        UserCreatedWithoutRoleIds,

        /// <summary>A direct write of the role or assignment tables through the context.</summary>
        DirectTableWrite,
    }

    private enum Group
    {
        Ordinary,
        Exempt,
        Mechanism,
    }

    private sealed record Classified(string File, Writes Kind, int Count, Group Group, string Why);

    private const string Security = "Apperture.ControlPlane/Apperture.ControlPlane.Security/";
    private const string Persistence = "Apperture.Persistence/Apperture.Persistence.SqlServer/";

    private static readonly IReadOnlyList<Classified> Inventory =
    [
        // Ordinary: the rules apply.
        new(Security + "UserManagementService.cs", Writes.RoleAssignmentCall, 2, Group.Ordinary, "assign and remove a role"),
        new(Security + "UserManagementService.cs", Writes.RolePermissionJsonCall, 2, Group.Ordinary, "create a role with grants; replace a role's permissions"),
        new(Security + "UserManagementService.cs", Writes.UserCreatedWithoutRoleIds, 1, Group.Ordinary, "a new user starts with no roles"),
        new(Security + "UserProvisioningService.cs", Writes.RoleAssignmentCall, 1, Group.Ordinary, "the initial role of a provisioned person"),
        new(Security + "UserProvisioningService.cs", Writes.UserCreatedWithoutRoleIds, 2, Group.Ordinary, "a provisioned person starts with no roles; the role is assigned in the same unit"),

        // Exempt: named system actors, approved to write without the rules.
        new(Security + "BootstrapPlatformAdminAssignment.cs", Writes.RoleAssignmentCall, 1, Group.Exempt, "startup configuration assigns the administrator role"),
        new(Security + "BootstrapPlatformAdminAssignment.cs", Writes.UserCreatedWithRoleIds, 2, Group.Exempt, "startup configuration creates the first administrator"),
        new(Security + "DevTestUserBootstrapHostedService.cs", Writes.RoleAssignmentCall, 1, Group.Exempt, "development bootstrap"),
        new(Security + "DevTestUserBootstrapHostedService.cs", Writes.UserCreatedWithRoleIds, 1, Group.Exempt, "development bootstrap"),
        new(Security + "RealBootstrapRecoveryService.cs", Writes.RoleAssignmentCall, 1, Group.Exempt, "bootstrap recovery restores the administrator role"),
        new(Security + "FirstAdminBootstrapService.cs", Writes.UserCreatedWithRoleIds, 1, Group.Exempt, "first administrator setup"),
        new(Security + "EmergencyAccessService.cs", Writes.UserCreatedWithRoleIds, 1, Group.Exempt, "Recovery Access provisioning"),
        new(Security + "PlatformAdminRoleRepairService.cs", Writes.RolePermissionJsonCall, 1, Group.Exempt, "the startup repair of the administrator role (system actor administrator-repair; one audited unit that locks the role row before it decides and writes)"),
        new(Persistence + "RoleAuthorizationDataStep.cs", Writes.DirectTableWrite, 1, Group.Exempt, "the startup data step that preserves what the removed role.manage implication gave (audited, one transaction, adds only the grants it names)"),

        // Mechanisms: carry out a write that an ordinary or exempt writer decided.
        new(Security + "SqlRoleProvider.cs", Writes.RoleAssignmentCall, 2, Group.Mechanism, "the role provider's assign and remove, which nothing in production calls"),
        new(Persistence + "SqlRoleRepository.cs", Writes.DirectTableWrite, 9, Group.Mechanism, "the role repository writes its own tables"),
        new(Persistence + "SqlUserRepository.cs", Writes.DirectTableWrite, 1, Group.Mechanism, "persists the role ids of a user it is asked to add"),
        new(Persistence + "SqlUserRepository.cs", Writes.UserCreatedWithRoleIds, 1, Group.Mechanism, "maps a stored user to the domain; not a writer"),
    ];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<(string Relative, string Text)> ProductionSource()
    {
        var src = Path.Combine(RepoRoot(), "src");
        foreach (var path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, path).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal)
                || relative.Contains("/Migrations/", StringComparison.Ordinal))
                continue;
            yield return (relative, File.ReadAllText(path));
        }
    }

    private static readonly Regex AssignmentCall = new(@"\.(AssignToUserAsync|RemoveFromUserAsync)\(", RegexOptions.CultureInvariant);
    private static readonly Regex AdapterCall = new(@"\.(AssignRoleToUserAsync|RemoveRoleFromUserAsync)\(", RegexOptions.CultureInvariant);
    private static readonly Regex JsonCall = new(@"\.(UpdatePermissionsAsync|ReplacePermissionsAsync)\(|[Rr]ole\w*\.CreateAsync\(", RegexOptions.CultureInvariant);
    private static readonly Regex DirectWrite = new(@"UserRoles\.(Add|Remove)|new UserRoleEntity|\bRoles\.(Add|Remove)|PermissionsJson\s*=[^=]|new RoleEntity", RegexOptions.CultureInvariant);
    private static readonly Regex NewUser = new(@"new User\(", RegexOptions.CultureInvariant);

    /// <summary>Every write found in the given sources, counted by file and kind.</summary>
    private static Dictionary<(string File, Writes Kind), int> Scan(IEnumerable<(string Relative, string Text)> sources)
    {
        var counts = new Dictionary<(string, Writes), int>();
        void Add(string file, Writes kind, int n)
        {
            if (n > 0)
                counts[(file, kind)] = counts.GetValueOrDefault((file, kind)) + n;
        }

        foreach (var (file, text) in sources)
        {
            var code = StripComments(text);
            var assignment = AssignmentCall.Matches(code).Count;
            Add(file, Writes.RoleAssignmentCall, assignment);
            // The adapter calls are classified by the same kind: a role provider caller is as much a writer as a repository caller.
            Add(file, Writes.RoleProviderAdapterCall, AdapterCall.Matches(code).Count);
            Add(file, Writes.RolePermissionJsonCall, JsonCall.Matches(code).Count);
            Add(file, Writes.DirectTableWrite, DirectWrite.Matches(code).Count);
            foreach (Match match in NewUser.Matches(code))
            {
                var roleIds = RoleIdsArgument(code, match.Index + match.Length);
                Add(file, IsEmpty(roleIds) ? Writes.UserCreatedWithoutRoleIds : Writes.UserCreatedWithRoleIds, 1);
            }
        }

        return counts;
    }

    /// <summary>The fourth argument of a <c>new User(...)</c> (the role ids), read by balanced brackets.</summary>
    private static string RoleIdsArgument(string code, int afterOpenParenthesis)
    {
        var depth = 1;
        var argument = 0;
        var start = afterOpenParenthesis;
        for (var i = afterOpenParenthesis; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '(' or '[' or '{')
                depth++;
            else if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth == 0)
                    return argument == 3 ? code[start..i] : "";
            }
            else if (c == ',' && depth == 1)
            {
                if (argument == 3)
                    return code[start..i];
                argument++;
                start = i + 1;
            }
        }

        return "";
    }

    private static bool IsEmpty(string roleIds) =>
        Regex.IsMatch(roleIds.Trim(), @"^(\[\s*\]|Array\.Empty<RoleId>\(\)|new RoleId\[0\]|new\[\]\s*\{\s*\}|)$", RegexOptions.CultureInvariant);

    private static string StripComments(string text) =>
        Regex.Replace(text, @"^\s*//.*$", "", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static List<string> Differences(Dictionary<(string File, Writes Kind), int> found)
    {
        var expected = Inventory.ToDictionary(i => (i.File, i.Kind), i => i.Count);
        var differences = new List<string>();
        foreach (var ((file, kind), count) in found.OrderBy(kv => kv.Key.File, StringComparer.Ordinal).ThenBy(kv => kv.Key.Kind))
        {
            if (!expected.TryGetValue((file, kind), out var want))
                differences.Add($"UNCLASSIFIED WRITER: {file} has {count} x {kind}; classify it as ordinary, exempt or a mechanism");
            else if (want != count)
                differences.Add($"COUNT DIFFERS: {file} {kind}: classified {want}, source {count}");
        }

        foreach (var ((file, kind), count) in expected)
            if (!found.ContainsKey((file, kind)))
                differences.Add($"CLASSIFIED BUT NOT FOUND: {file} {kind} x{count}");
        return differences;
    }

    [Fact]
    public void Every_production_writer_of_roles_assignments_and_users_with_role_ids_is_classified()
    {
        var found = Scan(ProductionSource());

        found.Should().NotBeEmpty();
        Differences(found).Should().BeEmpty(
            "a writer of role assignments, role permissions or a user created with roles must be classified in the inventory of this guard");
    }

    [Fact]
    public void Nothing_in_production_calls_the_role_providers_assign_or_remove()
    {
        Scan(ProductionSource()).Keys.Where(k => k.Kind == Writes.RoleProviderAdapterCall)
            .Should().BeEmpty("the adapters are unused; a caller would be an unclassified writer that bypasses the rules");
    }

    [Fact]
    public void Ordinary_writers_take_the_authority_lock_set_before_they_write()
    {
        var sources = ProductionSource().ToDictionary(s => s.Relative, s => s.Text, StringComparer.Ordinal);
        var management = sources[Security + "UserManagementService.cs"];
        var provisioning = sources[Security + "UserProvisioningService.cs"];

        // Each ordinary method decides under the locks, and the decision comes before the write that follows it.
        AssertLocksBefore(management, "public async ValueTask SetRolePermissionsAsync(", "ReplacePermissionsAsync(");
        AssertLocksBefore(management, "public async ValueTask<Role> CreateRoleAsync(", "_roleRepository.CreateAsync(");
        AssertLocksBefore(management, "public async ValueTask AssignRoleAsync(", "AssignToUserAsync(");
        AssertLocksBefore(management, "public async ValueTask RemoveRoleAsync(", "RemoveFromUserAsync(");
        AssertLocksBefore(provisioning, "private async ValueTask<InitialRoleDecision> DecideInitialRoleAsync(", "return new InitialRoleDecision(role, null);");

        // The provisioning writer assigns only a role the decision allowed.
        provisioning.Should().Contain("AssignInitialRoleAsync(transaction, userId, initialRole.Role, ct)");
        provisioning.Should().NotContain("TryAssignRoleAsync", "the earlier assignment that needed only role.assign is gone");
    }

    private static void AssertLocksBefore(string text, string methodSignature, string write)
    {
        var start = text.IndexOf(methodSignature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, methodSignature);
        var nextMember = Regex.Match(text[(start + methodSignature.Length)..], @"\n    (\[|///|public |private |internal )");
        var body = nextMember.Success ? text.Substring(start, methodSignature.Length + nextMember.Index) : text[start..];
        var lockAt = body.IndexOf("LockAuthoritySnapshotAsync(", StringComparison.Ordinal);
        var writeAt = body.IndexOf(write, StringComparison.Ordinal);
        lockAt.Should().BeGreaterThanOrEqualTo(0, $"{methodSignature} must take the authority lock set");
        writeAt.Should().BeGreaterThan(lockAt, $"{methodSignature} must decide under the locks before it writes ({write})");
    }

    // ---- The guard bites ----

    [Fact]
    public void The_guard_fails_for_a_new_writer_in_an_unclassified_file()
    {
        var sources = ProductionSource().Append((Security + "SomethingNew.cs", "await _roles.AssignToUserAsync(a, b, ct);")).ToList();

        Differences(Scan(sources)).Should().ContainSingle().Which.Should().StartWith("UNCLASSIFIED WRITER").And.Contain("SomethingNew.cs");
    }

    [Fact]
    public void The_guard_fails_for_an_extra_writer_in_a_classified_file_and_for_a_different_kind_of_writer()
    {
        var extra = ProductionSource().Append((Security + "UserManagementService.cs", "await _roleRepository.RemoveFromUserAsync(a, b, ct);")).ToList();
        Differences(Scan(extra)).Should().ContainSingle().Which.Should().StartWith("COUNT DIFFERS");

        var json = ProductionSource().Append((Security + "UserProvisioningService.cs", "await _roles.UpdatePermissionsAsync(id, p, ct);")).ToList();
        Differences(Scan(json)).Should().ContainSingle().Which.Should().StartWith("UNCLASSIFIED WRITER").And.Contain("RolePermissionJsonCall");
    }

    [Fact]
    public void The_guard_fails_for_a_user_created_with_role_ids_outside_the_system_writers()
    {
        var sources = ProductionSource().Append((Security + "UserProvisioningService.cs",
            "var user = new User(id, name, links, new[] { new RoleId(\"role-x\") }, IsBreakGlassEligible: false);")).ToList();

        Differences(Scan(sources)).Should().ContainSingle().Which.Should().StartWith("UNCLASSIFIED WRITER").And.Contain("UserCreatedWithRoleIds");
    }

    [Fact]
    public void The_guard_fails_when_a_classified_writer_disappears()
    {
        var sources = ProductionSource().Where(s => !s.Relative.EndsWith("RealBootstrapRecoveryService.cs", StringComparison.Ordinal)).ToList();

        Differences(Scan(sources)).Should().ContainSingle().Which.Should().StartWith("CLASSIFIED BUT NOT FOUND");
    }

    [Theory]
    [InlineData("[]", true)]
    [InlineData("Array.Empty<RoleId>()", true)]
    [InlineData(" new RoleId[0] ", true)]
    [InlineData("[adminRoleId]", false)]
    [InlineData("new[] { new RoleId(\"x\") }", false)]
    [InlineData("assigned", false)]
    public void An_empty_role_id_argument_is_recognised(string argument, bool empty) =>
        IsEmpty(argument).Should().Be(empty);

    [Fact]
    public void The_role_id_argument_is_read_by_balanced_brackets()
    {
        const string code = "new User(userId, name.Trim(), [new IdentityLink(a, b, c)], [adminRoleId], IsBreakGlassEligible: false)";

        RoleIdsArgument(code, code.IndexOf('(') + 1).Trim().Should().Be("[adminRoleId]");
    }
}
