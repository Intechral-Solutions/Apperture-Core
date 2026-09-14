using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public class ResourceContextViewScopeTests
{
    [Fact]
    public void All_IsAll_AndHasNoResourceContextId()
    {
        var scope = ResourceContextViewScope.All;
        scope.IsAll.Should().BeTrue();
        scope.ResourceContextId.Should().BeNull();
    }

    [Fact]
    public void All_ToApiFilter_ReturnsNull()
    {
        ResourceContextViewScope.All.ToApiFilter().Should().BeNull();
    }

    [Fact]
    public void All_ToQueryValue_ReturnsAllToken()
    {
        ResourceContextViewScope.All.ToQueryValue().Should().Be("all");
    }

    [Fact]
    public void Of_WithAnId_IsNotAll_AndReturnsThatIdFromApiFilterAndQueryValue()
    {
        var scope = ResourceContextViewScope.Of("canvas-id");
        scope.IsAll.Should().BeFalse();
        scope.ResourceContextId.Should().Be("canvas-id");
        scope.ToApiFilter().Should().Be("canvas-id");
        scope.ToQueryValue().Should().Be("canvas-id");
    }

    [Fact]
    public void Of_WithShared_BehavesAsAnOrdinaryId()
    {
        var scope = ResourceContextViewScope.Of("shared");
        scope.IsAll.Should().BeFalse();
        scope.ResourceContextId.Should().Be("shared");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyOrWhitespace_ReturnsNullMeaningUnspecified(string? value)
    {
        ResourceContextViewScope.Parse(value).Should().BeNull();
    }

    [Fact]
    public void Parse_AllToken_ReturnsAll()
    {
        var scope = ResourceContextViewScope.Parse("all");
        scope.Should().NotBeNull();
        scope!.Value.IsAll.Should().BeTrue();
    }

    [Fact]
    public void Parse_AnId_ReturnsOfThatId()
    {
        var scope = ResourceContextViewScope.Parse("canvas-id");
        scope.Should().NotBeNull();
        scope!.Value.ResourceContextId.Should().Be("canvas-id");
    }

    [Fact]
    public void Of_NullOrWhitespace_Throws()
    {
        var act = () => ResourceContextViewScope.Of("  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void All_EqualsAll_AndOfSameId_EqualsEachOther()
    {
        (ResourceContextViewScope.All == ResourceContextViewScope.All).Should().BeTrue();
        (ResourceContextViewScope.Of("x") == ResourceContextViewScope.Of("x")).Should().BeTrue();
        (ResourceContextViewScope.Of("x") == ResourceContextViewScope.Of("y")).Should().BeFalse();
        (ResourceContextViewScope.All == ResourceContextViewScope.Of("all")).Should().BeFalse();
    }
}
