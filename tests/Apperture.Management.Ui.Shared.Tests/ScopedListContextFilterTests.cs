using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public class ScopedListContextFilterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_MissingOrWhitespaceQueryContext_ReturnsAwaitingContext(string? queryContext)
    {
        var resolution = ScopedListContextFilter.Resolve(queryContext, _ => true);

        resolution.State.Should().Be(ScopedListContextState.AwaitingContext);
        resolution.ApiFilter.Should().BeNull();
        resolution.QueryValue.Should().BeNull();
        resolution.IsAll.Should().BeFalse();
    }

    [Fact]
    public void Resolve_All_ReturnsReadyWithNullApiFilter()
    {
        var resolution = ScopedListContextFilter.Resolve("all", _ => true);

        resolution.State.Should().Be(ScopedListContextState.Ready);
        resolution.ApiFilter.Should().BeNull();
        resolution.QueryValue.Should().Be("all");
        resolution.IsAll.Should().BeTrue();
    }

    [Fact]
    public void Resolve_KnownId_ReturnsReadyWithThatIdAsApiFilter()
    {
        var resolution = ScopedListContextFilter.Resolve("app-a", id => id == "app-a");

        resolution.State.Should().Be(ScopedListContextState.Ready);
        resolution.ApiFilter.Should().Be("app-a");
        resolution.QueryValue.Should().Be("app-a");
        resolution.IsAll.Should().BeFalse();
    }

    [Fact]
    public void Resolve_UnknownId_ReturnsUnknownContextWithNullApiFilter()
    {
        var resolution = ScopedListContextFilter.Resolve("ghost-id", _ => false);

        resolution.State.Should().Be(ScopedListContextState.UnknownContext);
        resolution.ApiFilter.Should().BeNull();
        resolution.QueryValue.Should().Be("ghost-id");
        resolution.IsAll.Should().BeFalse();
    }
}
