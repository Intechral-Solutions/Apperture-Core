using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Scheduling;

public sealed class ScheduleLifecycleEpic66Tests
{
    private static readonly IntegrationId IntId = new("int01");

    private static IntegrationReadinessProjection RunnableReadiness() =>
        new(IntId.Value, "Sample.Mod", true, 0, 0, Array.Empty<IntegrationReadinessIssue>());

    private static IntegrationFleetAttentionFlags NoBlockAttention() =>
        new(false, false, false, false, false, false, false);

    private static ScheduleDefinition BaseWorkflow(string cron = "0 * * * *") =>
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
    public void MaterialSemanticsComparer_detects_cron_or_timezone_change()
    {
        var a = BaseWorkflow("0 * * * *");
        var b = a with { RecurrenceExpression = "0 0 * * *" };
        ScheduleMaterialSemanticsComparer.HasMaterialSemanticsChange(a, b).Should().BeTrue();
        ScheduleMaterialSemanticsComparer.HasMaterialSemanticsChange(a, a with { TimeZone = "Eastern Standard Time" }).Should().BeTrue();
        ScheduleMaterialSemanticsComparer.HasMaterialSemanticsChange(a, a with { Name = "Other" }).Should().BeFalse();
    }

    [Fact]
    public void Applier_create_enabled_sets_enable_and_material_baseline()
    {
        var def = BaseWorkflow();
        var now = DateTimeOffset.Parse("2025-03-01T10:00:00Z");
        var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(null, def, now);
        merged.Lifecycle.Should().NotBeNull();
        merged.Lifecycle!.LastScheduleEnabledAtUtc.Should().Be(now);
        merged.Lifecycle.MaterialScheduleSemanticsChangedAtUtc.Should().Be(now);
        merged.Lifecycle.LastScheduleDisabledAtUtc.Should().BeNull();
    }

    [Fact]
    public void Applier_create_disabled_sets_disabled_only()
    {
        var def = BaseWorkflow() with { Enabled = false };
        var now = DateTimeOffset.Parse("2025-03-01T10:00:00Z");
        var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(null, def, now);
        merged.Lifecycle!.LastScheduleDisabledAtUtc.Should().Be(now);
        merged.Lifecycle.LastScheduleEnabledAtUtc.Should().BeNull();
        merged.Lifecycle.MaterialScheduleSemanticsChangedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Applier_idempotent_name_change_preserves_lifecycle()
    {
        var prior = BaseWorkflow() with
        {
            Lifecycle = new ScheduleLifecycleEvidence(
                DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
                null,
                DateTimeOffset.Parse("2025-01-01T00:00:00Z"))
        };
        var proposed = prior with { Name = "Renamed" };
        var now = DateTimeOffset.Parse("2025-06-01T00:00:00Z");
        var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(prior, proposed, now);
        merged.Lifecycle.Should().Be(prior.Lifecycle);
    }

    [Fact]
    public void Applier_material_change_advances_material_timestamp()
    {
        var prior = BaseWorkflow("0 * * * *") with
        {
            Lifecycle = new ScheduleLifecycleEvidence(
                DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
                null,
                DateTimeOffset.Parse("2025-01-01T00:00:00Z"))
        };
        var proposed = prior with { RecurrenceExpression = "0 0 * * *" };
        var now = DateTimeOffset.Parse("2025-06-01T12:00:00Z");
        var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(prior, proposed, now);
        merged.Lifecycle!.MaterialScheduleSemanticsChangedAtUtc.Should().Be(now);
        merged.Lifecycle.LastScheduleEnabledAtUtc.Should().Be(prior.Lifecycle!.LastScheduleEnabledAtUtc);
    }

    [Fact]
    public void Applier_re_enable_sets_new_enabled_timestamp()
    {
        var prior = BaseWorkflow() with
        {
            Enabled = false,
            Lifecycle = new ScheduleLifecycleEvidence(
                DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
                DateTimeOffset.Parse("2025-02-01T00:00:00Z"),
                DateTimeOffset.Parse("2025-01-01T00:00:00Z"))
        };
        var proposed = prior with { Enabled = true };
        var now = DateTimeOffset.Parse("2025-06-01T00:00:00Z");
        var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(prior, proposed, now);
        merged.Lifecycle!.LastScheduleEnabledAtUtc.Should().Be(now);
    }

    [Fact]
    public void Evaluator_schedule_lifecycle_grace_suppresses_missing_expected_run()
    {
        var utcNow = DateTimeOffset.Parse("2025-01-01T15:00:00Z");
        var windowStart = utcNow - IntegrationFleetRunEvidence.Lookback;
        var anchor = DateTimeOffset.Parse("2025-01-01T14:00:00Z");
        var hourly = BaseWorkflow("0 * * * *") with
        {
            Lifecycle = new ScheduleLifecycleEvidence(anchor, null, anchor)
        };
        var evaluator = new ScheduleExpectationEvaluator();
        var ctx = new ScheduleExpectationEvaluationContext(
            "Active",
            utcNow,
            windowStart,
            DateTimeOffset.Parse("2020-01-01T00:00:00Z"),
            NoBlockAttention(),
            RunnableReadiness(),
            IntId,
            new[] { hourly },
            null);
        var r = evaluator.Evaluate(ctx);
        r.Status.Should().Be(ScheduleExpectationEvaluationStatus.Grace);
        r.InGracePeriod.Should().BeTrue();
        r.GraceReason.Should().Be(ScheduleExpectationGraceReason.RecentlyEnabled);
        r.GraceAnchorUtc.Should().Be(anchor);
    }

    [Fact]
    public void Evaluator_after_grace_window_missing_expected_run_still_actionable()
    {
        var utcNow = DateTimeOffset.Parse("2025-01-05T15:00:00Z");
        var windowStart = utcNow - IntegrationFleetRunEvidence.Lookback;
        var anchor = DateTimeOffset.Parse("2025-01-01T00:00:00Z");
        var hourly = BaseWorkflow("0 * * * *") with
        {
            Lifecycle = new ScheduleLifecycleEvidence(anchor, null, anchor)
        };
        var evaluator = new ScheduleExpectationEvaluator();
        var ctx = new ScheduleExpectationEvaluationContext(
            "Active",
            utcNow,
            windowStart,
            DateTimeOffset.Parse("2020-01-01T00:00:00Z"),
            NoBlockAttention(),
            RunnableReadiness(),
            IntId,
            new[] { hourly },
            null);
        var r = evaluator.Evaluate(ctx);
        r.Status.Should().Be(ScheduleExpectationEvaluationStatus.MissingExpectedRun);
        r.InGracePeriod.Should().BeFalse();
    }

    [Fact]
    public void Evaluator_material_change_anchor_prefers_recently_changed_reason()
    {
        var utcNow = DateTimeOffset.Parse("2025-01-01T15:00:00Z");
        var windowStart = utcNow - IntegrationFleetRunEvidence.Lookback;
        var enabledAt = DateTimeOffset.Parse("2025-01-01T08:00:00Z");
        var materialAt = DateTimeOffset.Parse("2025-01-01T14:30:00Z");
        var hourly = BaseWorkflow("0 * * * *") with
        {
            Lifecycle = new ScheduleLifecycleEvidence(enabledAt, null, materialAt)
        };
        var evaluator = new ScheduleExpectationEvaluator();
        var ctx = new ScheduleExpectationEvaluationContext(
            "Active",
            utcNow,
            windowStart,
            DateTimeOffset.Parse("2020-01-01T00:00:00Z"),
            NoBlockAttention(),
            RunnableReadiness(),
            IntId,
            new[] { hourly },
            null);
        var r = evaluator.Evaluate(ctx);
        r.GraceReason.Should().Be(ScheduleExpectationGraceReason.RecentlyMateriallyChanged);
        r.GraceAnchorUtc.Should().Be(materialAt);
    }
}
