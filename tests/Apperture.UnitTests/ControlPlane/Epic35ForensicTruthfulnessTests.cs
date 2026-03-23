using System;
using Apperture.ControlPlane.Security;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Epic 35 Corrective: Forensic truthfulness proof. Ensures unknown stays unknown,
/// inferred relationships are caveated, and the UI/API contract does not overclaim.
/// </summary>
public sealed class Epic35ForensicTruthfulnessTests
{
    [Fact]
    public void Unknown_OriginDetail_ExplicitlyStatesInsufficientMetadata()
    {
        var (_, detail, _, _) = RunOriginClassifier.Classify(
            retryOfRunId: null,
            startIdempotencyKey: null,
            startCorrelationId: null,
            hasStartRequest: false);

        Assert.Contains("insufficient", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No persisted", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("schedule", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manual", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_OriginKind_FormattedAsUnknown()
    {
        Epic33UiDisplayHelpers.FormatRunOriginKind(RunOriginKindNames.Unknown).Should().Be("Unknown");
        Epic33UiDisplayHelpers.FormatRunOriginKind("unknown").Should().Be("Unknown");
        Epic33UiDisplayHelpers.FormatRunOriginKind(null).Should().Be("Unknown");
    }

    [Fact]
    public void ScheduleHint_WhenTargetIdNull_ReturnsPlaceholder_SoUiDoesNotLink()
    {
        Epic33UiDisplayHelpers.GetRunInvestigationHintHref("schedule", null).Should().Be("#");
        Epic33UiDisplayHelpers.GetRunInvestigationHintHref("module", null).Should().Be("#");
        Epic33UiDisplayHelpers.GetRunInvestigationHintHref("audit", null).Should().Be("#");
    }

    [Fact]
    public void ModuleLabel_MentionsFromDefinition_SoUiDoesNotOverclaim()
    {
        Assert.Contains("definition", Epic33UiDisplayHelpers.RunForensicModuleLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Module", Epic33UiDisplayHelpers.RunForensicModuleLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScheduleDisclaimer_ExplicitlyStatesNotPersistedAssociation()
    {
        Assert.Contains("inferred", Epic33UiDisplayHelpers.RunScheduleInferenceDisclaimer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a separate persisted", Epic33UiDisplayHelpers.RunScheduleInferenceDisclaimer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_Unknown_ReturnsNullScheduleId()
    {
        var (kind, _, scheduleId, fireUtc) = RunOriginClassifier.Classify(
            retryOfRunId: null,
            startIdempotencyKey: null,
            startCorrelationId: null,
            hasStartRequest: false);

        kind.Should().Be(RunOriginKindNames.Unknown);
        scheduleId.Should().BeNull();
        fireUtc.Should().BeNull();
    }

    [Fact]
    public void Classify_Manual_ReturnsNullScheduleId_NoFalseScheduleClaim()
    {
        var (kind, _, scheduleId, _) = RunOriginClassifier.Classify(
            retryOfRunId: null,
            startIdempotencyKey: "custom-api-key",
            startCorrelationId: null,
            hasStartRequest: true);

        kind.Should().Be(RunOriginKindNames.Manual);
        scheduleId.Should().BeNull();
    }

    [Fact]
    public void Classify_Retry_ReturnsNullScheduleId_NoFalseScheduleClaim()
    {
        var (kind, _, scheduleId, _) = RunOriginClassifier.Classify(
            retryOfRunId: "parent-123",
            startIdempotencyKey: "schedule:s1:2026-01-01T00:00:00Z",
            startCorrelationId: "schedule:s1",
            hasStartRequest: true);

        kind.Should().Be(RunOriginKindNames.Retry);
        scheduleId.Should().BeNull();
    }
}
