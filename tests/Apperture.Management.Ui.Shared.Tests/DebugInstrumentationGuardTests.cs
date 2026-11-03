using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class DebugInstrumentationGuardTests
{
    [Fact]
    public void Operator_ui_sources_do_not_call_the_debug_agent_log()
    {
        var root = RepositoryRoot.Find();
        var hits = SourceTree.Files("src/Apperture.ControlPlane", [".razor", ".cshtml", ".js", ".cs"], includeMigrations: true, SourceTree.Floor.ControlPlaneSource)
            .Where(path => File.ReadAllText(path).Contains("appertureShell.debugLog", StringComparison.Ordinal)
                || File.ReadAllText(path).Contains("debug-4bd3d8.log", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path)
            .ToArray();

        hits.Should().BeEmpty();
    }
}
