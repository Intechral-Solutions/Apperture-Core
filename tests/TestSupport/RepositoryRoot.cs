namespace Apperture.TestSupport;

/// <summary>
/// Finds the repository root from a test's output folder. One definition for the source-reading tests, so none of them
/// has to know what a root looks like, and so every one of them works in an ordinary clone and in a linked worktree.
/// </summary>
/// <remarks>
/// A folder is the root when it holds a <c>.git</c> directory (ordinary clone), a <c>.git</c> file (a linked worktree
/// or submodule keeps a one-line pointer file there, which <c>Directory.Exists</c> does not see) or the repository's
/// <c>AGENTS.md</c> (a source export has no <c>.git</c> at all). The nearest such folder above the test binaries wins.
/// </remarks>
internal static class RepositoryRoot
{
    private static readonly Lazy<string> FromTestBinaries = new(() => Find(AppContext.BaseDirectory));

    /// <summary>The repository root above the running test binaries. Located once.</summary>
    public static string Find() => FromTestBinaries.Value;

    /// <summary>The repository root at or above <paramref name="startDirectory"/>.</summary>
    /// <exception cref="InvalidOperationException">No folder at or above the start holds a root marker.</exception>
    public static string Find(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (IsRoot(directory.FullName))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Repository root not found: no folder at or above '{startDirectory}' holds a .git directory, a .git file (linked worktree) or AGENTS.md.");
    }

    /// <summary>True when <paramref name="directory"/> holds a root marker.</summary>
    public static bool IsRoot(string directory) =>
        Directory.Exists(Path.Combine(directory, ".git"))
        || File.Exists(Path.Combine(directory, ".git"))
        || File.Exists(Path.Combine(directory, "AGENTS.md"));
}
