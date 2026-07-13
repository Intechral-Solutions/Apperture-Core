using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Apperture.ControlPlane.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class PlatformEventNotificationEvaluationProcessorTests
{
    [Fact]
    public async Task ProcessBatch_evaluates_pending_worker_module_events()
    {
        var envelope = CreateHelloWorldEnvelope();
        var pending = new Mock<IPlatformEventNotificationEvaluationStore>();
        pending.Setup(s => s.ListPendingEvaluationAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([envelope]);

        var coordinator = new Mock<INotificationEvaluateOnlyCoordinator>();
        coordinator.Setup(c => c.EvaluatePlatformEventAsync(envelope, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = new PlatformEventNotificationEvaluationProcessor(
            pending.Object,
            coordinator.Object,
            Options.Create(new NotificationEvaluationOptions { Enabled = true, BatchSize = 20 }),
            NullLogger<PlatformEventNotificationEvaluationProcessor>.Instance);

        var processed = await sut.ProcessBatchAsync();

        processed.Should().Be(1);
        coordinator.Verify(c => c.EvaluatePlatformEventAsync(envelope, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatch_noop_when_evaluation_disabled()
    {
        var pending = new Mock<IPlatformEventNotificationEvaluationStore>();
        var coordinator = new Mock<INotificationEvaluateOnlyCoordinator>();
        var sut = new PlatformEventNotificationEvaluationProcessor(
            pending.Object,
            coordinator.Object,
            Options.Create(new NotificationEvaluationOptions { Enabled = false }),
            NullLogger<PlatformEventNotificationEvaluationProcessor>.Instance);

        var processed = await sut.ProcessBatchAsync();

        processed.Should().Be(0);
        pending.Verify(s => s.ListPendingEvaluationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PlatformEventEnvelope CreateHelloWorldEnvelope()
    {
        var now = DateTimeOffset.UtcNow;
        return new PlatformEventEnvelope(
            PlatformEventId.New(),
            HelloWorldDemoNotificationDefinitionSeed.EventType,
            "/modules/sample.helloworld",
            "1.0",
            "application/json",
            """{"message":"HelloWorld demo platform event completed."}""",
            now,
            now,
            PlatformEventSeverities.Info,
            "int-hw:demo.completed",
            null,
            "int-hw",
            "Sample.HelloWorld",
            null,
            "run-hw-1",
            null,
            null,
            null,
            null);
    }
}
