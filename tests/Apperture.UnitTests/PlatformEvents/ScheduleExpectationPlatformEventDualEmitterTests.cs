using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Integration;
using Apperture.ControlPlane.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.PlatformEvents;

public sealed class ScheduleExpectationPlatformEventDualEmitterTests
{
    private static readonly IntegrationId IntId = new("int-dual");

    private static ScheduleExpectationSnapshot Snapshot(ScheduleExpectationEvaluationStatus status) =>
        new(
            new ScheduleExpectationEvaluation(
                ExpectsRecurringRuns: true,
                HasExpectedFireInWindow: true,
                ExpectedFireCountInWindow: 1,
                MostRecentExpectedFireUtc: DateTimeOffset.Parse("2026-05-15T11:00:00Z"),
                InGracePeriod: false,
                SatisfiedByAuthorizedScheduleOriginRun: false,
                LastSatisfyingScheduleOriginRunId: null,
                LastSatisfyingScheduleOriginRunAt: null,
                Status: status,
                OperatorSummaryLine: "test line"),
            ReferenceScheduleId: "sch1",
            LatestScheduleOriginAuthorizedRun: null,
            EvidenceWindowStartUtc: DateTimeOffset.Parse("2026-05-01T00:00:00Z"));

    [Fact]
    public async Task Opened_emits_missing_event()
    {
        var emitter = new Mock<IPlatformEventEmitter>();
        var sut = new ScheduleExpectationPlatformEventDualEmitter(
            emitter.Object,
            Options.Create(new PlatformEventsOptions { Enabled = true }),
            NullLogger<ScheduleExpectationPlatformEventDualEmitter>.Instance);

        var at = DateTimeOffset.Parse("2026-05-15T12:00:00Z");
        await sut.TryEmitForObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.Opened, "attn_1"),
            Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun),
            at);

        emitter.Verify(
            e => e.EmitAsync(
                It.Is<PlatformEventEmitRequest>(r =>
                    r.Type == PlatformEventTypes.ScheduleExpectedRunMissing
                    && r.IntegrationId == IntId.Value),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Resolved_emits_cleared_event()
    {
        var emitter = new Mock<IPlatformEventEmitter>();
        var sut = new ScheduleExpectationPlatformEventDualEmitter(
            emitter.Object,
            Options.Create(new PlatformEventsOptions { Enabled = true }),
            NullLogger<ScheduleExpectationPlatformEventDualEmitter>.Instance);

        await sut.TryEmitForObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.Resolved, "attn_1"),
            Snapshot(ScheduleExpectationEvaluationStatus.Satisfied),
            DateTimeOffset.UtcNow);

        emitter.Verify(
            e => e.EmitAsync(
                It.Is<PlatformEventEmitRequest>(r => r.Type == PlatformEventTypes.ScheduleExpectedRunCleared),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ObservedStillActive_does_not_emit()
    {
        var emitter = new Mock<IPlatformEventEmitter>();
        var sut = new ScheduleExpectationPlatformEventDualEmitter(
            emitter.Object,
            Options.Create(new PlatformEventsOptions { Enabled = true }),
            NullLogger<ScheduleExpectationPlatformEventDualEmitter>.Instance);

        await sut.TryEmitForObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.ObservedStillActive, "attn_1"),
            Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun),
            DateTimeOffset.UtcNow);

        emitter.Verify(
            e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task When_disabled_does_not_emit()
    {
        var emitter = new Mock<IPlatformEventEmitter>();
        var sut = new ScheduleExpectationPlatformEventDualEmitter(
            emitter.Object,
            Options.Create(new PlatformEventsOptions { Enabled = false }),
            NullLogger<ScheduleExpectationPlatformEventDualEmitter>.Instance);

        await sut.TryEmitForObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.Opened, "attn_1"),
            Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun),
            DateTimeOffset.UtcNow);

        emitter.Verify(
            e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
