using System.Text.Json;
using Apperture.Core.Application.PlatformEvents;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.PlatformEvents;

public sealed class PlatformEventEmitterTests
{
    private static PlatformEventEmitter CreateEmitter(
        IPlatformEventStore? store = null,
        bool enabled = true)
    {
        var mockStore = store ?? new Mock<IPlatformEventStore>().Object;
        if (store is null)
        {
            var m = new Mock<IPlatformEventStore>();
            m.Setup(s => s.AppendAsync(It.IsAny<PlatformEventEnvelope>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PlatformEventEnvelope e, CancellationToken _) => e);
            mockStore = m.Object;
        }

        return new PlatformEventEmitter(
            new PlatformEventCatalog(),
            mockStore,
            Options.Create(new PlatformEventsOptions { Enabled = enabled }),
            TimeProvider.System);
    }

    private static string MissingPayload() =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            [PlatformEventDataProperties.EvaluationStatus] = "MissingExpectedRun",
            [PlatformEventDataProperties.OperatorSummaryLine] = "summary",
        });

    private static string ClearedPayload() =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            [PlatformEventDataProperties.EvaluationStatus] = "Satisfied",
            [PlatformEventDataProperties.ResolutionReason] = "expectation_cleared",
        });

    [Fact]
    public async Task EmitAsync_valid_missing_event_appends()
    {
        PlatformEventEnvelope? captured = null;
        var store = new Mock<IPlatformEventStore>();
        store.Setup(s => s.AppendAsync(It.IsAny<PlatformEventEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformEventEnvelope, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync((PlatformEventEnvelope e, CancellationToken _) => e);

        var sut = CreateEmitter(store.Object);
        var at = DateTimeOffset.Parse("2026-05-15T12:00:00Z");
        var result = await sut.EmitAsync(
            new PlatformEventEmitRequest(
                PlatformEventTypes.ScheduleExpectedRunMissing,
                PlatformEventSources.PlatformReadiness,
                MissingPayload(),
                at,
                PlatformEventSeverities.Warning,
                IntegrationId: "int1",
                ScheduleId: "sch1"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Type.Should().Be(PlatformEventTypes.ScheduleExpectedRunMissing);
        captured.IntegrationId.Should().Be("int1");
        captured.Id.Value.Should().StartWith("pevt_");
        result.OccurredAtUtc.Should().Be(at);
    }

    [Fact]
    public async Task EmitAsync_valid_cleared_event_appends()
    {
        var store = new Mock<IPlatformEventStore>();
        store.Setup(s => s.AppendAsync(It.IsAny<PlatformEventEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformEventEnvelope e, CancellationToken _) => e);

        var sut = CreateEmitter(store.Object);
        await sut.EmitAsync(
            new PlatformEventEmitRequest(
                PlatformEventTypes.ScheduleExpectedRunCleared,
                PlatformEventSources.PlatformReadiness,
                ClearedPayload(),
                DateTimeOffset.UtcNow,
                PlatformEventSeverities.Info,
                IntegrationId: "int1"),
            CancellationToken.None);
    }

    [Fact]
    public async Task EmitAsync_unknown_type_throws()
    {
        var sut = CreateEmitter();
        Func<Task> act = async () => await sut.EmitAsync(
            new PlatformEventEmitRequest(
                "apperture.unknown.event",
                PlatformEventSources.PlatformReadiness,
                "{}",
                DateTimeOffset.UtcNow,
                PlatformEventSeverities.Info),
            CancellationToken.None);
        await act.Should().ThrowAsync<PlatformEventValidationException>();
    }

    [Fact]
    public async Task EmitAsync_missing_integrationId_throws()
    {
        var sut = CreateEmitter();
        Func<Task> act = async () => await sut.EmitAsync(
            new PlatformEventEmitRequest(
                PlatformEventTypes.ScheduleExpectedRunMissing,
                PlatformEventSources.PlatformReadiness,
                MissingPayload(),
                DateTimeOffset.UtcNow,
                PlatformEventSeverities.Warning),
            CancellationToken.None);
        await act.Should().ThrowAsync<PlatformEventValidationException>();
    }

    [Fact]
    public async Task EmitAsync_missing_required_data_property_throws()
    {
        var sut = CreateEmitter();
        Func<Task> act = async () => await sut.EmitAsync(
            new PlatformEventEmitRequest(
                PlatformEventTypes.ScheduleExpectedRunMissing,
                PlatformEventSources.PlatformReadiness,
                """{"evaluationStatus":"MissingExpectedRun"}""",
                DateTimeOffset.UtcNow,
                PlatformEventSeverities.Warning,
                IntegrationId: "int1"),
            CancellationToken.None);
        await act.Should().ThrowAsync<PlatformEventValidationException>();
    }

    [Fact]
    public async Task EmitAsync_when_disabled_throws()
    {
        var sut = CreateEmitter(enabled: false);
        Func<Task> act = async () => await sut.EmitAsync(
            new PlatformEventEmitRequest(
                PlatformEventTypes.ScheduleExpectedRunCleared,
                PlatformEventSources.PlatformReadiness,
                ClearedPayload(),
                DateTimeOffset.UtcNow,
                PlatformEventSeverities.Info,
                IntegrationId: "int1"),
            CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
