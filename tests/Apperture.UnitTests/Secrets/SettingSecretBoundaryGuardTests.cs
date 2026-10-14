using System.Text.RegularExpressions;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Secrets;

/// <summary>
/// SEC-2.3 (A3.4): the setting-secret boundary stays the only code that turns a reference read from a setting row into a secret.
/// An identifier-based guard over production source (the SEC-2.2 and closure-guard style) that fails when
/// <list type="bullet">
/// <item>a production file other than the three that resolve a stored reference builds a <c>SecretRef</c>, so a new reader cannot
/// silently resolve by reference alone;</item>
/// <item>a file that reads settings (a settings resolver or a resolved setting value) also holds a secret resolver;</item>
/// <item>the runtime accessor or the SMTP resolver names a secret resolver at all, rather than the boundary;</item>
/// <item>anything but the SMTP resolver calls the explicit deployment-configured path, which proves no ownership;</item>
/// <item>the boundary stops proving ownership: its one unchecked call must be the deployment-configured one.</item>
/// </list>
/// A self-test feeds the scanner the shapes it must catch.
///
/// <para>It is not a data-flow analysis. It cannot see a reference passed through a variable defined in another file, which is why
/// the allow-list is three files, why the readers may not mention a resolver at all, and why the runtime behaviour (every foreign
/// reference refused, the foreign payload never read) is proved separately against SQL Server.</para>
/// </summary>
public sealed class SettingSecretBoundaryGuardTests
{
    private const string Boundary = "Apperture.Core/Apperture.Core.Application/PlatformSettings/SettingSecretResolver.cs";
    private const string SmtpResolver = "Apperture.ControlPlane/Apperture.ControlPlane.Security/EmailSmtpCredentialResolver.cs";
    private const string Accessor = "Apperture.RuntimePlane/Apperture.RuntimePlane.Engine/Integration/IntegrationSettingsAccessor.cs";

    /// <summary>
    /// The files that may build a <c>SecretRef</c>, and why. The connection boundary resolves a connection's own references (SEC-2.2),
    /// the setting boundary resolves a setting's, and the Entra client secret is a different holder this package does not cover
    /// (design gate K-3: a candidate consumer of the same primitive, recorded for the owner and not assessed).
    /// </summary>
    private static readonly string[] MaySecretRef =
    [
        "Apperture.ConnectorResolution/ConnectionMaterialResolver.cs",
        Boundary,
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/EntraClientSecretAccess.cs",
    ];

    /// <summary>Anything that can turn a reference into material. A new resolver type belongs here.</summary>
    private static readonly string[] SecretResolverTypes =
    [
        "ISecretResolver", "CompositeSecretResolver", "SqlSecretResolver", "AzureKeyVaultSecretResolver", "ISecretResolverRegistry",
    ];

    /// <summary>Identifiers that mark code reading setting values.</summary>
    private static readonly string[] SettingReaders =
    [
        "IIntegrationSettingsResolver", "IPlatformSettingsResolver", "PlatformSettingResolvedValue", "IIntegrationSettingsAccessor",
    ];

    /// <summary>Files that read settings and hold a resolver for a reason other than a setting's reference, each named and justified.</summary>
    private static readonly string[] ReadSettingsAndMentionResolverBecause = [];

    private static readonly Regex NewSecretRef = new(@"\bnew\s+SecretRef\s*\(", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal sealed record Violation(string Path, string Rule);

    internal static IReadOnlyList<Violation> Scan(IEnumerable<SourceFile> files)
    {
        var violations = new List<Violation>();
        foreach (var file in files)
        {
            if (NewSecretRef.IsMatch(file.Code) && !MaySecretRef.Contains(file.RelativePath, StringComparer.Ordinal))
                violations.Add(new Violation(file.RelativePath, "builds a SecretRef outside the connection boundary, the setting boundary and the Entra client secret access"));

            var resolver = SecretResolverTypes.FirstOrDefault(file.Mentions);
            var reader = SettingReaders.FirstOrDefault(file.Mentions);
            if (resolver is not null && reader is not null
                && !string.Equals(file.RelativePath, Boundary, StringComparison.Ordinal)
                && !ReadSettingsAndMentionResolverBecause.Contains(file.RelativePath, StringComparer.Ordinal))
            {
                violations.Add(new Violation(file.RelativePath, $"reads settings ({reader}) and holds {resolver}: a setting's reference is resolved only through ISettingSecretResolver"));
            }

            if (file.Mentions("ResolveDeploymentConfiguredAsync")
                && !string.Equals(file.RelativePath, Boundary, StringComparison.Ordinal)
                && !string.Equals(file.RelativePath, "Apperture.Core/Apperture.Core.Application/PlatformSettings/ISettingSecretResolver.cs", StringComparison.Ordinal)
                && !string.Equals(file.RelativePath, SmtpResolver, StringComparison.Ordinal))
            {
                violations.Add(new Violation(file.RelativePath, "calls the deployment-configured path, which proves no ownership; only the SMTP resolver may, and only for a reference deployment configuration supplied"));
            }

            if ((string.Equals(file.RelativePath, SmtpResolver, StringComparison.Ordinal) || string.Equals(file.RelativePath, Accessor, StringComparison.Ordinal))
                && (resolver is not null || NewSecretRef.IsMatch(file.Code)))
            {
                violations.Add(new Violation(file.RelativePath, "names a secret resolver or builds a SecretRef; it resolves only through ISettingSecretResolver"));
            }
        }

        return violations;
    }

    // ---- the repository ----

    [Fact]
    public void No_production_file_resolves_a_setting_reference_outside_the_boundary()
    {
        var violations = Scan(RepositorySource.Production);

        violations.Should().BeEmpty(
            "a reference read from a setting row must be resolved through ISettingSecretResolver, which proves the secret is the setting's own before it is read. "
            + string.Join("; ", violations.Select(v => $"{v.Path}: {v.Rule}")));
    }

    [Fact]
    public void The_allow_lists_name_files_that_still_exist_and_still_do_what_they_allow()
    {
        foreach (var path in MaySecretRef)
        {
            var file = RepositorySource.Find(path);
            file.Should().NotBeNull($"the allow-list names {path}, which must exist");
            NewSecretRef.IsMatch(file!.Code).Should().BeTrue($"{path} no longer builds a SecretRef, so it should leave the allow-list");
        }

        foreach (var path in ReadSettingsAndMentionResolverBecause)
        {
            var file = RepositorySource.Find(path);
            file.Should().NotBeNull($"the allow-list names {path}, which must exist");
            SecretResolverTypes.Any(file!.Mentions).Should().BeTrue($"{path} no longer holds a resolver, so it should leave the allow-list");
            SettingReaders.Any(file.Mentions).Should().BeTrue($"{path} no longer reads settings, so it should leave the allow-list");
        }
    }

    [Fact]
    public void The_boundary_proves_ownership_and_its_only_unchecked_call_is_the_deployment_configured_one()
    {
        var boundary = RepositorySource.Find(Boundary);

        boundary.Should().NotBeNull();
        boundary!.Mentions("ResolveOwnedAsync").Should().BeTrue();
        var unchecked_ = Regex.Matches(boundary.Code, @"\.\s*ResolveAsync\s*\(", RegexOptions.CultureInvariant);
        unchecked_.Should().HaveCount(1, "the boundary makes exactly one unchecked call");
        var deploymentMethod = boundary.Code.IndexOf("ResolveDeploymentConfiguredAsync(string reference", StringComparison.Ordinal);
        deploymentMethod.Should().BeGreaterThan(0);
        unchecked_[0].Index.Should().BeGreaterThan(deploymentMethod, "and it is inside the deployment-configured method, the last one the file declares");
    }

    [Fact]
    public void The_smtp_resolver_and_the_accessor_resolve_through_the_boundary_and_through_nothing_else()
    {
        foreach (var path in new[] { SmtpResolver, Accessor })
        {
            var file = RepositorySource.Find(path);
            file.Should().NotBeNull(path);
            file!.Mentions("ISettingSecretResolver").Should().BeTrue($"{path} resolves through the boundary");
        }
    }

    [Fact]
    public void The_boundary_is_registered_for_every_host_that_reads_a_setting_secret()
    {
        string Code(string path) => RepositorySource.Find(path)?.Code ?? throw new InvalidOperationException($"{path} not found");

        Code("Apperture.Core/Apperture.Core.Application/PlatformSettings/SettingSecretServiceCollectionExtensions.cs")
            .Should().Contain("TryAddScoped<ISettingSecretResolver, SettingSecretResolver>");
        Code("Apperture.RuntimePlane/Apperture.RuntimePlane.Engine/IntegrationRuntimeServiceCollectionExtensions.cs")
            .Should().Contain("AddSettingSecretResolution()", "the Worker Host's accessor reads through it");
        Code("Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Governance.cs")
            .Should().Contain("AddSettingSecretResolution()", "account mail reads the SMTP password through it");
        Code("Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Notifications.cs")
            .Should().Contain("AddSettingSecretResolution()", "notification delivery reads the SMTP password through it");
    }

    // ---- the scanner proves itself ----

    private static SourceFile Source(string path, string text) => new(path, text);

    private const string ANewReader = """
        namespace Apperture.ControlPlane.Security;

        public sealed class SomeNewSettingReader(IPlatformSettingsResolver settings, ISecretResolver secrets)
        {
            public async Task<string> ReadAsync(string key)
            {
                var resolved = await settings.GetResolvedAsync(key);
                var secret = await secrets.ResolveAsync(new SecretRef((string)resolved!.CurrentValue));
                return secret.Material.OpaqueString!;
            }
        }
        """;

    [Fact]
    public void Self_test_a_new_setting_reader_that_resolves_by_reference_is_caught_two_ways()
    {
        var violations = Scan([Source("Apperture.ControlPlane/Apperture.ControlPlane.Security/SomeNewSettingReader.cs", ANewReader)]);

        violations.Select(v => v.Rule).Should().Contain(r => r.Contains("builds a SecretRef"))
            .And.Contain(r => r.Contains("reads settings"));
    }

    [Fact]
    public void Self_test_the_accessor_and_the_smtp_resolver_may_not_name_a_resolver_and_nothing_else_may_call_the_trusted_path()
    {
        var accessor = Scan([Source(Accessor, "class A { ISecretResolver r; ISettingSecretResolver b; IPlatformSettingsResolver s; }")]);
        var other = Scan([Source("Apperture.ControlPlane/Apperture.ControlPlane.Security/Other.cs", "class O { void M(ISettingSecretResolver b) => b.ResolveDeploymentConfiguredAsync(\"x\"); }")]);
        var smtpOk = Scan([Source(SmtpResolver, "class S { ISettingSecretResolver b; void M() => b.ResolveDeploymentConfiguredAsync(\"x\"); }")]);

        accessor.Select(v => v.Rule).Should().Contain(r => r.Contains("names a secret resolver"));
        other.Select(v => v.Rule).Should().Contain(r => r.Contains("deployment-configured path"));
        smtpOk.Should().BeEmpty();
    }

    [Fact]
    public void Self_test_the_allowed_files_are_not_flagged_for_what_they_are_allowed_to_do()
    {
        var violations = Scan(
        [
            Source(Boundary, "class B { ISecretResolver r; IIntegrationSettingsResolver s; void M() => new SecretRef(\"x\"); }"),
            Source("Apperture.ConnectorResolution/ConnectionMaterialResolver.cs", "class C { void M() => new SecretRef(\"x\"); }"),
        ]);

        violations.Should().BeEmpty();
    }
}
