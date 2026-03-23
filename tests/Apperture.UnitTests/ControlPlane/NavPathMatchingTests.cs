using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class NavPathMatchingTests
{
    [Theory]
    [InlineData("/config", "/config", true)]
    [InlineData("/config/governance", "/config", true)]
    [InlineData("/configuration", "/config", false)]
    [InlineData("/configx", "/config", false)]
    public void IsPathUnder_ConfigSegmentBoundary(string path, string prefix, bool expected) =>
        NavPathMatching.IsPathUnder(path, prefix).Should().Be(expected);

    [Theory]
    [InlineData("/modules", "/modules", true)]
    [InlineData("/modules/abc", "/modules", true)]
    [InlineData("/modulesx", "/modules", false)]
    public void PathMatchesMenuHref_SegmentBoundary(string path, string href, bool expected) =>
        NavPathMatching.PathMatchesMenuHref(path, href).Should().Be(expected);
}
