using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Module detail shows what a module contributes as a compact facts row. These are Module metadata, not
/// Components: workflows, operations, permissions and navigation are deliberately not catalog kinds.
/// </summary>
public sealed class ModuleContributionPresentationTests
{
    [Fact]
    public void Summary_orders_the_known_kinds_and_pluralizes_them()
    {
        var counts = new Dictionary<string, int>
        {
            ["navigation"] = 1,
            ["permission"] = 3,
            ["workflow"] = 2,
            ["operation"] = 1,
        };

        ModuleContributionPresentation.Summary(counts)
            .Should().Be("2 workflows · 1 operation · 3 permissions · 1 navigation item");
    }

    [Fact]
    public void Summary_omits_zero_counts_and_appends_unknown_kinds_verbatim()
    {
        var counts = new Dictionary<string, int> { ["workflow"] = 0, ["operation"] = 2, ["future-kind"] = 4 };

        ModuleContributionPresentation.Summary(counts).Should().Be("2 operations · 4 future-kind");
    }

    [Fact]
    public void Summary_is_null_when_nothing_is_declared_so_the_row_can_be_omitted()
    {
        ModuleContributionPresentation.Summary(null).Should().BeNull();
        ModuleContributionPresentation.Summary(new Dictionary<string, int>()).Should().BeNull();
        ModuleContributionPresentation.Summary(new Dictionary<string, int> { ["workflow"] = 0 }).Should().BeNull();
    }

    [Fact]
    public void Contributions_are_not_catalog_kinds()
    {
        // The Component catalog covers connectors and supplied notification definitions only.
        var summary = ModuleContributionPresentation.Summary(new Dictionary<string, int> { ["workflow"] = 1 })!;

        summary.Should().NotContain("component", "workflows are module contributions, not Components");
    }
}
