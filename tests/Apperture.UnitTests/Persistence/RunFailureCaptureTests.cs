using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Persistence;

public sealed class RunFailureCaptureTests
{
    [Fact]
    public void TryCapture_DefaultPolicy_ReturnsSummaryOnly()
    {
        var r = RunFailureCapture.TryCapture(new InvalidOperationException("boom"));

        r.FailureSummary.Should().Be("boom");
        r.FailureDiagnosticsStatus.Should().Be(RunFailureCapture.StatusSummaryOnly);
        r.FailureDiagnosticsJson.Should().BeNull();
        r.FailureDiagnosticsDisplayPolicy.Should().Be(RunFailureCapture.DisplayPolicySummaryOnly);
        r.FailureDiagnosticsVersion.Should().Be(RunFailureCapture.SchemaVersion);
    }

    [Fact]
    public void TryCapture_InlinePolicy_ReturnsCapturedDiagnosticsJson()
    {
        var r = RunFailureCapture.TryCapture(new InvalidOperationException("boom"), RunFailureCapture.DisplayPolicyInline);

        r.FailureDiagnosticsStatus.Should().Be(RunFailureCapture.StatusCaptured);
        r.FailureDiagnosticsJson.Should().Contain("boom");
        r.FailureDiagnosticsDisplayPolicy.Should().Be(RunFailureCapture.DisplayPolicyInline);
    }

    [Fact]
    public void TryCapture_WithheldPolicy_ReturnsWithheldWithoutDiagnosticsJson()
    {
        var r = RunFailureCapture.TryCapture(new InvalidOperationException("boom"), RunFailureCapture.DisplayPolicyWithheld);

        r.FailureDiagnosticsStatus.Should().Be(RunFailureCapture.StatusWithheld);
        r.FailureDiagnosticsJson.Should().BeNull();
        r.FailureDiagnosticsDisplayPolicy.Should().Be(RunFailureCapture.DisplayPolicyWithheld);
    }
}
