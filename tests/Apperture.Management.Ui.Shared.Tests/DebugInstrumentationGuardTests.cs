using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class DebugInstrumentationGuardTests
{
    [Fact]
    public void Operator_ui_sources_do_not_call_the_debug_agent_log()
    {
        var root = RepoRoot();
        var hits = Directory.EnumerateFiles(Path.Combine(root, "src/Apperture.ControlPlane"), "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("appertureShell.debugLog", StringComparison.Ordinal)
                || File.ReadAllText(path).Contains("debug-4bd3d8.log", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path)
            .ToArray();

        hits.Should().BeEmpty();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }
}
