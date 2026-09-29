using System.Reflection;
using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Apperture.BrowserTests;

/// <summary>
/// Architectural pin: the harness's test-only controls (server clock, request recorder, stub provider,
/// attacker origins, CORS/OIDC/data-protection substitutions) exist ONLY in this test assembly and are wired
/// ONLY by <see cref="LiveManagementHost"/>. No production project can reference them, so no production
/// composition can activate them.
/// <para>
/// These are ordinary fast tests (no browser, no SQL): they run in a plain <c>dotnet test Apperture.slnx</c>
/// and deliberately are NOT gated by <c>APPERTURE_BROWSER_TESTS</c>.
/// </para>
/// </summary>
public sealed class HarnessIsolationGuardTests
{
    private static readonly string[] TestOnlyTokens =
    [
        "Apperture.BrowserTests",
        "APPERTURE_BROWSER_TESTS",
        nameof(ControllableTimeProvider),
        nameof(RequestRecorder),
        nameof(StubOidcProvider),
        nameof(AttackerHost),
        nameof(LiveManagementHost),
        nameof(BrowserHarnessSecretProtector),
    ];

    [Fact]
    public void No_production_project_references_any_test_project()
    {
        var root = RepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("tests\\", StringComparison.OrdinalIgnoreCase)
                || File.ReadAllText(f).Contains("tests/", StringComparison.OrdinalIgnoreCase)
                || File.ReadAllText(f).Contains("BrowserTests", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        offenders.Should().BeEmpty("production projects must never depend on a test project");
    }

    [Fact]
    public void Production_source_never_mentions_the_harness_or_its_test_only_controls()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        string[] scanned = [".cs", ".cshtml", ".razor", ".json", ".js", ".html", ".props", ".targets", ".csproj", ".yml", ".yaml", ".ps1"];
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => scanned.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            // Solution-wide build files that would apply to every production project.
            .Concat(new[] { "Directory.Build.props", "Directory.Build.targets" }
                .Select(name => Path.Combine(root, name))
                .Where(File.Exists));
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in TestOnlyTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                    offenders.Add($"{Path.GetRelativePath(root, file)}: {token}");
            }
        }

        offenders.Should().BeEmpty("no production file may reference a harness type, switch or environment variable");
    }

    [Fact]
    public void Test_only_types_live_only_in_the_test_assembly_and_no_production_assembly_references_it()
    {
        var harnessAssembly = typeof(ControllableTimeProvider).Assembly;
        harnessAssembly.GetName().Name.Should().Be("Apperture.BrowserTests");

        var production = new[]
        {
            typeof(Apperture.Management.Host.ManagementHostWebApplicationFactoryEntryPoint).Assembly,
            typeof(Apperture.Management.Api.Controllers.AuthController).Assembly,
            typeof(Apperture.ControlPlane.Security.EntraAuthOptions).Assembly,
            typeof(Apperture.Persistence.SqlServer.AppertureDbContext).Assembly,
            typeof(Apperture.Bootstrap.IBootstrapConnectionProfile).Assembly,
            typeof(Apperture.Management.Ui.Shared.Auth.LoginRouteHelper).Assembly,
        };
        foreach (var assembly in production)
        {
            assembly.GetReferencedAssemblies().Select(a => a.Name)
                .Should().NotContain("Apperture.BrowserTests", $"{assembly.GetName().Name} must not reference the harness");
        }
    }

    [Fact]
    public void The_browser_suite_is_opt_in_and_off_by_default()
    {
        // The variable is unset in an ordinary run, so every browser test reports Skipped and no host, SQL
        // connection or browser is started (the fixture returns immediately when the gate is closed).
        // Only the exact value "1" opens the gate, whatever else the variable holds.
        var value = Environment.GetEnvironmentVariable(BrowserTestGate.EnvironmentVariable);
        BrowserTestGate.Enabled.Should().Be(value == "1");
        if (!BrowserTestGate.Enabled)
        {
            new BrowserFactAttribute().Skip.Should().Be(BrowserTestGate.SkipReason);
            new BrowserTheoryAttribute().Skip.Should().Be(BrowserTestGate.SkipReason);
        }
    }

    private static string RepoRoot() =>
        BrowserSqlConfiguration.FindRepoRoot() ?? throw new InvalidOperationException("Repository root (.git) not found.");
}
