using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Domain.Workflow;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Integration;

public sealed class IntegrationFleetExpectedRunAttentionTests
{
    private static readonly IntegrationId IntId = new("int01");

    private static IntegrationReadinessProjection RunnableReadiness() =>
        new(IntId.Value, "Sample.Mod", true, 0, 0, Array.Empty<IntegrationReadinessIssue>());

    private static IntegrationFleetAttentionFlags NoBlockAttention() =>
        new(false, false, false, false, false, false, false);

    private static ScheduleDefinition EnabledWorkflowSchedule(string cron = "0 * * * *") =>
        new(
            new ScheduleId("s1"),
            "N",
            cron,
            "UTC",
            true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            null,
            1,
            IntId);

    [Fact]
    public void FromScheduleExpectationEvaluation_missing_expected_run_maps_to_silence_attention()
    {
        var eval = new ScheduleExpectationEvaluation(
            true,
            true,
            3,
            DateTimeOffset.Parse("2025-06-15T11:00:00Z"),
            false,
            false,
            null,
            null,
            ScheduleExpectationEvaluationStatus.MissingExpectedRun,
            "No schedule-origin run was found after the last expected fire time.");
        var a = IntegrationFleetAttention.FromScheduleExpectationEvaluation(
            eval,
            "s1",
            DateTimeOffset.Parse("2025-06-08T00:00:00Z"),
            null,
            null);
        a.HasRecurringScheduleExpectation.Should().BeTrue();
        a.HasUnexpectedSilence.Should().BeTrue();
        a.ExpectedRunEvaluationStatus.Should().Be(ScheduleExpectationEvaluationStatus.MissingExpectedRun.ToString());
        a.ExpectedRunSatisfied.Should().BeFalse();
        a.OperatorSummaryLine.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void FromScheduleExpectationEvaluation_grace_is_not_silent()
    {
        var eval = new ScheduleExpectationEvaluation(
            true,
            true,
            1,
            DateTimeOffset.Parse("2025-06-15T10:00:00Z"),
            true,
            false,
            null,
            null,
            ScheduleExpectationEvaluationStatus.Grace,
            "Within grace period after integration creation.",
            ScheduleExpectationGraceReason.IntegrationCreated,
            DateTimeOffset.Parse("2025-06-01T00:00:00Z"));
        var a = IntegrationFleetAttention.FromScheduleExpectationEvaluation(
            eval,
            "s1",
            DateTimeOffset.Parse("2025-06-08T00:00:00Z"),
            "run-1",
            DateTimeOffset.Parse("2025-06-15T09:00:00Z"));
        a.HasUnexpectedSilence.Should().BeFalse();
        a.ExpectedRunInGracePeriod.Should().BeTrue();
        a.ExpectedRunGraceReason.Should().Be(nameof(ScheduleExpectationGraceReason.IntegrationCreated));
        a.ExpectedRunGraceAnchorUtc.Should().Be(DateTimeOffset.Parse("2025-06-01T00:00:00Z"));
    }

    [Fact]
    public void FromScheduleExpectationEvaluation_no_expectation_is_not_silent()
    {
        var eval = new ScheduleExpectationEvaluation(
            true,
            false,
            0,
            null,
            false,
            false,
            null,
            null,
            ScheduleExpectationEvaluationStatus.NoExpectation,
            "No run was due during the observation period.");
        var a = IntegrationFleetAttention.FromScheduleExpectationEvaluation(
            eval,
            "s1",
            DateTimeOffset.Parse("2025-06-08T00:00:00Z"),
            null,
            null);
        a.HasUnexpectedSilence.Should().BeFalse();
        a.ExpectedFireCountInWindow.Should().Be(0);
    }

    [Fact]
    public void ScheduleExpectationEvaluator_end_to_end_uses_cron_window()
    {
        var utcNow = DateTimeOffset.Parse("2025-01-15T12:00:00Z");
        var windowStart = utcNow - IntegrationFleetRunEvidence.Lookback;
        var evaluator = new ScheduleExpectationEvaluator();
        var monthly = EnabledWorkflowSchedule("0 0 1 * *");
        var ctx = new ScheduleExpectationEvaluationContext(
            "Active",
            utcNow,
            windowStart,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            NoBlockAttention(),
            RunnableReadiness(),
            IntId,
            new[] { monthly },
            null);
        var r = evaluator.Evaluate(ctx);
        r.Status.Should().Be(ScheduleExpectationEvaluationStatus.NoExpectation);
        r.HasExpectedFireInWindow.Should().BeFalse();
        r.ExpectedFireCountInWindow.Should().Be(0);
    }

    [Fact]
    public void ScheduleExpectationEvaluator_schedule_origin_before_last_expected_fire_is_not_satisfied()
    {
        var utcNow = DateTimeOffset.Parse("2025-01-01T15:00:00Z");
        var windowStart = utcNow - IntegrationFleetRunEvidence.Lookback;
        var hourly = EnabledWorkflowSchedule("0 * * * *");
        var evaluator = new ScheduleExpectationEvaluator();
        var ctx = new ScheduleExpectationEvaluationContext(
            "Active",
            utcNow,
            windowStart,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            NoBlockAttention(),
            RunnableReadiness(),
            IntId,
            new[] { hourly },
            new ScheduleOriginRunEvidenceInput(
                "run-old",
                DateTimeOffset.Parse("2025-01-01T08:00:00Z")));
        var r = evaluator.Evaluate(ctx);
        r.Status.Should().Be(ScheduleExpectationEvaluationStatus.MissingExpectedRun);
        r.SatisfiedByAuthorizedScheduleOriginRun.Should().BeFalse();
        r.MostRecentExpectedFireUtc.Should().BeAfter(DateTimeOffset.Parse("2025-01-01T08:00:00Z"));
    }

    [Fact]
    public void ScheduleExpectationEvaluator_disabled_schedule_yields_no_recurring_expectation()
    {
        var utcNow = DateTimeOffset.Parse("2025-01-01T12:00:00Z");
        var windowStart = utcNow - IntegrationFleetRunEvidence.Lookback;
        var disabled = new ScheduleDefinition(
            new ScheduleId("s-off"),
            "N",
            "0 0 * * *",
            "UTC",
            false,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            null,
            1,
            IntId);
        var evaluator = new ScheduleExpectationEvaluator();
        var ctx = new ScheduleExpectationEvaluationContext(
            "Active",
            utcNow,
            windowStart,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            NoBlockAttention(),
            RunnableReadiness(),
            IntId,
            new[] { disabled },
            null);
        var r = evaluator.Evaluate(ctx);
        r.ExpectsRecurringRuns.Should().BeFalse();
        r.Status.Should().Be(ScheduleExpectationEvaluationStatus.NoExpectation);
        r.OperatorSummaryLine.Should().Be("No run was due because the schedule is disabled.");
    }
}
