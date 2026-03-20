using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Persistence;

public sealed class RunOutputCaptureTests
{
    [Fact]
    public void TryCapture_WhenNull_ReturnsEmpty()
    {
        var r = RunOutputCapture.TryCapture(null);

        r.CapturedOutputStatus.Should().Be(RunOutputCapture.StatusEmpty);
        r.CapturedOutputJson.Should().BeNull();
        r.CapturedOutputSummary.Should().BeNull();
        r.CapturedOutputVersion.Should().Be(RunOutputCapture.SchemaVersion);
    }

    [Fact]
    public void TryCapture_WhenSimpleObject_ReturnsCaptured()
    {
        var value = new { message = "Hello", step = "Complete" };
        var r = RunOutputCapture.TryCapture(value);

        r.CapturedOutputStatus.Should().Be(RunOutputCapture.StatusCaptured);
        r.CapturedOutputJson.Should().NotBeNullOrEmpty();
        r.CapturedOutputJson.Should().Contain("Hello");
        r.CapturedOutputJson.Should().Contain("Complete");
        r.CapturedOutputSummary.Should().Be("Hello");
    }

    [Fact]
    public void TryCapture_WhenString_ReturnsCapturedWithSummary()
    {
        var r = RunOutputCapture.TryCapture("done");

        r.CapturedOutputStatus.Should().Be(RunOutputCapture.StatusCaptured);
        r.CapturedOutputJson.Should().Contain("done");
        r.CapturedOutputSummary.Should().Be("done");
    }

    [Fact]
    public void TryCapture_WhenObjectExceedsMaxSize_ReturnsTruncated()
    {
        var big = new string('x', RunOutputCapture.MaxPayloadBytes);
        var r = RunOutputCapture.TryCapture(new { data = big });

        r.CapturedOutputStatus.Should().Be(RunOutputCapture.StatusTruncated);
        r.CapturedOutputJson.Should().NotBeNullOrEmpty();
        r.CapturedOutputSummary.Should().NotBeNullOrEmpty();
    }
}
