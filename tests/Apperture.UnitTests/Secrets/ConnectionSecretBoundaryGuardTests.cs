using System.Text.RegularExpressions;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Secrets;

/// <summary>
/// SEC-2.2 (A2.1): the connection-material boundary stays the only code that turns <c>Connection.SecretRefs</c> into secrets.
/// An identifier-based guard over production source (the closure-guard style): it fails when a production file other than the
/// boundary both reads <c>SecretRefs</c> and holds a secret resolver, or builds a <c>SecretRef</c> next to <c>SecretRefs</c>, or
/// when a connector or connector-resolution file other than the boundary mentions a secret resolver at all. A self-test feeds the
/// scanner the loop the verifiers used to copy and proves it is caught.
///
/// <para>It is not a data-flow analysis. It cannot see a reference smuggled through a variable defined in another file, which is
/// why the allow-list is one file, why the connector projects may not mention a resolver at all, and why the runtime behaviour
/// (every path refused for a poisoned row) is proved separately against SQL Server.</para>
/// </summary>
public sealed class ConnectionSecretBoundaryGuardTests
{
    /// <summary>The one file that may read <c>SecretRefs</c> and resolve secrets, and why.</summary>
    private const string Boundary = "Apperture.ConnectorResolution/ConnectionMaterialResolver.cs";

    /// <summary>Anything that can turn a reference into material. A new resolver type belongs here.</summary>
    private static readonly string[] SecretResolverTypes =
    [
        "ISecretResolver", "CompositeSecretResolver", "SqlSecretResolver", "AzureKeyVaultSecretResolver", "ISecretResolverRegistry",
    ];

    /// <summary>Projects in which no file but the boundary may name a secret resolver (the connectors may not resolve secrets themselves).</summary>
    private static readonly string[] NoResolverRoots = ["Apperture.Connectors/", "Apperture.ConnectorResolution/"];

    private static readonly Regex NewSecretRef = new(@"\bnew\s+SecretRef\s*\(", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal sealed record Violation(string Path, string Rule);

    /// <summary>The scanner. Public to the self-test so the same code that guards the repository is the code that is proved.</summary>
    internal static IReadOnlyList<Violation> Scan(IEnumerable<SourceFile> files, string boundary = Boundary)
    {
        var violations = new List<Violation>();
        foreach (var file in files)
        {
            if (string.Equals(file.RelativePath, boundary, StringComparison.Ordinal))
                continue;

            var readsReferences = file.Mentions("SecretRefs");
            var resolverMentioned = SecretResolverTypes.Where(file.Mentions).ToList();

            if (readsReferences && resolverMentioned.Count > 0)
                violations.Add(new Violation(file.RelativePath, $"reads Connection.SecretRefs and holds {resolverMentioned[0]}: hands a connection reference to a secret resolver outside the boundary"));

            if (readsReferences && NewSecretRef.IsMatch(file.Code))
                violations.Add(new Violation(file.RelativePath, "builds a SecretRef in a file that reads Connection.SecretRefs, outside the boundary"));

            // The same references read as the stored column text (a persistence helper that deserializes SecretRefsJson) next to a
            // resolver is the same bypass by another name.
            if (!readsReferences && resolverMentioned.Count > 0 && file.Mentions("SecretRefsJson"))
                violations.Add(new Violation(file.RelativePath, $"reads the stored SecretRefsJson and holds {resolverMentioned[0]}: resolves stored connection references outside the boundary"));

            if (resolverMentioned.Count > 0 && NoResolverRoots.Any(root => file.RelativePath.StartsWith(root, StringComparison.Ordinal)))
                violations.Add(new Violation(file.RelativePath, $"mentions {resolverMentioned[0]}; connector code resolves a connection only through IConnectionMaterialResolver"));
        }

        return violations;
    }

    // ---- the repository ----

    [Fact]
    public void No_production_file_outside_the_boundary_resolves_connection_secret_references()
    {
        var violations = Scan(RepositorySource.Production);

        violations.Should().BeEmpty(
            "every Connection.SecretRefs entry must be resolved through IConnectionMaterialResolver, which proves the reference is the connection's own before the secret is read. "
            + string.Join("; ", violations.Select(v => $"{v.Path}: {v.Rule}")));
    }

    [Fact]
    public void The_boundary_is_not_a_stale_allow_list_entry_it_still_reads_references_and_resolves_only_through_the_owned_call()
    {
        var boundary = RepositorySource.Find(Boundary);

        boundary.Should().NotBeNull("the allow-list names a file that exists");
        boundary!.Mentions("SecretRefs").Should().BeTrue("an entry that no longer does what it allows must be removed");
        boundary.Mentions("ResolveOwnedAsync").Should().BeTrue();
        // Whatever the receiver is called (a field, a local alias, a parameter): the boundary makes no call to a method named ResolveAsync.
        Regex.IsMatch(boundary.Code, @"\.\s*ResolveAsync\s*\(", RegexOptions.CultureInvariant)
            .Should().BeFalse("the boundary never uses the unchecked call");
    }

    [Fact]
    public void The_boundary_is_registered_once_for_every_host_that_composes_connection_resolution()
    {
        var registration = RepositorySource.Find("Apperture.ConnectorResolution/ServiceCollectionExtensions.cs");

        registration.Should().NotBeNull();
        registration!.Code.Should().Contain("TryAddScoped<IConnectionMaterialResolver, ConnectionMaterialResolver>");
    }

    // ---- the scanner proves itself ----

    private static SourceFile Source(string path, string text) => new(path, text);

    /// <summary>The material loop the Sftp and CampusNexus verifiers carried before SEC-2.2.</summary>
    private const string TheCopiedLoop = """
        namespace Apperture.Connectors.Sftp;

        public sealed class SftpConnectionVerifier(ISecretResolver secretResolver)
        {
            private static async ValueTask<ResolvedConnectionMaterial> BuildMaterialAsync(Connection connection, ISecretResolver secretResolver, CancellationToken ct)
            {
                var merged = new Dictionary<string, string>(connection.Config);
                foreach (var kv in connection.SecretRefs)
                {
                    var secretRef = new SecretRef(kv.Value);
                    var resolved = await secretResolver.ResolveAsync(secretRef, ct);
                    merged[kv.Key] = resolved.Material.OpaqueString ?? "";
                }

                return new ResolvedConnectionMaterial(merged);
            }
        }
        """;

    [Fact]
    public void Self_test_the_copied_resolution_loop_is_detected_in_a_connector_and_elsewhere()
    {
        var inConnector = Scan([Source("Apperture.Connectors/Apperture.Connectors.Sftp/SftpConnectionVerifier.cs", TheCopiedLoop)]);
        var inControlPlane = Scan([Source("Apperture.ControlPlane/Apperture.ControlPlane.Security/SomeNewService.cs", TheCopiedLoop)]);

        inConnector.Select(v => v.Rule).Should().Contain(r => r.Contains("hands a connection reference"))
            .And.Contain(r => r.Contains("builds a SecretRef"))
            .And.Contain(r => r.Contains("connector code resolves a connection only through"));
        inControlPlane.Select(v => v.Rule).Should().Contain(r => r.Contains("hands a connection reference"))
            .And.Contain(r => r.Contains("builds a SecretRef"));
    }

    [Fact]
    public void Self_test_each_resolver_type_is_recognised()
    {
        foreach (var type in SecretResolverTypes)
        {
            var text = $"public sealed class C {{ private readonly {type} _r; object M(Connection c) => c.SecretRefs; }}";

            Scan([Source("Apperture.ControlPlane/X/C.cs", text)]).Should().NotBeEmpty($"{type} next to SecretRefs is a way to resolve a connection's references");
        }
    }

    [Fact]
    public void Self_test_a_persistence_helper_that_resolves_the_stored_reference_column_is_caught()
    {
        const string text = """
            public sealed class Helper(SqlSecretResolver resolver)
            {
                public async Task Go(ConnectionEntity e) =>
                    await resolver.ResolveAsync(new Core.SecretRef(JsonSerializer.Deserialize<Dictionary<string, string>>(e.SecretRefsJson)!["Password"]));
            }
            """;

        Scan([Source("Apperture.Persistence/Apperture.Persistence.SqlServer/Helper.cs", text)]).Should().Contain(v => v.Rule.Contains("SecretRefsJson"));
        Scan([Source("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlConnectionRepository.cs", "public sealed class R { string S(ConnectionEntity e) => e.SecretRefsJson; }")])
            .Should().BeEmpty("the repository reads the column and resolves nothing");
    }

    [Fact]
    public void Self_test_the_boundary_path_alone_is_exempt()
    {
        Scan([Source(Boundary, TheCopiedLoop)]).Should().BeEmpty();
        Scan([Source("Apperture.ConnectorResolution/Other.cs", TheCopiedLoop)]).Should().NotBeEmpty();
    }

    [Fact]
    public void Self_test_a_verifier_that_uses_the_boundary_is_clean()
    {
        const string text = """
            namespace Apperture.Connectors.Sftp;

            public sealed class SftpConnectionVerifier(IConnectionMaterialResolver materials)
            {
                public async ValueTask Verify(Connection connection, CancellationToken ct) =>
                    await materials.ResolveAsync(connection, ConnectionResolutionPath.Verify, ct);
            }
            """;

        Scan([Source("Apperture.Connectors/Apperture.Connectors.Sftp/SftpConnectionVerifier.cs", text)]).Should().BeEmpty();
    }

    [Fact]
    public void Self_test_a_comment_that_names_the_types_is_not_a_violation()
    {
        const string text = """
            // Do not read Connection.SecretRefs here or hand it to an ISecretResolver; use IConnectionMaterialResolver.
            namespace Apperture.Connectors.Sftp;
            public sealed class Clean { }
            """;

        Scan([Source("Apperture.Connectors/Apperture.Connectors.Sftp/Clean.cs", text)]).Should().BeEmpty();
    }

    [Fact]
    public void Self_test_holders_that_are_not_connections_are_not_caught_by_the_connection_rule()
    {
        // The Entra client secret and the settings secrets resolve a reference they own; SEC-2.3 and later hold them to a rule of their own.
        const string text = "public sealed class EntraClientSecretAccess(ISecretResolver resolver) { object M(string clientSecretRef) => resolver; }";

        Scan([Source("Apperture.ControlPlane/Apperture.ControlPlane.Security/EntraClientSecretAccess.cs", text)]).Should().BeEmpty();
    }

    [Fact]
    public void Self_test_a_resolver_in_a_connector_project_is_caught_even_without_reading_references()
    {
        const string text = "public sealed class V(ISecretResolver resolver) { }";

        Scan([Source("Apperture.Connectors/Apperture.Connectors.CampusNexus/V.cs", text)]).Should().ContainSingle();
        Scan([Source("Apperture.ConnectorResolution/V.cs", text)]).Should().ContainSingle();
        Scan([Source("Apperture.ControlPlane/V.cs", text)]).Should().BeEmpty();
    }
}
