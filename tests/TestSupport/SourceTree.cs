using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace Apperture.TestSupport;

/// <summary>
/// The repository's production source files (<c>src/**</c>) for the source-reading coverage guards. Every guard that
/// scans source asks this class for its files, so the repository is walked once per test process and a guard can no
/// longer pass because a scan found nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pruning.</b> The walk does not descend into folders named exactly <c>bin</c>, <c>obj</c> or <c>artifacts</c> (lower case, as
/// the tooling creates them; a source folder named <c>Artifacts</c> or <c>BIN</c> is kept) at any depth below <c>src</c>. A walk that filtered
/// those paths after enumerating them still paid for every file of every build output, which on a developer machine with
/// old builds in place is the whole cost of a source guard.
/// </para>
/// <para>
/// <b>Caching.</b> The list of files is walked once per process and kept for its life (file contents are read fresh by each caller).
/// <c>dotnet test</c> starts a new process per run; a host that is reused between runs would not see a file added in between.
/// <c>node_modules</c> and <c>TestResults</c> are not pruned here (<c>src</c> has none); <c>RepositorySource</c> still filters them.
/// </para>
/// <para>
/// <b>Untracked files.</b> The walk reads the working tree, not the git index, so a new file that is not yet committed is
/// scanned like any other. A guard therefore still fails for an offender added locally before it is staged.
/// </para>
/// <para>
/// <b>Views.</b> A guard states the files it covers: a subtree, the extensions, and whether <c>Migrations</c> folders
/// are part of the corpus. Sharing one walk does not share one boundary; each guard keeps the boundary it had.
/// </para>
/// <para>
/// <b>Floor.</b> Each request states how many files it expects at least. A request that finds fewer throws, so a wrong
/// root, a missing folder or an over-eager prune turns the guard red rather than green.
/// </para>
/// </remarks>
internal static class SourceTree
{
    /// <summary>
    /// Lower bounds for the standard views, about two thirds of the tracked file counts when they were set, so that
    /// ordinary deletion and consolidation never trips them while a failed scan always does. They detect a catastrophic loss (a wrong
    /// root, a missing folder, an over-eager prune), not a partial one: losing a whole project can still leave a view above its
    /// floor, and a guard that must see a particular file asserts that itself. Tracked counts then: src
    /// <c>.cs</c> 1,780 (1,618 outside Migrations); src <c>.cs</c> and <c>.razor</c> 1,967; ControlPlane <c>.cs</c> 580 and
    /// <c>.razor</c>/<c>.cshtml</c>/<c>.js</c>/<c>.cs</c> 770; src <c>.razor</c> and <c>.cshtml</c> 189; Persistence <c>.cs</c> 288;
    /// Ui.Client <c>.razor</c> 185, <c>.cs</c> and <c>.razor</c> 227, Pages <c>.razor</c> 98.
    /// </summary>
    public static class Floor
    {
        public const int CSharp = 1_200;
        public const int CSharpOutsideMigrations = 1_050;
        public const int CSharpAndRazor = 1_300;
        public const int SourceRazor = 125;
        public const int ControlPlaneCSharp = 380;
        public const int ControlPlaneSource = 500;
        public const int PersistenceCSharp = 190;
        public const int UiClientRazor = 120;
        public const int UiClientSource = 150;
        public const int UiClientPagesRazor = 65;
    }

    private static readonly string[] BuildOutput = ["bin", "obj", "artifacts"];
    private const string MigrationsFolder = "Migrations";

    private static readonly ConcurrentDictionary<string, Lazy<string[]>> Walks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The files below <paramref name="subtree"/> (a repository-relative folder under <c>src</c>, for example <c>src</c> or
    /// <c>src/Apperture.ControlPlane</c>) whose name ends with one of <paramref name="extensions"/>, as absolute paths in
    /// ordinal order, for the repository above the running test binaries.
    /// </summary>
    public static IReadOnlyList<string> Files(string subtree, IReadOnlyCollection<string> extensions, bool includeMigrations, int minimum) =>
        Files(RepositoryRoot.Find(), subtree, extensions, includeMigrations, minimum);

    /// <summary>As <see cref="Files(string, IReadOnlyCollection{string}, bool, int)"/>, for an explicit repository root.</summary>
    /// <exception cref="InvalidOperationException">Fewer than <paramref name="minimum"/> files were found.</exception>
    public static IReadOnlyList<string> Files(string repositoryRoot, string subtree, IReadOnlyCollection<string> extensions, bool includeMigrations, int minimum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimum, 1);
        if (extensions.Count == 0 || extensions.Any(e => !e.StartsWith('.')))
            throw new ArgumentException("Extensions are required and start with a dot, for example '.cs'.", nameof(extensions));

        var src = Path.GetFullPath(Path.Combine(repositoryRoot, "src"));
        var wanted = Path.GetFullPath(Path.Combine(repositoryRoot, subtree.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = wanted.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!prefix.StartsWith(src + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{subtree}' is not a folder under src.", nameof(subtree));

        var matched = Walks.GetOrAdd(src, key => new Lazy<string[]>(() => Walk(key))).Value
            .Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(path => extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            .Where(path => includeMigrations || !InMigrationsFolder(path, prefix.Length))
            .ToArray();

        if (matched.Length < minimum)
        {
            throw new InvalidOperationException(
                $"The source scan of '{subtree}' ({string.Join(", ", extensions)}{(includeMigrations ? ", Migrations included" : string.Empty)}) found {matched.Length} file(s) under '{repositoryRoot}', "
                + $"fewer than the {minimum} it must find. A guard over an empty or shrunken corpus would pass without checking anything: check the repository root and the build-output pruning.");
        }

        return matched;
    }

    /// <summary>Every file below <paramref name="src"/> except those under a build-output folder, in ordinal path order.</summary>
    private static string[] Walk(string src)
    {
        if (!Directory.Exists(src))
            throw new InvalidOperationException($"The repository has no src folder at '{src}'.");

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
        };
        var files = new FileSystemEnumerable<string>(src, static (ref FileSystemEntry entry) => entry.ToFullPath(), options)
        {
            ShouldIncludePredicate = static (ref FileSystemEntry entry) => !entry.IsDirectory,
            ShouldRecursePredicate = static (ref FileSystemEntry entry) => !IsBuildOutput(entry.FileName),
        }.ToList();
        files.Sort(StringComparer.Ordinal);
        return [.. files];
    }

    // Exact, lower-case names: what MSBuild and the .NET tooling create, and what the guards' own filters matched. A source folder
    // that merely resembles one (Artifacts, BIN) is real source and stays in the corpus.
    private static bool IsBuildOutput(ReadOnlySpan<char> directoryName)
    {
        foreach (var name in BuildOutput)
        {
            if (directoryName.Equals(name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>True when a <c>Migrations</c> folder lies between the subtree and the file.</summary>
    private static bool InMigrationsFolder(string path, int subtreePrefixLength)
    {
        var below = path.AsSpan(subtreePrefixLength);
        var separator = Path.DirectorySeparatorChar;
        var from = 0;
        while (true)
        {
            var next = below[from..].IndexOf(separator);
            if (next < 0)
                return false;
            if (below.Slice(from, next).Equals(MigrationsFolder, StringComparison.Ordinal))
                return true;
            from += next + 1;
        }
    }
}
