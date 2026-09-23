using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Conventions;

/// <summary>
/// Prevents internal development metadata (epic/track/agent labels) from leaking into
/// curated runtime-visible seed/demo/menu/setting surfaces.
/// Docs, ADRs, audits, tests, migrations, and code comments are intentionally out of scope.
/// Operator UI must not carry epic/track labels. Notification delivery no longer has a cutover dual-path.
/// </summary>
public sealed class RuntimeVisibleDevMetadataGuardTests
{
    private static readonly Regex[] Forbidden =
    [
        new(@"\bEpic\s*\d+[A-Z]?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bSDK-\d+[A-Z]?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bSWM-\d+[A-Z]?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bBDS-\d+[A-Z]?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bRR-\d+[A-Z]?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bPFB-\d+[A-Z]?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bCMP-\d+[A-Z]?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"Cursor\s+agent", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"closure\s+report", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"implementation\s+pass", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    ];

    public static TheoryData<string> RuntimeVisiblePaths
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var path in EnumerateScanTargets())
                data.Add(path);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(RuntimeVisiblePaths))]
    public void Curated_runtime_visible_surfaces_do_not_contain_dev_metadata(string relativePath)
    {
        var fullPath = RepoPath(relativePath);
        File.Exists(fullPath).Should().BeTrue("scan target missing: {0}", relativePath);

        var raw = File.ReadAllText(fullPath);
        var content = StripComments(raw, Path.GetExtension(fullPath));

        foreach (var pattern in Forbidden)
        {
            var match = pattern.Match(content);
            match.Success.Should().BeFalse(
                "Forbidden development metadata '{0}' found in runtime-visible file {1}. " +
                "Use product-like names; keep epic/track labels in docs/ADRs/tests/comments. Match: '{2}'",
                pattern,
                relativePath,
                match.Success ? match.Value : "");
        }
    }

    [Fact]
    public void Guard_covers_expected_seed_and_demo_surfaces()
    {
        var targets = EnumerateScanTargets().ToHashSet(StringComparer.OrdinalIgnoreCase);
        targets.Should().Contain(p => p.EndsWith("ExpectedRunMissingNotificationDefinitionSeed.cs", StringComparison.OrdinalIgnoreCase));
        targets.Should().Contain(p => p.EndsWith("PlatformSettingDefinitionRegistry.cs", StringComparison.OrdinalIgnoreCase));
        targets.Should().Contain(p => p.EndsWith("ControlPanelMenuDefinitions.cs", StringComparison.OrdinalIgnoreCase));
        targets.Should().Contain(p => p.EndsWith("DeveloperTasks.razor", StringComparison.OrdinalIgnoreCase));
        targets.Should().Contain(p => p.Contains("Sample.FileImport", StringComparison.OrdinalIgnoreCase));
        targets.Should().Contain(p => p.Contains("Sample.ApiPolling", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateScanTargets()
    {
        yield return "src/Apperture.Core/Apperture.Core.Application/Notifications/ExpectedRunMissingNotificationDefinitionSeed.cs";
        yield return "src/Apperture.Core/Apperture.Core.Application/Notifications/HelloWorldDemoNotificationDefinitionSeed.cs";
        yield return "src/Apperture.Core/Apperture.Core.Application/Notifications/FileImportPartialFailureNotificationDefinitionSeed.cs";
        yield return "src/Apperture.Core/Apperture.Core.Application/Notifications/ApiPollingPartialFailureNotificationDefinitionSeed.cs";
        yield return "src/Apperture.Core/Apperture.Core.Application/PlatformSettings/PlatformSettingDefinitionRegistry.cs";
        yield return "src/Apperture.Core/Apperture.Core.Application/PlatformSettings/PlatformSettingDemoDefinitionRegistry.cs";
        yield return "src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ControlPanelMenuDefinitions.cs";
        yield return "src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/DemoCustomerUiHelpers.cs";
        yield return "src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/PlatformSettingUiHelpers.cs";
        yield return "src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ComponentCatalogPresentation.cs";
        yield return "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/ComponentCatalog/ComponentsList.razor";
        yield return "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/DeveloperTasks.razor";
        yield return "src/Apperture.Modules/Sample.HelloWorld/Sample.HelloWorld.Module/HelloWorldModule.cs";
        yield return "src/Apperture.Modules/Sample.HelloWorld/Sample.HelloWorld.Module/HelloWorldIntegrationConfiguration.cs";
        yield return "src/Apperture.Modules/Sample.FileImport/Sample.FileImport.Module/FileImportModule.cs";
        yield return "src/Apperture.Modules/Sample.FileImport/Sample.FileImport.Module/FileImportIntegrationConfiguration.cs";
        yield return "src/Apperture.Modules/Sample.ApiPolling/Sample.ApiPolling.Module/ApiPollingModule.cs";
        yield return "src/Apperture.Modules/Sample.ApiPolling/Sample.ApiPolling.Module/ApiPollingIntegrationConfiguration.cs";

        foreach (var snapshot in Directory.EnumerateFiles(
                     RepoPath("src/Apperture.Modules/Sample.ApiPolling/Sample.ApiPolling.Module/Snapshots"),
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            yield return ToRepoRelative(snapshot);
        }

        var fixturesDir = RepoPath("src/Apperture.Modules/Sample.FileImport/Sample.FileImport.Module/Fixtures");
        if (Directory.Exists(fixturesDir))
        {
            foreach (var fixture in Directory.EnumerateFiles(fixturesDir, "*.*", SearchOption.AllDirectories)
                         .Where(f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                                     || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                yield return ToRepoRelative(fixture);
            }
        }
    }

    private static string StripComments(string content, string extension)
    {
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return content;

        // Block comments (C# /* */ and Razor @* *@ / HTML <!-- -->).
        content = Regex.Replace(content, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        content = Regex.Replace(content, @"@\*.*?\*@", " ", RegexOptions.Singleline);
        content = Regex.Replace(content, @"<!--.*?-->", " ", RegexOptions.Singleline);

        // Line comments // and /// (not URLs http://).
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var inString = false;
            for (var j = 0; j < line.Length - 1; j++)
            {
                var c = line[j];
                if (c == '"' && (j == 0 || line[j - 1] != '\\'))
                    inString = !inString;
                if (inString)
                    continue;
                if (c == '/' && line[j + 1] == '/')
                {
                    lines[i] = line[..j];
                    break;
                }
            }
        }

        return string.Join('\n', lines);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }

    private static string RepoPath(string relative) =>
        Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    private static string ToRepoRelative(string absolutePath)
    {
        var root = RepoRoot();
        var full = Path.GetFullPath(absolutePath);
        full.StartsWith(root, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        return full[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace('\\', '/');
    }
}
