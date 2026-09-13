using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// UIX-7 closure: the Management UI stylesheet is generated from committed Tailwind
/// sources, not hand-maintained. These guards keep source and output from drifting
/// back into the pre-closure state where app.css was its own only source.
/// </summary>
public sealed class Uix7CssPipelineTests
{
    private const string ClientRoot = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client";
    private const string EntrySource = ClientRoot + "/styles/app.css";
    private const string ThemeSource = ClientRoot + "/styles/theme.css";
    private const string GeneratedCss = ClientRoot + "/wwwroot/css/app.css";
    private const string PrimitivesCss = ClientRoot + "/wwwroot/css/management-ui-primitives.css";
    private const string UiAssetsScript = "scripts/lib/Apperture.UiAssets.ps1";

    /// <summary>Pinned in scripts/lib/Apperture.UiAssets.ps1; bump both together.</summary>
    private const string PinnedTailwindVersion = "4.1.8";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull("tests must run from inside the repository");
        return dir!.FullName;
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"expected source at {path}");
        return File.ReadAllText(path);
    }

    // --- Source of truth exists ---

    [Fact]
    public void Authored_tailwind_sources_exist()
    {
        ReadRepoFile(EntrySource).Should().NotBeNullOrWhiteSpace();
        ReadRepoFile(ThemeSource).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Entry_source_imports_tailwind_and_the_theme_source()
    {
        var entry = ReadRepoFile(EntrySource);

        entry.Should().Contain("@import \"tailwindcss\"");
        entry.Should().Contain("@import \"./theme.css\"");
    }

    [Fact]
    public void Entry_source_scans_markup_and_the_shared_assembly_class_literals()
    {
        var entry = ReadRepoFile(EntrySource);

        entry.Should().Contain("@source \"../**/*.razor\"");
        entry.Should().Contain("@source \"../**/*.cs\"");
        entry.Should().Contain("@source \"../../Apperture.Management.Ui.Shared/**/*.cs\"",
            "Epic33UiDisplayHelpers maps status values to text-success/text-warning/text-error");
    }

    // --- Generated output contract ---

    [Fact]
    public void Generated_stylesheet_carries_the_pinned_tailwind_version()
    {
        var generated = ReadRepoFile(GeneratedCss);

        generated.Should().StartWith($"/*! tailwindcss v{PinnedTailwindVersion}",
            "the committed output must match the version pinned by build-ui");
    }

    [Fact]
    public void Generated_stylesheet_is_marked_as_generated()
    {
        var generated = ReadRepoFile(GeneratedCss);

        generated.Should().Contain("GENERATED FILE");
        generated.Should().Contain("build-ui",
            "the marker must tell the reader how to regenerate instead of hand-editing");
    }

    // --- Source/output ownership: no stranded theme definitions ---

    [Fact]
    public void Semantic_theme_tokens_are_authored_in_the_theme_source()
    {
        var theme = ReadRepoFile(ThemeSource);

        theme.Should().Contain(":root {");
        theme.Should().Contain(".dark {");
        theme.Should().Contain("--color-surface:");
        theme.Should().Contain("--color-accent:");
    }

    [Fact]
    public void Authored_component_rules_live_in_the_theme_source_not_only_in_the_output()
    {
        var theme = ReadRepoFile(ThemeSource);

        theme.Should().Contain(".btn {");
        theme.Should().Contain(".btn-primary {");
        theme.Should().Contain(".focus-ring {");
        theme.Should().Contain(".nav-rail-btn {");
    }

    [Fact]
    public void Every_theme_token_in_the_generated_output_is_declared_by_the_authored_source()
    {
        var theme = ReadRepoFile(ThemeSource);
        var generated = ReadRepoFile(GeneratedCss);

        var authored = SemanticTokens(theme, ":root");
        var emitted = SemanticTokens(generated, ":root");

        emitted.Should().NotBeEmpty();
        emitted.Except(authored).Should().BeEmpty(
            "a token present only in generated output would be unreproducible and lost on the next build-ui");
    }

    // --- Light/dark sibling parity (UIX-7 invariant) ---

    [Fact]
    public void Dark_theme_overrides_every_theme_dependent_token_and_adds_none_of_its_own()
    {
        var generated = ReadRepoFile(GeneratedCss);

        var light = SemanticTokens(generated, ":root");
        var dark = SemanticTokens(generated, ".dark");

        // The only light-only tokens are theme-independent by design: 3 font
        // families and 3 radii. Everything else must have a dark sibling.
        var lightOnly = light.Except(dark).ToList();
        var darkOnly = dark.Except(light).ToList();

        darkOnly.Should().BeEmpty("dark must be a sibling of light, not a separate token set");
        lightOnly.Should().BeEquivalentTo(new[]
        {
            "--font-sans", "--font-serif", "--font-mono", "--r-sm", "--r-md", "--r-lg"
        }, "only fonts and radii are theme-independent");
    }

    [Fact]
    public void Generated_output_preserves_the_full_semantic_token_surface()
    {
        var generated = ReadRepoFile(GeneratedCss);

        SemanticTokens(generated, ":root").Should().HaveCount(149);
        SemanticTokens(generated, ".dark").Should().HaveCount(143);
    }

    // --- Primitives stay separate ---

    [Fact]
    public void Primitives_remain_a_separately_authored_stylesheet()
    {
        var primitives = ReadRepoFile(PrimitivesCss);
        var entry = ReadRepoFile(EntrySource);

        primitives.Should().Contain("ui-page-header");

        // Referencing them in a comment is fine; importing or scanning them is not.
        entry.Should().NotContain("@import \"./../wwwroot/css/management-ui-primitives.css\"");
        entry.Should().NotMatchRegex(@"@(?:import|source)\s+""[^""]*management-ui-primitives[^""]*""",
            "primitives are loaded by the host document, not compiled through Tailwind");
    }

    // --- The build script is real, not a scaffold ---

    [Fact]
    public void Ui_asset_script_runs_the_pinned_tailwind_cli_instead_of_the_npm_scaffold()
    {
        var script = ReadRepoFile(UiAssetsScript);

        script.Should().NotContain("UI asset pipeline not configured yet");
        script.Should().NotContain("npm run");
        script.Should().NotContain("package.json");

        script.Should().Contain($"'{PinnedTailwindVersion}'", "the Tailwind version must stay pinned");
        script.Should().Contain("tailwindcss-windows-x64.exe");
        script.Should().Contain("Get-FileHash", "downloads must be checksum verified");
    }

    /// <summary>
    /// Collects <c>--*</c> custom property names declared directly in the given
    /// top-level selector's block, ignoring nested blocks.
    /// </summary>
    private static HashSet<string> SemanticTokens(string css, string selector)
    {
        var lines = css.Replace("\r\n", "\n").Split('\n');
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var declaration = new Regex(@"^\s*(--[a-zA-Z0-9-]+)\s*:", RegexOptions.Compiled);

        var start = Array.FindIndex(lines, l => l.Trim() == selector + " {");
        if (start < 0)
            return tokens;

        var depth = 0;
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            var depthBefore = depth;

            foreach (var ch in line)
            {
                if (ch == '{') depth++;
                else if (ch == '}') depth--;
            }

            if (depthBefore == 1)
            {
                var match = declaration.Match(line);
                if (match.Success)
                    tokens.Add(match.Groups[1].Value);
            }

            if (i > start && depth == 0)
                break;
        }

        return tokens;
    }
}
