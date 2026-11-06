using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.UnitTests.Conventions;

/// <summary>
/// The shared repository-root helper recognises an ordinary clone (<c>.git</c> directory) and a linked worktree (<c>.git</c>
/// file), so the source-reading tests run in both. A <c>.git</c> directory check alone fails every one of them in a worktree.
/// </summary>
public sealed class RepositoryRootTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "apperture-repository-root-" + Guid.NewGuid().ToString("N"));

    public RepositoryRootTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
            // A scanner holding the folder briefly must not fail the test; the folder is under the system temp path.
        }
    }

    [Fact]
    public void A_git_directory_marks_the_root_of_an_ordinary_clone()
    {
        var root = Folder("clone");
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var nested = Folder("clone", "src", "Area", "bin", "Debug");

        RepositoryRoot.IsRoot(root).Should().BeTrue();
        RepositoryRoot.Find(nested).Should().Be(root);
    }

    [Fact]
    public void A_git_file_marks_the_root_of_a_linked_worktree()
    {
        // A linked worktree keeps a one-line pointer file where a clone keeps the .git directory.
        var root = Folder("worktree");
        File.WriteAllText(Path.Combine(root, ".git"), "gitdir: /elsewhere/.git/worktrees/wt\n");
        var nested = Folder("worktree", "tests", "Project", "bin", "Debug", "net10.0");

        Directory.Exists(Path.Combine(root, ".git")).Should().BeFalse("the marker is a file, which Directory.Exists does not see");
        RepositoryRoot.IsRoot(root).Should().BeTrue();
        RepositoryRoot.Find(nested).Should().Be(root);
    }

    [Fact]
    public void AGENTS_md_marks_the_root_of_a_source_export_that_has_no_git_entry()
    {
        var root = Folder("export");
        File.WriteAllText(Path.Combine(root, "AGENTS.md"), "# agents\n");

        RepositoryRoot.Find(Folder("export", "src", "Area")).Should().Be(root);
    }

    [Fact]
    public void The_nearest_root_wins()
    {
        var outer = Folder("outer");
        Directory.CreateDirectory(Path.Combine(outer, ".git"));
        var inner = Folder("outer", "artifacts", "nested-worktree");
        File.WriteAllText(Path.Combine(inner, ".git"), "gitdir: /elsewhere\n");

        RepositoryRoot.Find(Folder("outer", "artifacts", "nested-worktree", "src")).Should().Be(inner);
    }

    [Fact]
    public void A_folder_without_a_marker_is_not_a_root()
    {
        RepositoryRoot.IsRoot(Folder("plain")).Should().BeFalse();
    }

    [Fact]
    public void The_running_tests_locate_the_repository_they_were_built_from()
    {
        var root = RepositoryRoot.Find();
        var marker = Directory.Exists(Path.Combine(root, ".git")) ? ".git directory (ordinary clone)"
            : File.Exists(Path.Combine(root, ".git")) ? ".git file (linked worktree)"
            : "AGENTS.md only";
        _output.WriteLine($"repository root {root} is marked by a {marker}");

        File.Exists(Path.Combine(root, "Apperture.slnx")).Should().BeTrue("the located folder is the solution root");
        Directory.Exists(Path.Combine(root, "src")).Should().BeTrue();
        Directory.Exists(Path.Combine(root, "tests")).Should().BeTrue();
    }

    private string Folder(params string[] parts)
    {
        var path = Path.Combine([_scratch, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }
}
