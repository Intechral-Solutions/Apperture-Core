using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Kernel;

public sealed class ResultTests
{
    [Fact]
    public void Ok_returns_success_with_value()
    {
        var r = Result.Ok(42);
        r.IsSuccess.Should().BeTrue();
        r.Value.Should().Be(42);
        r.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_returns_failure_with_error()
    {
        var r = Result.Fail<int>("bad");
        r.IsSuccess.Should().BeFalse();
        r.Error.Should().Be("bad");
        r.Invoking(x => _ = x.Value).Should().Throw<InvalidOperationException>();
    }
}
