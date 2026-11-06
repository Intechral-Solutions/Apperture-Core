using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Conventions;

/// <summary>
/// The shared source corpus behind the coverage guards: it prunes build output, keeps each guard's boundary (Migrations in
/// or out), sees files that are not committed, and fails instead of returning nothing.
/// </summary>
public sealed class SourceTreeTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "apperture-source-tree-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_scratch))
                Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
            // A scanner holding the folder briefly must not fail the test; the folder is under the system temp path.
        }
    }

    [Fact]
    public void Build_output_is_not_part_of_the_corpus()
    {
        var root = Repository(
            "src/Area/Real.cs",
            "src/Area/bin/Debug/net10.0/Copy.cs",
            "src/Area/obj/Debug/Generated.cs",
            "src/Area/artifacts/old-build/obj/Stray.cs",
            "src/Area/Nested/bin/Deeper/obj/Deep.cs",
            "src/Area/Nested/Kept.cs");

        Names(SourceTree.Files(root, "src", [".cs"], includeMigrations: true, minimum: 1))
            .Should().Equal("src/Area/Nested/Kept.cs", "src/Area/Real.cs");
    }

    [Fact]
    public void A_checkout_that_itself_sits_under_a_folder_named_like_build_output_is_still_scanned()
    {
        // A worktree can live under artifacts/ or bin/ (a nested worktree does). Only folders below src are pruned; a filter on the
        // whole path would have emptied the corpus and let every guard pass.
        var root = Repository("artifacts/nested-worktree/bin/checkout/src/Area/Real.cs", "artifacts/nested-worktree/bin/checkout/src/Area/obj/Generated.cs");
        var checkout = Path.Combine(root, "artifacts", "nested-worktree", "bin", "checkout");

        SourceTree.Files(checkout, "src", [".cs"], includeMigrations: true, minimum: 1)
            .Select(path => Path.GetRelativePath(checkout, path).Replace('\\', '/'))
            .Should().Equal("src/Area/Real.cs");
    }

    [Fact]
    public void A_source_folder_that_only_resembles_build_output_is_kept()
    {
        // Build output is bin, obj and artifacts exactly as the tooling spells them. A real source folder such as Artifacts or BIN
        // is part of the corpus: pruning it would let an offender in it pass every guard.
        var root = Repository("src/Area/Real.cs", "src/Area/BIN/Upper.cs", "src/Area/Obj/Mixed.cs", "src/Area/Artifacts/Domain.cs");

        Names(SourceTree.Files(root, "src", [".cs"], includeMigrations: true, minimum: 1))
            .Should().Equal("src/Area/Artifacts/Domain.cs", "src/Area/BIN/Upper.cs", "src/Area/Obj/Mixed.cs", "src/Area/Real.cs");
    }

    [Fact]
    public void Migrations_are_in_or_out_of_the_corpus_as_each_guard_asks()
    {
        var root = Repository("src/Persistence/Context.cs", "src/Persistence/Migrations/20260101_Init.cs", "src/Persistence/Migrations/Snapshot.cs", "src/Other/Thing.cs");

        Names(SourceTree.Files(root, "src", [".cs"], includeMigrations: true, minimum: 1))
            .Should().Equal("src/Other/Thing.cs", "src/Persistence/Context.cs", "src/Persistence/Migrations/20260101_Init.cs", "src/Persistence/Migrations/Snapshot.cs");
        Names(SourceTree.Files(root, "src", [".cs"], includeMigrations: false, minimum: 1))
            .Should().Equal("src/Other/Thing.cs", "src/Persistence/Context.cs");
    }

    [Fact]
    public void A_subtree_and_the_extensions_narrow_the_view_without_another_walk()
    {
        var root = Repository("src/Client/Page.razor", "src/Client/Page.razor.cs", "src/Client/site.css", "src/Server/Program.cs", "src/Server/Page.razor");

        Names(SourceTree.Files(root, "src/Client", [".razor"], includeMigrations: true, minimum: 1)).Should().Equal("src/Client/Page.razor");
        Names(SourceTree.Files(root, "src/Client", [".razor", ".cs"], includeMigrations: true, minimum: 1)).Should().Equal("src/Client/Page.razor", "src/Client/Page.razor.cs");
        Names(SourceTree.Files(root, "src", [".cs"], includeMigrations: true, minimum: 1)).Should().Equal("src/Client/Page.razor.cs", "src/Server/Program.cs");
    }

    [Fact]
    public void A_file_that_is_not_committed_is_scanned_like_any_other()
    {
        // The corpus reads the working tree, not the git index: there is no repository here at all, and every file is "untracked".
        var root = Repository("src/Area/Clean.cs", "src/Area/NewOffender.cs");
        File.WriteAllText(Path.Combine(root, "src", "Area", "NewOffender.cs"), "var actor = new ActorId(\"someone-new\");");

        var offenders = SourceTree.Files(root, "src", [".cs"], includeMigrations: true, minimum: 1)
            .Where(path => Regex.IsMatch(File.ReadAllText(path), "new ActorId\\(\"someone-new\"\\)"))
            .ToList();

        Names(offenders).Should().Equal("src/Area/NewOffender.cs");
    }

    [Fact]
    public void An_empty_corpus_fails_instead_of_passing_a_guard()
    {
        var root = Repository();
        Directory.CreateDirectory(Path.Combine(root, "src"));

        var scan = () => SourceTree.Files(root, "src", [".cs"], includeMigrations: true, minimum: 1);

        scan.Should().Throw<InvalidOperationException>().WithMessage("*found 0 file(s)*");
    }

    [Fact]
    public void A_corpus_that_is_only_build_output_fails()
    {
        var root = Repository("src/Area/bin/Debug/Copy.cs", "src/Area/obj/Generated.cs");

        var scan = () => SourceTree.Files(root, "src", [".cs"], includeMigrations: true, minimum: 1);

        scan.Should().Throw<InvalidOperationException>().WithMessage("*found 0 file(s)*");
    }

    [Fact]
    public void A_corpus_below_the_floor_fails()
    {
        var root = Repository("src/Area/One.cs", "src/Area/Two.cs");

        var scan = () => SourceTree.Files(root, "src", [".cs"], includeMigrations: true, minimum: 3);

        scan.Should().Throw<InvalidOperationException>().WithMessage("*found 2 file(s)*fewer than the 3*");
    }

    [Fact]
    public void A_missing_subtree_fails_instead_of_returning_nothing()
    {
        var root = Repository("src/Area/One.cs");

        var scan = () => SourceTree.Files(root, "src/Gone", [".cs"], includeMigrations: true, minimum: 1);

        scan.Should().Throw<InvalidOperationException>().WithMessage("*found 0 file(s)*");
    }

    [Fact]
    public void A_missing_src_folder_fails()
    {
        var root = Repository();

        var scan = () => SourceTree.Files(root, "src", [".cs"], includeMigrations: true, minimum: 1);

        scan.Should().Throw<InvalidOperationException>().WithMessage("*no src folder*");
    }

    [Theory]
    [InlineData("tests")]
    [InlineData("src/../tests")]
    [InlineData("")]
    public void Only_folders_under_src_can_be_requested(string subtree)
    {
        var root = Repository("src/Area/One.cs", "tests/Project/Two.cs");

        var scan = () => SourceTree.Files(root, subtree, [".cs"], includeMigrations: true, minimum: 1);

        scan.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Extensions_are_required_and_start_with_a_dot()
    {
        var root = Repository("src/Area/One.cs");

        ((Action)(() => SourceTree.Files(root, "src", [], includeMigrations: true, minimum: 1))).Should().Throw<ArgumentException>();
        ((Action)(() => SourceTree.Files(root, "src", ["cs"], includeMigrations: true, minimum: 1))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Every_standard_view_of_this_repository_meets_its_floor()
    {
        // The floors are set below today's tracked counts; this fails if a view shrinks below one (a wrong root, an over-eager prune).
        const string UiClient = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client";

        SourceTree.Files("src", [".cs"], includeMigrations: true, SourceTree.Floor.CSharp).Should().NotBeEmpty();
        SourceTree.Files("src", [".cs"], includeMigrations: false, SourceTree.Floor.CSharpOutsideMigrations).Should().NotBeEmpty();
        SourceTree.Files("src", [".cs", ".razor"], includeMigrations: true, SourceTree.Floor.CSharpAndRazor).Should().NotBeEmpty();
        SourceTree.Files("src", [".razor", ".cshtml"], includeMigrations: false, SourceTree.Floor.SourceRazor).Should().NotBeEmpty();
        SourceTree.Files("src/Apperture.ControlPlane", [".cs"], includeMigrations: true, SourceTree.Floor.ControlPlaneCSharp).Should().NotBeEmpty();
        SourceTree.Files("src/Apperture.ControlPlane", [".razor", ".cshtml", ".js", ".cs"], includeMigrations: true, SourceTree.Floor.ControlPlaneSource).Should().NotBeEmpty();
        SourceTree.Files("src/Apperture.Persistence", [".cs"], includeMigrations: true, SourceTree.Floor.PersistenceCSharp).Should().NotBeEmpty();
        SourceTree.Files(UiClient, [".razor"], includeMigrations: true, SourceTree.Floor.UiClientRazor).Should().NotBeEmpty();
        SourceTree.Files(UiClient, [".cs", ".razor"], includeMigrations: true, SourceTree.Floor.UiClientSource).Should().NotBeEmpty();
        SourceTree.Files(UiClient + "/Pages", [".razor"], includeMigrations: true, SourceTree.Floor.UiClientPagesRazor).Should().NotBeEmpty();
    }

    [Fact]
    public void The_real_corpus_holds_no_build_output_and_reaches_the_migrations_the_components_guard_reads()
    {
        var separator = Path.DirectorySeparatorChar;
        var all = SourceTree.Files("src", [".cs"], includeMigrations: true, SourceTree.Floor.CSharp);

        all.Should().NotContain(path => path.Contains($"{separator}bin{separator}") || path.Contains($"{separator}obj{separator}") || path.Contains($"{separator}artifacts{separator}"));
        all.Should().Contain(path => path.Contains($"{separator}Apperture.Persistence.SqlServer{separator}Migrations{separator}"), "the Migrations-inclusive view reaches the platform migrations");
        SourceTree.Files("src", [".cs"], includeMigrations: false, SourceTree.Floor.CSharpOutsideMigrations)
            .Should().NotContain(path => path.Contains($"{separator}Migrations{separator}"));
    }

    private string Repository(params string[] files)
    {
        Directory.CreateDirectory(_scratch);
        foreach (var file in files)
        {
            var full = Path.Combine(_scratch, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "// " + file);
        }

        return _scratch;
    }

    private List<string> Names(IEnumerable<string> paths) =>
        paths.Select(path => Path.GetRelativePath(_scratch, path).Replace('\\', '/')).ToList();
}
