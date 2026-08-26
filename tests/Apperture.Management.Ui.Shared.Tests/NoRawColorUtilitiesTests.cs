using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Enforces the design-system rule that feature markup uses semantic tokens, not raw palettes.
/// </summary>
public sealed class NoRawColorUtilitiesTests
{
    private static readonly Regex HexLiteral = new(@"#[0-9a-fA-F]{3,8}\b", RegexOptions.Compiled);
    private static readonly Regex ArbitraryColor = new(
        @"\b(?:bg|text|border|ring|fill|stroke|from|to)-\[(?:#|rgb|hsl|oklch)",
        RegexOptions.Compiled);
    private static readonly Regex PaletteUtility = new(
        @"\b(?:bg|text|border|ring|fill|stroke)-(?:slate|gray|zinc|neutral|stone|blue|red|green|amber|yellow|emerald|cyan|sky|indigo|violet|purple|orange|teal|rose|lime|fuchsia|pink)-\d{2,3}(?:/\d{1,3})?\b",
        RegexOptions.Compiled);
    private static readonly Regex DarkVariant = new(@"\bdark:(?:bg|text|border|ring)-", RegexOptions.Compiled);
    private static readonly Regex InterpolatedClass = new(
        @"\$""[^""]*(?:bg|text|border)-\{",
        RegexOptions.Compiled);

    [Fact]
    public void Razor_markup_does_not_use_raw_palette_or_dark_color_variants()
    {
        var clientRoot = Path.Combine(FindRepoRoot(), "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client");
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(clientRoot, "*.razor", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            var relative = Path.GetRelativePath(clientRoot, path).Replace('\\', '/');
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("ui-theme-card__swatch--", StringComparison.Ordinal))
                    continue; // preview swatches intentionally show light/dark sample hexes in CSS class names only

                if (ArbitraryColor.IsMatch(line) || PaletteUtility.IsMatch(line) || DarkVariant.IsMatch(line) || InterpolatedClass.IsMatch(line))
                    violations.Add($"{relative}:{i + 1}: {line.Trim()}");

                // Hex in razor class attributes is banned; allow SVG path data / comments separately by ignoring lines without Tailwind-ish context.
                if (HexLiteral.IsMatch(line)
                    && (line.Contains("class=", StringComparison.Ordinal)
                        || line.Contains("style=", StringComparison.Ordinal)
                        || line.Contains("bg-[", StringComparison.Ordinal)
                        || line.Contains("text-[", StringComparison.Ordinal)
                        || line.Contains("border-[", StringComparison.Ordinal)))
                {
                    violations.Add($"{relative}:{i + 1}: {line.Trim()}");
                }
            }
        }

        violations.Should().BeEmpty(
            "feature razor must use semantic tokens; raw palette / dark: / arbitrary colors / interpolated class fragments are banned:\n"
            + string.Join('\n', violations.Take(40)));
    }

    [Fact]
    public void Hand_written_primitives_css_does_not_hardcode_theme_hex_fallbacks()
    {
        var cssPath = Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client",
            "wwwroot",
            "css",
            "management-ui-primitives.css");
        var css = File.ReadAllText(cssPath);

        // Preview swatches and intentional token definitions may include hex; ban the old navy + blue focus fallbacks.
        css.Should().NotContain("#2F3E52");
        css.Should().NotContain("#3B4A5F");
        css.Should().NotContain("#2563eb");
        css.Should().NotContain("rgba(0, 0, 0, 0.25)");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "AGENTS.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repo root.");
    }
}
