using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using static Apperture.UnitTests.Governance.ProductionAuthorizationCheckCorpus;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The completeness guard of the production authorization-check corpus (IAM-10.0 gate, P-1 and A10.2.1): a new production
/// check of one of the permissions the evaluator treats specially fails here until it is added to the corpus, which in
/// turn puts it in front of the differential equivalence tests.
/// </summary>
public sealed class ProductionAuthorizationCheckCorpusGuardTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>Production C# source, as paths relative to <c>src/</c> with forward slashes, excluding build output.</summary>
    private static IEnumerable<(string Relative, string FullPath)> ProductionSource()
    {
        var src = Path.Combine(RepoRoot(), "src");
        foreach (var path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, path).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
                continue;
            yield return (relative, path);
        }
    }

    private static List<ActionMention> ScanProductionSource()
    {
        var mentions = new List<ActionMention>();
        foreach (var (relative, path) in ProductionSource())
        {
            var text = File.ReadAllText(path);
            if (!text.Contains("ActionConstants.", StringComparison.Ordinal))
                continue;
            mentions.AddRange(AuthorizationSourceScanner.Scan(relative, text));
        }

        return mentions;
    }

    /// <summary>
    /// Compares what was found in source with the corpus. Returns one line per difference: a mention the corpus does not
    /// account for, and a corpus entry the source no longer has.
    /// </summary>
    private static List<string> Differences(IReadOnlyList<ActionMention> mentions)
    {
        var definitionFiles = DefinitionSites.Select(d => d.File).ToHashSet(StringComparer.Ordinal);

        var expected = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var check in Checks)
            expected[$"{check.File} :: {check.Action} on {check.ResourceType}"] = check.ChecksPerFile;
        foreach (var site in DefinitionSites)
            expected[$"{site.File} :: {site.Action} (definition: {site.Role})"] = site.References;

        var actual = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var mention in mentions)
        {
            var key = definitionFiles.Contains(mention.File)
                ? $"{mention.File} :: {mention.Action} (definition: {DefinitionSites.First(d => d.File == mention.File).Role})"
                : $"{mention.File} :: {mention.Action} on {mention.ResourceType ?? "<no resource type found>"}";
            actual[key] = actual.GetValueOrDefault(key) + 1;
        }

        var differences = new List<string>();
        foreach (var (key, count) in actual.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var want = expected.GetValueOrDefault(key);
            if (want == 0)
                differences.Add($"NOT IN CORPUS (x{count}): {key}");
            else if (want != count)
                differences.Add($"COUNT DIFFERS for {key}: corpus {want}, source {count}");
        }

        foreach (var (key, count) in expected.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (!actual.ContainsKey(key))
                differences.Add($"IN CORPUS BUT NOT IN SOURCE (x{count}): {key}");

        return differences;
    }

    [Fact]
    public void Every_production_mention_of_these_permissions_is_accounted_for_by_the_corpus()
    {
        var mentions = ScanProductionSource();

        mentions.Should().NotBeEmpty("the scan must find the production checks");
        Differences(mentions).Should().BeEmpty(
            "a production check of one of these permissions must be added to ProductionAuthorizationCheckCorpus so the differential tests ask it");
    }

    [Fact]
    public void Every_check_the_corpus_lists_is_found_in_source_with_the_scope_it_assumes()
    {
        var mentions = ScanProductionSource();
        var checkFiles = Checks.Select(c => c.File).ToHashSet(StringComparer.Ordinal);

        var wrongScope = mentions
            .Where(m => checkFiles.Contains(m.File) && m.ScopeMember is not null and not nameof(ScopeKeyConstants.Instance))
            .Select(m => $"{m.File} :: {m.Action} uses ScopeKeyConstants.{m.ScopeMember}")
            .ToList();

        wrongScope.Should().BeEmpty("the corpus assumes every check is made against the instance scope");
    }

    [Fact]
    public void Helpers_that_build_a_check_from_an_action_and_a_resource_type_use_the_instance_scope()
    {
        foreach (var helper in InstanceScopeHelpers)
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", helper));
            text.Should().Contain("new ResourceDescriptor(", $"{helper} builds the checked resource");
            text.Should().Contain("ScopeKeyConstants.Instance", $"{helper} must check against the instance scope");
        }
    }

    [Fact]
    public void No_production_source_asks_for_these_permissions_by_literal_or_static_import()
    {
        var offenders = new List<string>();
        foreach (var (relative, path) in ProductionSource())
        {
            if (relative.Contains("/Migrations/", StringComparison.Ordinal) || relative.EndsWith("/ActionConstants.cs", StringComparison.Ordinal))
                continue;

            var text = File.ReadAllText(path);
            offenders.AddRange(AuthorizationSourceScanner.ActionLiterals(text).Select(l => $"{relative}: literal {l}"));
            if (AuthorizationSourceScanner.UsesStaticImportOfActionConstants(text))
                offenders.Add($"{relative}: using static ActionConstants");
        }

        offenders.Should().BeEmpty(
            "a check spelled with a literal or a static import would escape the scan; use ActionConstants.X (migrations are data and are excluded)");
    }

    [Fact]
    public void The_corpus_covers_every_floor_combination_that_has_a_consumer_and_names_the_one_that_has_none()
    {
        var asked = Permissions().Select(p => (p.Action, p.Resource!.ResourceType)).ToHashSet();

        var floorWithoutConsumer = AuthorizationEquivalenceFixtures.FloorCombinations.Where(c => !asked.Contains(c)).ToList();

        // Row 9 of the IAM-10.0 gate section 2.2 (role.assign on User) is the vestigial combination: nothing asks for it.
        floorWithoutConsumer.Should().BeEquivalentTo([("role.assign", "User")]);
        asked.Should().Contain(("role.manage", "Role"), "the implying permission has production consumers too");
        asked.Except(AuthorizationEquivalenceFixtures.FloorCombinations.Append(("role.manage", "Role"))).Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The guard bites
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_guard_fails_for_a_new_check_in_a_file_the_corpus_does_not_know()
    {
        var mentions = ScanProductionSource();
        mentions.AddRange(AuthorizationSourceScanner.Scan(
            "Apperture.ControlPlane/Apperture.ControlPlane.Security/SomethingNewService.cs",
            "var ok = await Evaluate(new PermissionDescriptor(ActionConstants.UserManage, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance)));"));

        Differences(mentions).Should().ContainSingle().Which.Should().StartWith("NOT IN CORPUS").And.Contain("SomethingNewService.cs");
    }

    [Fact]
    public void The_guard_fails_for_an_extra_check_in_a_file_the_corpus_lists()
    {
        var mentions = ScanProductionSource();
        mentions.AddRange(AuthorizationSourceScanner.Scan(
            "Apperture.ControlPlane/Apperture.ControlPlane.Security/UserManagementService.cs",
            "x(ActionConstants.CredentialManage, ResourceTypeConstants.User);"));

        Differences(mentions).Should().ContainSingle().Which.Should().StartWith("COUNT DIFFERS").And.Contain("UserManagementService.cs");
    }

    [Fact]
    public void The_guard_fails_for_the_same_action_asked_of_a_different_resource_type()
    {
        var mentions = ScanProductionSource();
        mentions.AddRange(AuthorizationSourceScanner.Scan(
            "Apperture.ControlPlane/Apperture.ControlPlane.Security/UserManagementService.cs",
            "x(ActionConstants.CredentialManage, ResourceTypeConstants.Role);"));

        Differences(mentions).Should().ContainSingle().Which.Should().StartWith("NOT IN CORPUS").And.Contain("CredentialManage on Role");
    }

    [Fact]
    public void The_guard_fails_when_a_corpus_check_disappears_from_source()
    {
        var mentions = ScanProductionSource()
            .Where(m => !(m.File.EndsWith("GovernanceDashboardCardProvider.cs", StringComparison.Ordinal) && m.Action == nameof(ActionConstants.UserRead)))
            .ToList();

        Differences(mentions).Should().ContainSingle().Which.Should().StartWith("IN CORPUS BUT NOT IN SOURCE").And.Contain("GovernanceDashboardCardProvider.cs");
    }

    [Fact]
    public void The_guard_fails_when_a_definition_site_gains_a_reference()
    {
        var mentions = ScanProductionSource();
        mentions.AddRange(AuthorizationSourceScanner.Scan(
            "Apperture.ControlPlane/Apperture.ControlPlane.Security/PlatformAdministratorGovernancePermissions.cs",
            "or ActionConstants.UserManage;"));

        Differences(mentions).Should().ContainSingle().Which.Should().StartWith("COUNT DIFFERS");
    }

    [Fact]
    public void The_scanner_ignores_comment_lines_and_reads_the_resource_type_of_its_own_call()
    {
        var mentions = AuthorizationSourceScanner.Scan(
            "f.cs",
            """
            // ActionConstants.UserRead on ResourceTypeConstants.Role is only mentioned here
            var a = Ask(ActionConstants.UserRead, ResourceTypeConstants.Role);
            var b = Ask(
                ActionConstants.RoleAssign,
                ResourceTypeConstants.User);
            """);

        mentions.Should().BeEquivalentTo(
        [
            new ActionMention("f.cs", "UserRead", "Role", null),
            new ActionMention("f.cs", "RoleAssign", "User", null),
        ]);
    }

    [Fact]
    public void The_literal_scan_finds_a_quoted_action_and_skips_comments()
    {
        AuthorizationSourceScanner.ActionLiterals("var x = \"user.read\";\n// \"role.manage\" is a comment\n").Should().ContainSingle().Which.Should().Be("\"user.read\"");
    }
}
