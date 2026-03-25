using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Domain.Workflow;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Integration;

public sealed class IntegrationFleetExpectedRunAttentionTests
{
    private static readonly IntegrationId IntId = new("int01");
    private static readonly DateTimeOffset Utc = DateTimeOffset.Parse("2025-06-15T12:00:00Z");
    private static readonly DateTimeOffset CreatedOld = DateTimeOffset.Parse("2025-01-01T00:00:00Z");
    private static readonly DateTimeOffset CreatedNew = Utc - TimeSpan.FromHours(1);

    private static IntegrationReadinessProjection RunnableReadiness() =>
        new(IntId.Value, "Sample.Mod", true, 0, 0, Array.Empty<IntegrationReadinessIssue>());

    private static IntegrationFleetAttentionFlags NoBlockAttention() =>
        new(false, false, false, false, false, false, false);

    private static ScheduleDefinition EnabledSchedule() =>
        new(
            new ScheduleId("s1"),
            "N",
            "0 * * * *",
            "UTC",
            true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            null,
            1,
            IntId);

    [Fact]
    public void BuildExpectedRunAttention_inactive_no_expectation_or_silence()
    {
        var op = IntegrationFleetAttention.BuildOperationalRunAttention(
            "Inactive", Utc, 0, null, null, null, null, null);
        var a = IntegrationFleetAttention.BuildExpectedRunAttention(
            "Inactive",
            CreatedOld,
            Utc,
            NoBlockAttention(),
            RunnableReadiness(),
            new[] { EnabledSchedule() },
            IntId,
            op);
        a.HasRecurringScheduleExpectation.Should().BeFalse();
        a.HasUnexpectedSilence.Should().BeFalse();
    }

    [Fact]
    public void BuildExpectedRunAttention_blocked_no_silence()
    {
        var attention = new IntegrationFleetAttentionFlags(
            false, false, true, false, false, false, false);
        var op = IntegrationFleetAttention.BuildOperationalRunAttention(
            "Active", Utc, 0, null, null, null, null, null);
        var a = IntegrationFleetAttention.BuildExpectedRunAttention(
            "Active",
            CreatedOld,
            Utc,
            attention,
            RunnableReadiness(),
            new[] { EnabledSchedule() },
            IntId,
            op);
        a.HasRecurringScheduleExpectation.Should().BeFalse();
        a.HasUnexpectedSilence.Should().BeFalse();
    }

    [Fact]
    public void BuildExpectedRunAttention_no_schedule_no_silence()
    {
        var op = IntegrationFleetAttention.BuildOperationalRunAttention(
            "Active", Utc, 0, null, null, null, null, null);
        var a = IntegrationFleetAttention.BuildExpectedRunAttention(
            "Active",
            CreatedOld,
            Utc,
            NoBlockAttention(),
            RunnableReadiness(),
            Array.Empty<ScheduleDefinition>(),
            IntId,
            op);
        a.HasRecurringScheduleExpectation.Should().BeFalse();
        a.HasUnexpectedSilence.Should().BeFalse();
    }

    [Fact]
    public void BuildExpectedRunAttention_inside_grace_no_silence()
    {
        var op = IntegrationFleetAttention.BuildOperationalRunAttention(
            "Active", Utc, 0, null, null, null, null, null);
        var a = IntegrationFleetAttention.BuildExpectedRunAttention(
            "Active",
            CreatedNew,
            Utc,
            NoBlockAttention(),
            RunnableReadiness(),
            new[] { EnabledSchedule() },
            IntId,
            op);
        a.HasRecurringScheduleExpectation.Should().BeTrue();
        a.HasUnexpectedSilence.Should().BeFalse();
    }

    [Fact]
    public void BuildExpectedRunAttention_has_authorized_run_no_silence()
    {
        var op = IntegrationFleetAttention.BuildOperationalRunAttention(
            "Active",
            Utc,
            0,
            null,
            null,
            "run-1",
            WorkflowLifecycleStatusNames.Completed,
            Utc.AddDays(-1));
        var a = IntegrationFleetAttention.BuildExpectedRunAttention(
            "Active",
            CreatedOld,
            Utc,
            NoBlockAttention(),
            RunnableReadiness(),
            new[] { EnabledSchedule() },
            IntId,
            op);
        a.HasRecurringScheduleExpectation.Should().BeTrue();
        a.HasUnexpectedSilence.Should().BeFalse();
    }

    [Fact]
    public void BuildExpectedRunAttention_eligible_no_run_is_silent()
    {
        var op = IntegrationFleetAttention.BuildOperationalRunAttention(
            "Active", Utc, 0, null, null, null, null, null);
        var a = IntegrationFleetAttention.BuildExpectedRunAttention(
            "Active",
            CreatedOld,
            Utc,
            NoBlockAttention(),
            RunnableReadiness(),
            new[] { EnabledSchedule() },
            IntId,
            op);
        a.HasRecurringScheduleExpectation.Should().BeTrue();
        a.HasUnexpectedSilence.Should().BeTrue();
        a.ReferenceScheduleId.Should().Be("s1");
    }
}
