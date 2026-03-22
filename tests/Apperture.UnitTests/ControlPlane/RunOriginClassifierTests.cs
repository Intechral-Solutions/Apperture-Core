using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class RunOriginClassifierTests
{
    [Fact]
    public void TryParseScheduleDispatchIdempotency_ParsesScheduleIdAndFireTime()
    {
        var scheduleId = "sched-1";
        var fire = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var key = $"schedule:{scheduleId}:{fire:O}";

        var parsed = RunOriginClassifier.TryParseScheduleDispatchIdempotency(key);

        parsed.Should().NotBeNull();
        parsed!.Value.ScheduleId.Should().Be(scheduleId);
        parsed.Value.FireUtc.Should().Be(fire);
    }

    [Fact]
    public void TryParseScheduleDispatchIdempotency_ReturnsNull_WhenMalformed()
    {
        RunOriginClassifier.TryParseScheduleDispatchIdempotency(null).Should().BeNull();
        RunOriginClassifier.TryParseScheduleDispatchIdempotency("").Should().BeNull();
        RunOriginClassifier.TryParseScheduleDispatchIdempotency("schedule:").Should().BeNull();
        RunOriginClassifier.TryParseScheduleDispatchIdempotency("schedule:id").Should().BeNull();
        RunOriginClassifier.TryParseScheduleDispatchIdempotency("schedule:id:not-a-date").Should().BeNull();
    }

    [Fact]
    public void Classify_Retry_WinsOverSchedulePattern()
    {
        var fire = DateTimeOffset.UtcNow;
        var idem = $"schedule:{Guid.NewGuid():N}:{fire:O}";

        var (kind, _, schedId, _) = RunOriginClassifier.Classify(
            retryOfRunId: "parent-run",
            startIdempotencyKey: idem,
            startCorrelationId: null,
            hasStartRequest: true);

        kind.Should().Be(RunOriginKindNames.Retry);
        schedId.Should().BeNull();
    }

    [Fact]
    public void Classify_Scheduled_FromPlatformIdempotency_WithMatchingCorrelation()
    {
        var scheduleId = Guid.NewGuid().ToString("N");
        var fire = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero);
        var idem = $"schedule:{scheduleId}:{fire:O}";

        var (kind, detail, schedId, fireUtc) = RunOriginClassifier.Classify(
            retryOfRunId: null,
            startIdempotencyKey: idem,
            startCorrelationId: $"schedule:{scheduleId}",
            hasStartRequest: true);

        kind.Should().Be(RunOriginKindNames.Scheduled);
        schedId.Should().Be(scheduleId);
        fireUtc.Should().Be(fire);
        detail.Should().Contain("idempotency", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_Scheduled_StillScheduled_WhenCorrelationMismatch_ButDetailNotesMismatch()
    {
        var scheduleId = "abc";
        var fire = DateTimeOffset.UtcNow;
        var idem = $"schedule:{scheduleId}:{fire:O}";

        var (kind, detail, schedId, _) = RunOriginClassifier.Classify(
            retryOfRunId: null,
            startIdempotencyKey: idem,
            startCorrelationId: "other",
            hasStartRequest: true);

        kind.Should().Be(RunOriginKindNames.Scheduled);
        schedId.Should().Be(scheduleId);
        detail.Should().Contain("correlation", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_Manual_WhenNotRetryOrSchedule_AndHasStartRequest()
    {
        var (kind, _, schedId, fire) = RunOriginClassifier.Classify(
            retryOfRunId: null,
            startIdempotencyKey: "custom-key",
            startCorrelationId: null,
            hasStartRequest: true);

        kind.Should().Be(RunOriginKindNames.Manual);
        schedId.Should().BeNull();
        fire.Should().BeNull();
    }

    [Fact]
    public void Classify_Unknown_WhenNoStartRequest()
    {
        var (kind, _, schedId, fire) = RunOriginClassifier.Classify(
            retryOfRunId: null,
            startIdempotencyKey: null,
            startCorrelationId: null,
            hasStartRequest: false);

        kind.Should().Be(RunOriginKindNames.Unknown);
        schedId.Should().BeNull();
        fire.Should().BeNull();
    }
}
