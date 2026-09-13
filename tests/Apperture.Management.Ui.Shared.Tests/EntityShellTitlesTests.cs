using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class EntityShellTitlesTests
{
    [Theory]
    [InlineData(null, "Connection", "Connection")]
    [InlineData("", "Connection", "Connection")]
    [InlineData("  Payroll DB  ", "Connection", "Payroll DB")]
    public void Detail_uses_display_name_or_section_label(string? displayName, string label, string expected) =>
        EntityShellTitles.Detail(displayName, label).Should().Be(expected);

    [Theory]
    [InlineData("conn-1", "/connections/conn-1", true)]
    [InlineData("CONN-1", "/connections/conn-1", true)]
    [InlineData("Payroll DB", "/connections/conn-1", false)]
    [InlineData("conn-1", "/connections", false)]
    public void LooksLikeRouteKey_detects_terminal_segment(string value, string path, bool expected) =>
        EntityShellTitles.LooksLikeRouteKey(value, EntityShellTitles.NormalizePath(path)).Should().Be(expected);

    [Fact]
    public void NormalizePath_trims_trailing_slash() =>
        EntityShellTitles.NormalizePath("/integrations/int-1/").Should().Be("/integrations/int-1");
}
