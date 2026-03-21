using System;
using Apperture.Core.Application.Execution;
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
        r.CapturedOutputDisplayPolicy.Should().BeNull();
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
        r.CapturedOutputDisplayPolicy.Should().BeNull();
    }

    [Fact]
    public void TryCapture_WhenString_ReturnsCapturedWithSummary()
    {
        var r = RunOutputCapture.TryCapture("done");

        r.CapturedOutputStatus.Should().Be(RunOutputCapture.StatusCaptured);
        r.CapturedOutputJson.Should().Contain("done");
        r.CapturedOutputSummary.Should().Be("done");
        r.CapturedOutputDisplayPolicy.Should().BeNull();
    }

    [Fact]
    public void TryCapture_WhenObjectExceedsMaxSize_ReturnsTruncated()
    {
        var big = new string('x', RunOutputCapture.MaxPayloadBytes);
        var r = RunOutputCapture.TryCapture(new { data = big });

        r.CapturedOutputStatus.Should().Be(RunOutputCapture.StatusTruncated);
        r.CapturedOutputJson.Should().NotBeNullOrEmpty();
        r.CapturedOutputSummary.Should().NotBeNullOrEmpty();
        r.CapturedOutputDisplayPolicy.Should().BeNull();
    }

    [Fact]
    public void TryCapture_WhenJsonSerializationFails_ReturnsUnsupportedWithNullPayloadAndSummary()
    {
        // System.Text.Json does not support delegate serialization; throws → unsupported path.
        var r = RunOutputCapture.TryCapture((Action)(() => { }));

        r.CapturedOutputStatus.Should().Be(RunOutputCapture.StatusUnsupported);
        r.CapturedOutputJson.Should().BeNull();
        r.CapturedOutputSummary.Should().BeNull();
        r.CapturedOutputVersion.Should().Be(RunOutputCapture.SchemaVersion);
        r.CapturedOutputDisplayPolicy.Should().BeNull();
    }

    [Fact]
    public void TryCapture_OperatorRunOutputWithheld_DoesNotPersistPayloadOrSummary()
    {
        var r = RunOutputCapture.TryCapture(OperatorRunOutput.Withheld());

        r.CapturedOutputStatus.Should().Be(RunOutputCapture.StatusWithheld);
        r.CapturedOutputJson.Should().BeNull();
        r.CapturedOutputSummary.Should().BeNull();
        r.CapturedOutputDisplayPolicy.Should().Be(RunOutputCapture.DisplayPolicyWithheld);
    }

    [Fact]
    public void TryCapture_OperatorRunOutputSummaryOnly_StoresSummaryWithoutJson()
    {
        var r = RunOutputCapture.TryCapture(OperatorRunOutput.SummaryOnly(new { message = "Secret headline", detail = "x" }));

        r.CapturedOutputStatus.Should().Be(RunOutputCapture.StatusSummaryOnly);
        r.CapturedOutputJson.Should().BeNull();
        r.CapturedOutputSummary.Should().Be("Secret headline");
        r.CapturedOutputDisplayPolicy.Should().Be(RunOutputCapture.DisplayPolicySummaryOnly);
    }

    [Fact]
    public void TryCapture_OperatorRunOutputInline_MatchesPlainCapturePlusPolicy()
    {
        var value = new { message = "Hi", step = "Complete" };
        var wrapped = RunOutputCapture.TryCapture(OperatorRunOutput.Inline(value));
        var plain = RunOutputCapture.TryCapture(value);

        wrapped.CapturedOutputJson.Should().Be(plain.CapturedOutputJson);
        wrapped.CapturedOutputSummary.Should().Be(plain.CapturedOutputSummary);
        wrapped.CapturedOutputStatus.Should().Be(plain.CapturedOutputStatus);
        wrapped.CapturedOutputDisplayPolicy.Should().Be(RunOutputCapture.DisplayPolicyInline);
    }

    [Fact]
    public void TryCapture_OperatorExecutionResultSuccessInline_MatchesOperatorRunOutputInline()
    {
        var value = new { message = "contract", step = "Complete" };
        var declared = RunOutputCapture.TryCapture(OperatorExecutionResult.SuccessInline(value));
        var wrapped = RunOutputCapture.TryCapture(OperatorRunOutput.Inline(value));

        declared.CapturedOutputStatus.Should().Be(wrapped.CapturedOutputStatus);
        declared.CapturedOutputJson.Should().Be(wrapped.CapturedOutputJson);
        declared.CapturedOutputSummary.Should().Be(wrapped.CapturedOutputSummary);
        declared.CapturedOutputDisplayPolicy.Should().Be(wrapped.CapturedOutputDisplayPolicy);
    }

    [Fact]
    public void TryCapture_OperatorRunOutputSummaryOnly_WhenSerializeFails_IsUnsupported()
    {
        var r = RunOutputCapture.TryCapture(OperatorRunOutput.SummaryOnly((Action)(() => { })));

        r.CapturedOutputStatus.Should().Be(RunOutputCapture.StatusUnsupported);
        r.CapturedOutputJson.Should().BeNull();
        r.CapturedOutputSummary.Should().BeNull();
        r.CapturedOutputDisplayPolicy.Should().Be(RunOutputCapture.DisplayPolicySummaryOnly);
    }
}
