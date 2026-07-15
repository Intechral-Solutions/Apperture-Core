using Apperture.Core.Application.Scheduling;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Scheduling;

/// <summary>SWM-10: next-fire projection and readiness classification (compute-only).</summary>
public sealed class ScheduleClaritySwm10Tests
{
    [Fact]
    public void NextFire_Respects_Schedule_Timezone_When_Host_Supports_Iana()
    {
        if (!ScheduleTimeZoneResolver.TryFind("America/Los_Angeles", out _))
            return;

        // 2025-06-15 10:00 America/Los_Angeles = 17:00 UTC (PDT, UTC-7)
        var asOf = new DateTimeOffset(2025, 6, 15, 16, 30, 0, TimeSpan.Zero);
        var next = ScheduleNextFireCalculator.TryComputeNextFireUtc(
            "0 10 * * *",
            "America/Los_Angeles",
            enabled: true,
            asOf);

        next.Should().NotBeNull();
        next!.Value.UtcDateTime.Should().Be(new DateTime(2025, 6, 15, 17, 0, 0, DateTimeKind.Utc));
        next.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void NextFire_Utc_Cron_Is_Normalized()
    {
        var asOf = new DateTimeOffset(2025, 1, 1, 0, 30, 0, TimeSpan.Zero);
        var next = ScheduleNextFireCalculator.TryComputeNextFireUtc(
            "0 1 * * *",
            "UTC",
            enabled: true,
            asOf);

        next.Should().NotBeNull();
        next!.Value.UtcDateTime.Should().Be(new DateTime(2025, 1, 1, 1, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Disabled_Schedule_Has_No_NextFire()
    {
        var next = ScheduleNextFireCalculator.TryComputeNextFireUtc(
            "0 * * * *",
            "UTC",
            enabled: false,
            DateTimeOffset.UtcNow);

        next.Should().BeNull();
    }

    [Fact]
    public void Invalid_Persisted_Timezone_Falls_Back_To_Utc()
    {
        var asOf = new DateTimeOffset(2025, 3, 1, 0, 30, 0, TimeSpan.Zero);
        var next = ScheduleNextFireCalculator.TryComputeNextFireUtc(
            "0 1 * * *",
            "Corrupt/NotAZone",
            enabled: true,
            asOf);

        next.Should().NotBeNull();
        next!.Value.UtcDateTime.Should().Be(new DateTime(2025, 3, 1, 1, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Bounded_LookAhead_Returns_Null_When_Next_Outside_Window()
    {
        var asOf = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // yearly Jan 1 at 00:00 — next after asOf exclusive is 2026-01-01, beyond 30-day window
        var next = ScheduleNextFireCalculator.TryComputeNextFireUtc(
            "0 0 1 1 *",
            "UTC",
            enabled: true,
            asOf,
            lookAheadDays: 30);

        next.Should().BeNull();
    }

    [Fact]
    public void Classifier_Disabled_Is_Inactive()
    {
        var result = ScheduleReadinessClassifier.Classify(new ScheduleReadinessInput(
            Enabled: false,
            IntegrationId: "int-1",
            NextFireUtc: null,
            AsOfUtc: DateTimeOffset.UtcNow,
            WorkerRuntimeStatus: "Healthy",
            IncludeCatchUpPolicyReason: false));

        result.HealthStatus.Should().Be(ScheduleHealthStatuses.Inactive);
        result.StatusReasons.Should().Contain(r => r.Code == ScheduleStatusReasonCodes.Disabled);
    }

    [Fact]
    public void Classifier_NotDueYet_When_Healthy_Enabled_With_NextFire()
    {
        var asOf = DateTimeOffset.UtcNow;
        var result = ScheduleReadinessClassifier.Classify(new ScheduleReadinessInput(
            Enabled: true,
            IntegrationId: "int-1",
            NextFireUtc: asOf.AddHours(1),
            AsOfUtc: asOf,
            WorkerRuntimeStatus: "Healthy",
            IncludeCatchUpPolicyReason: false));

        result.HealthStatus.Should().Be(ScheduleHealthStatuses.Healthy);
        result.StatusReasons.Should().Contain(r => r.Code == ScheduleStatusReasonCodes.NotDueYet);
    }

    [Fact]
    public void Classifier_StaleHeartbeat_And_LastFailed()
    {
        var result = ScheduleReadinessClassifier.Classify(new ScheduleReadinessInput(
            Enabled: true,
            IntegrationId: "int-1",
            NextFireUtc: DateTimeOffset.UtcNow.AddMinutes(5),
            AsOfUtc: DateTimeOffset.UtcNow,
            LastOutcome: "Failed",
            WorkerRuntimeStatus: "Warning",
            IncludeCatchUpPolicyReason: false));

        result.HealthStatus.Should().Be(ScheduleHealthStatuses.Warning);
        result.StatusReasons.Should().Contain(r => r.Code == ScheduleStatusReasonCodes.LastFailed);
        result.StatusReasons.Should().Contain(r => r.Code == ScheduleStatusReasonCodes.SchedulerHeartbeatStale);
    }

    [Fact]
    public void Classifier_CatchUp_Policy_Message_Matches_One_Fire_Per_Cycle()
    {
        var result = ScheduleReadinessClassifier.Classify(new ScheduleReadinessInput(
            Enabled: true,
            IntegrationId: "int-1",
            NextFireUtc: DateTimeOffset.UtcNow.AddMinutes(1),
            AsOfUtc: DateTimeOffset.UtcNow,
            WorkerRuntimeStatus: "Healthy",
            IncludeCatchUpPolicyReason: true));

        var catchUp = result.StatusReasons.Should().ContainSingle(r => r.Code == ScheduleStatusReasonCodes.CatchUpPolicy).Subject;
        catchUp.Message.Should().Contain("one due fire per scheduler cycle");
        ScheduleReadinessClassifier.CatchUpPolicyMessage.Should().Be(catchUp.Message);
    }

    [Fact]
    public void Projector_Includes_Timezone_Label_And_NextFire()
    {
        var asOf = new DateTimeOffset(2025, 1, 1, 0, 30, 0, TimeSpan.Zero);
        var projection = ScheduleClarityProjector.Project(
            "0 1 * * *",
            "UTC",
            enabled: true,
            integrationId: "int-1",
            asOfUtc: asOf,
            workerRuntimeStatus: "Healthy",
            includeCatchUpPolicyReason: false);

        projection.ScheduleTimeZoneId.Should().Be("UTC");
        projection.NextFireUtc.Should().NotBeNull();
        projection.HealthStatus.Should().Be(ScheduleHealthStatuses.Healthy);
    }
}
