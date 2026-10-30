using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Source-closure pins for the disposable-target guard. The behavioural cases live in <see cref="SqlTestTargetGuardTests"/> and
/// <see cref="SqlServerTestConfigurationTests"/>; these pins keep the guard in front of every statement that creates or drops a
/// catalog, which no behavioural test can reach without a live server. Text pins by design: they check call order inside the
/// shared helpers, not behaviour.
/// </summary>
public sealed class SqlTestTargetGuardCoverageTests
{
    private const string Guard = "SqlTestTargetGuard.";

    [Theory]
    [InlineData("tests/Apperture.IntegrationTests/SqlServerTestConfiguration.cs", "CREATE DATABASE")]
    [InlineData("tests/Apperture.IntegrationTests/SqlServerScratchDatabase.cs", "DROP DATABASE")]
    [InlineData("tests/Apperture.IntegrationTests/Audit/AuditSqlScratch.cs", "DROP DATABASE")]
    [InlineData("tests/Apperture.IntegrationTests/ManagementHost/ManagementHostGovernanceFixture.cs", "CREATE DATABASE")]
    [InlineData("tests/Apperture.BrowserTests/Infrastructure/BrowserSqlConfiguration.cs", "DROP DATABASE")]
    public void The_guard_runs_before_the_first_statement_that_creates_or_drops_a_catalog(string relativePath, string statement)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        var guard = source.IndexOf(Guard, StringComparison.Ordinal);
        var destructive = source.IndexOf(statement, StringComparison.Ordinal);

        guard.Should().BeGreaterThanOrEqualTo(0, $"{relativePath} must call the guard");
        destructive.Should().BeGreaterThan(guard, $"{relativePath} must validate the target before it issues {statement}");
    }

    [Fact]
    public void The_governance_fixture_validates_its_catalog_outside_the_catch_that_turns_failures_into_a_skip()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "tests/Apperture.IntegrationTests/ManagementHost/ManagementHostGovernanceFixture.cs"));

        var guard = source.IndexOf(Guard + "EnsureDisposableTarget", StringComparison.Ordinal);
        var tryBlock = source.IndexOf("var probeCs", StringComparison.Ordinal);

        guard.Should().BeGreaterThanOrEqualTo(0);
        guard.Should().BeLessThan(tryBlock, "a refusal inside the bare catch would silently mark SQL unavailable and skip the suite");
        source.Should().Contain("requireTestsSuffix: true", "governance catalogs must end in 'Tests'");
    }

    [Fact]
    public void The_browser_harness_resolves_its_database_through_the_guard()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "tests/Apperture.BrowserTests/Infrastructure/BrowserSqlConfiguration.cs"));

        source.Should().Contain(Guard + "EnsureDisposableTarget");
    }

    [Fact]
    public void Every_test_source_that_creates_or_drops_a_catalog_reaches_its_server_through_a_guarded_entry_point()
    {
        var root = RepoRoot();
        var thisFiles = new[] { nameof(SqlTestTargetGuardCoverageTests), nameof(SqlTestTargetGuardTests), "SqlTestTargetGuard" };
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var project in new[] { "tests/Apperture.IntegrationTests", "tests/Apperture.BrowserTests" })
        {
            foreach (var file in EnumerateSourceFiles(Path.Combine(root, project)))
            {
                if (thisFiles.Any(name => Path.GetFileName(file).Equals(name + ".cs", StringComparison.Ordinal)))
                    continue;

                var text = File.ReadAllText(file);
                if (!text.Contains("CREATE DATABASE", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("DROP DATABASE", StringComparison.OrdinalIgnoreCase))
                    continue;

                inspected++;
                var guarded = text.Contains("SqlServerTestConfiguration.", StringComparison.Ordinal)
                    || text.Contains(Guard, StringComparison.Ordinal)
                    || text.Contains("BrowserSqlConfiguration.", StringComparison.Ordinal);
                if (!guarded)
                    offenders.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
            }
        }

        inspected.Should().BeGreaterThan(10, "the walk must reach the known create and drop sites; an empty walk proves nothing");
        offenders.Should().BeEmpty(
            "a file that creates or drops a catalog must obtain its server from SqlServerTestConfiguration (guarded) or call the guard");
    }

    private static IEnumerable<string> EnumerateSourceFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly))
            yield return file;

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (name is "bin" or "obj" or "artifacts" or "node_modules" or "TestResults")
                continue;

            foreach (var file in EnumerateSourceFiles(child))
                yield return file;
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Apperture.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Repository root (Apperture.slnx) not found above " + AppContext.BaseDirectory);
    }
}
