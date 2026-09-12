using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Engine.PlatformEvents;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.PlatformEvents;

public sealed class ModulePlatformEventEmitterTests
{
    private sealed class SignalsTestModule : IModule
    {
        private readonly ModuleManifest _manifest;
        public SignalsTestModule(ModuleManifest manifest) => _manifest = manifest;
        public ModuleManifest GetManifest() => _manifest;
        public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }

    private static ModuleManifest ModManifest(string moduleId, params ModuleSignalDeclaration[] signals) =>
        new(Id: moduleId, Name: "n", Version: "1.0", Description: "d") { Signals = signals };

    private static ModuleSignalDeclaration Sig(string name = "demo.x", string severity = "info") =>
        new(name, "T", "D", severity, RequiredDataProperties: ["detail"]);

    [Fact]
    public async Task EmitAsync_EnrichesRequest_FromContextAndSerializesCamelCasePayload()
    {
        var modules = new IModule[]
        {
            new SignalsTestModule(ModManifest("Mod.Emit", new ModuleSignalDeclaration("ping", "Ping", "P", "info", RequiredDataProperties: ["detail"]))),
        };
        ModuleSignalManifestValidator.ThrowIfInvalid(modules);
        var registry = ModuleSignalRegistry.FromModules(modules);
        var catalog = new CompositePlatformEventCatalog(registry);

        var store = new Mock<IPlatformEventStore>();
        PlatformEventEnvelope? captured = null;
        store.Setup(s => s.AppendAsync(It.IsAny<PlatformEventEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformEventEnvelope, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync((PlatformEventEnvelope e, CancellationToken _) => e);

        var inner = new PlatformEventEmitter(
            catalog,
            store.Object,
            Options.Create(new PlatformEventsOptions { Enabled = true }),
            TimeProvider.System);

        var sut = new ModulePlatformEventEmitter(
            registry,
            inner,
            Options.Create(new PlatformEventsOptions { Enabled = true }),
            TimeProvider.System);

        var emitCtx = new TestEmitContext(
            moduleId: "Mod.Emit",
            integrationId: "int-42",
            runId: "run-99",
            procedureId: "proc-1",
            scheduleId: "sched:z",
            correlationId: "corr-x",
            causationId: "cause-y");

        var result = await sut.EmitAsync(
            emitCtx,
            "ping",
            new { Detail = "hi" },
            cancellationToken: CancellationToken.None);

        result.EmittingSucceeded().Should().BeTrue();
        result.EventId.Should().NotBeNullOrEmpty();

        captured.Should().NotBeNull();
        captured!.RunId.Should().Be("run-99");
        captured.IntegrationId.Should().Be("int-42");
        captured.ModuleId.Should().Be("Mod.Emit");
        captured.ProcedureId.Should().Be("proc-1");
        captured.ScheduleId.Should().Be("sched:z");
        captured.CorrelationId.Should().Be("corr-x");
        captured.CausationId.Should().Be("cause-y");
        captured.Subject.Should().Contain("run-99");
        captured.DataJson.Should().Contain("\"detail\"");
        captured.Type.Should().Be("apperture.module.mod.emit.ping");
    }

    [Fact]
    public async Task EmitAsync_WhenDisabled_SkipsWithoutCallingInnerEmitter()
    {
        var modules = new IModule[] { new SignalsTestModule(ModManifest("Mod.Skip", Sig("x", "warning"))) };
        ModuleSignalManifestValidator.ThrowIfInvalid(modules);
        var registry = ModuleSignalRegistry.FromModules(modules);
        var inner = new Mock<IPlatformEventEmitter>();
        inner.Setup(e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()));

        var sut = new ModulePlatformEventEmitter(
            registry,
            inner.Object,
            Options.Create(new PlatformEventsOptions { Enabled = false }),
            TimeProvider.System);

        var r = await sut.EmitAsync(new TestEmitContext("Mod.Skip"), "x", new { detail = "z" });

        r.SkippedBecauseDisabled.Should().BeTrue();
        r.Emitted.Should().BeFalse();
        inner.Verify(e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmitAsync_UnknownSignal_ThrowsValidation()
    {
        var modules = new IModule[] { new SignalsTestModule(ModManifest("Mod.A", Sig())) };
        ModuleSignalManifestValidator.ThrowIfInvalid(modules);
        var registry = ModuleSignalRegistry.FromModules(modules);
        var inner = new Mock<IPlatformEventEmitter>();
        var sut = new ModulePlatformEventEmitter(
            registry,
            inner.Object,
            Options.Create(new PlatformEventsOptions { Enabled = true }),
            TimeProvider.System);

        var act = async () => await sut.EmitAsync(new TestEmitContext("Mod.A"), "missing", new { detail = "z" });

        await act.Should().ThrowAsync<PlatformEventValidationException>();
    }

    private sealed class TestEmitContext : IModulePlatformEmitContext
    {
        public TestEmitContext(
            string moduleId,
            string? integrationId = null,
            string? runId = null,
            string? procedureId = null,
            string? scheduleId = null,
            string? correlationId = null,
            string? causationId = null)
        {
            ModuleId = moduleId;
            IntegrationId = integrationId;
            RunId = runId;
            ProcedureId = procedureId;
            ScheduleId = scheduleId;
            CorrelationId = correlationId;
            CausationId = causationId;
        }

        public string ModuleId { get; }
        public string? IntegrationId { get; }
        public string? RunId { get; }
        public string? ProcedureId { get; }
        public string? ScheduleId { get; }
        public string? CorrelationId { get; }
        public string? CausationId { get; }

        public string? ResourceContextId { get; }
    }
}

internal static class EmitResultAssertions
{
    public static bool EmittingSucceeded(this ModulePlatformEventEmitResult r) =>
        r is { Emitted: true, SkippedBecauseDisabled: false };
}
