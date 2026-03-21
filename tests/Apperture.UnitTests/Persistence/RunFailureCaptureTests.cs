using Apperture.Persistence.SqlServer;
using Apperture.Core.Application.Execution;
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

    [Fact]
    public void TryCapture_DeclaredFailureSummaryOnly_UsesDeclaredSummaryAndPolicy()
    {
        var ex = OperatorExecutionResult.FailSummaryOnly("declared-summary");
        var r = RunFailureCapture.TryCapture(ex);

        r.FailureSummary.Should().Be("declared-summary");
        r.FailureDiagnosticsStatus.Should().Be(RunFailureCapture.StatusSummaryOnly);
        r.FailureDiagnosticsJson.Should().BeNull();
        r.FailureDiagnosticsDisplayPolicy.Should().Be(RunFailureCapture.DisplayPolicySummaryOnly);
    }

    [Fact]
    public void TryCapture_DeclaredFailureInline_CapturesDeclaredDiagnostics()
    {
        var ex = OperatorExecutionResult.FailInline("declared-summary", new { reason = "boom-declared" });
        var r = RunFailureCapture.TryCapture(ex);

        r.FailureSummary.Should().Be("declared-summary");
        r.FailureDiagnosticsStatus.Should().Be(RunFailureCapture.StatusCaptured);
        r.FailureDiagnosticsJson.Should().Contain("boom-declared");
        r.FailureDiagnosticsDisplayPolicy.Should().Be(RunFailureCapture.DisplayPolicyInline);
    }

    [Fact]
    public void TryCapture_DeclaredFailureWithheld_UsesDeclaredSummaryAndWithheldPolicy()
    {
        var ex = OperatorExecutionResult.FailWithheld("declared-withheld");
        var r = RunFailureCapture.TryCapture(ex);

        r.FailureSummary.Should().Be("declared-withheld");
        r.FailureDiagnosticsStatus.Should().Be(RunFailureCapture.StatusWithheld);
        r.FailureDiagnosticsJson.Should().BeNull();
        r.FailureDiagnosticsDisplayPolicy.Should().Be(RunFailureCapture.DisplayPolicyWithheld);
    }
}
