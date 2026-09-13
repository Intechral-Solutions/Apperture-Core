using System.Text.Json;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Domain.Workflow;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.PlatformEvents;

public sealed class ExecutionLifecyclePlatformEventEmitterTests
{
    private static ExecutionLifecyclePlatformEventEmitter CreateSut(
        IPlatformEventEmitter emitter,
        bool platformEnabled = true,
        bool lifecycleEnabled = true) =>
        new(
            emitter,
            Options.Create(new PlatformEventsOptions
            {
                Enabled = platformEnabled,
                ExecutionLifecycle = new ExecutionLifecycleOptions { Enabled = lifecycleEnabled },
            }),
            NullLogger<ExecutionLifecyclePlatformEventEmitter>.Instance);

    private static ExecutionLifecycleTerminalContext CompletedContext(string runId = "run-1") =>
        new(
            RunId: runId,
            TerminalStatus: WorkflowLifecycleStatusNames.Completed,
            ExecutionKind: "workflow",
            DefinitionId: "Sample.HelloWorld.HelloWorld",
            IntegrationId: "int-9",
            ModuleId: "Sample.HelloWorld",
            ProcedureId: "proc-1",
            ScheduleId: "sched-1",
            CorrelationId: "corr-1",
            CausationId: "cause-1",
            StartedAtUtc: DateTimeOffset.Parse("2026-05-20T10:00:00Z"),
            CompletedAtUtc: DateTimeOffset.Parse("2026-05-20T10:05:00Z"),
            StepCount: 3);

    private static ExecutionLifecycleTerminalContext FailedContext(string runId = "run-fail") =>
        new(
            RunId: runId,
            TerminalStatus: WorkflowLifecycleStatusNames.Failed,
            ExecutionKind: "operation",
            DefinitionId: "Sample.Op",
            IntegrationId: "int-9",
            ModuleId: "Sample.Module",
            ProcedureId: null,
            ScheduleId: null,
            CorrelationId: null,
            CausationId: null,
            StartedAtUtc: DateTimeOffset.Parse("2026-05-20T10:00:00Z"),
            CompletedAtUtc: DateTimeOffset.Parse("2026-05-20T10:01:00Z"),
            FailedStepName: "Sync",
            FailureSummary: "Something went wrong in the sync step.",
            FailureCode: "operator_execution_failure",
            ExceptionType: "OperatorExecutionFailureException");

    [Fact]
    public void PlatformEventCatalog_includes_execution_run_types()
    {
        var catalog = new PlatformEventCatalog();
        catalog.IsKnownType(PlatformEventTypes.ExecutionRunCompleted).Should().BeTrue();
        catalog.IsKnownType(PlatformEventTypes.ExecutionRunFailed).Should().BeTrue();
    }

    [Fact]
    public async Task Emit_completed_validates_required_fields_and_correlation()
    {
        PlatformEventEmitRequest? captured = null;
        var inner = new Mock<IPlatformEventEmitter>();
        inner.Setup(e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformEventEmitRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync((PlatformEventEmitRequest r, CancellationToken _) =>
                new PlatformEventEnvelope(
                    PlatformEventId.New(),
                    r.Type,
                    r.Source,
                    r.SpecVersion,
                    r.DataContentType,
                    r.DataJson,
                    r.OccurredAtUtc,
                    DateTimeOffset.UtcNow,
                    r.Severity,
                    r.Subject,
                    r.DedupeKey,
                    r.IntegrationId,
                    r.ModuleId,
                    r.ProcedureId,
                    r.RunId,
                    r.ScheduleId,
                    ActorId: null,
                    r.CorrelationId,
                    r.CausationId));

        var sut = CreateSut(inner.Object);
        await sut.TryEmitTerminalAsync(CompletedContext());

        captured.Should().NotBeNull();
        captured!.Type.Should().Be(PlatformEventTypes.ExecutionRunCompleted);
        captured.Source.Should().Be(PlatformEventSources.PlatformRuntime);
        captured.Severity.Should().Be(PlatformEventSeverities.Info);
        captured.RunId.Should().Be("run-1");
        captured.IntegrationId.Should().Be("int-9");
        captured.ModuleId.Should().Be("Sample.HelloWorld");
        captured.Subject.Should().Be("run:run-1");
        captured.DedupeKey.Should().Be("run-1:terminal:completed");

        using var doc = JsonDocument.Parse(captured.DataJson);
        doc.RootElement.GetProperty("executionKind").GetString().Should().Be("workflow");
        doc.RootElement.GetProperty("definitionId").GetString().Should().Be("Sample.HelloWorld.HelloWorld");
        doc.RootElement.GetProperty("outcome").GetString().Should().Be("success");
        doc.RootElement.GetProperty("durationMs").GetInt64().Should().Be(300_000);
    }

    [Fact]
    public async Task Emit_failed_includes_safe_failure_fields()
    {
        PlatformEventEmitRequest? captured = null;
        var inner = new Mock<IPlatformEventEmitter>();
        inner.Setup(e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformEventEmitRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync((PlatformEventEmitRequest r, CancellationToken _) =>
                new PlatformEventEnvelope(
                    PlatformEventId.New(),
                    r.Type,
                    r.Source,
                    r.SpecVersion,
                    r.DataContentType,
                    r.DataJson,
                    r.OccurredAtUtc,
                    DateTimeOffset.UtcNow,
                    r.Severity,
                    r.Subject,
                    r.DedupeKey,
                    r.IntegrationId,
                    r.ModuleId,
                    r.ProcedureId,
                    r.RunId,
                    r.ScheduleId,
                    ActorId: null,
                    r.CorrelationId,
                    r.CausationId));

        var sut = CreateSut(inner.Object);
        await sut.TryEmitTerminalAsync(FailedContext());

        captured!.Type.Should().Be(PlatformEventTypes.ExecutionRunFailed);
        captured.Severity.Should().Be(PlatformEventSeverities.Error);
        captured.DedupeKey.Should().Be("run-fail:terminal:failed");

        using var doc = JsonDocument.Parse(captured.DataJson);
        doc.RootElement.GetProperty("failureCode").GetString().Should().Be("operator_execution_failure");
        doc.RootElement.GetProperty("failureMessageExcerpt").GetString().Should().Contain("Something went wrong");
        doc.RootElement.GetProperty("failedStepName").GetString().Should().Be("Sync");
        doc.RootElement.TryGetProperty("stackTrace", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Lifecycle_flag_off_skips_emit()
    {
        var inner = new Mock<IPlatformEventEmitter>();
        var sut = CreateSut(inner.Object, lifecycleEnabled: false);
        await sut.TryEmitTerminalAsync(CompletedContext());
        inner.Verify(e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Platform_events_disabled_skips_emit()
    {
        var inner = new Mock<IPlatformEventEmitter>();
        var sut = CreateSut(inner.Object, platformEnabled: false);
        await sut.TryEmitTerminalAsync(CompletedContext());
        inner.Verify(e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Duplicate_terminal_emit_uses_stable_dedupe_key()
    {
        var requests = new List<PlatformEventEmitRequest>();
        var inner = new Mock<IPlatformEventEmitter>();
        inner.Setup(e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformEventEmitRequest, CancellationToken>((r, _) => requests.Add(r))
            .ReturnsAsync((PlatformEventEmitRequest r, CancellationToken _) =>
                new PlatformEventEnvelope(
                    PlatformEventId.New(),
                    r.Type,
                    r.Source,
                    r.SpecVersion,
                    r.DataContentType,
                    r.DataJson,
                    r.OccurredAtUtc,
                    DateTimeOffset.UtcNow,
                    r.Severity,
                    r.Subject,
                    r.DedupeKey,
                    r.IntegrationId,
                    r.ModuleId,
                    r.ProcedureId,
                    r.RunId,
                    r.ScheduleId,
                    ActorId: null,
                    r.CorrelationId,
                    r.CausationId));

        var sut = CreateSut(inner.Object);
        var ctx = CompletedContext("run-dedupe");
        await sut.TryEmitTerminalAsync(ctx);
        await sut.TryEmitTerminalAsync(ctx);

        requests.Should().HaveCount(2);
        requests[0].DedupeKey.Should().Be(requests[1].DedupeKey);
    }

    [Fact]
    public async Task Inner_emit_failure_does_not_throw()
    {
        var inner = new Mock<IPlatformEventEmitter>();
        inner.Setup(e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store down"));

        var sut = CreateSut(inner.Object);
        var act = async () => await sut.TryEmitTerminalAsync(CompletedContext());
        await act.Should().NotThrowAsync();
    }
}
