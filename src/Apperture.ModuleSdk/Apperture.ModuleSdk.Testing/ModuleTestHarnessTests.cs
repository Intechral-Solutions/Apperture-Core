using System.Data;
using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.ModuleSdk.Testing.Signals;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.ModuleSdk.Testing;

public sealed class ModuleTestHarnessTests
{
    private sealed class HarnessTestModule : IModule
    {
        private readonly ModuleManifest _manifest;

        public HarnessTestModule(ModuleManifest manifest) => _manifest = manifest;

        public ModuleManifest GetManifest() => _manifest;

        public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }

    private static ModuleManifest Manifest(string moduleId, params ModuleSignalDeclaration[] signals) =>
        new(moduleId, "Test", "1.0", "Harness tests") { Signals = signals };

    [Fact]
    public async Task HelloWorld_Complete_emits_demo_completed_via_signals_events_when_flag_set()
    {
        var harness = ModuleTestHarness.ForModule(new HelloWorldModule())
            .WithIntegrationId("int-hw")
            .WithRunId("run-hw")
            .WithSlotResolver(slot =>
                string.Equals(slot, "Db", StringComparison.Ordinal) ? new HarnessStubDbConnection() : null)
            .Build();

        var context = harness.CreateStepContext("Complete");
        await new HelloWorldCompleteStepHandler().ExecuteAsync(
            context,
            new Dictionary<string, object?> { ["emitDemoPlatformEvent"] = true });

        harness.PlatformEvents.ShouldContain("demo.completed");
        var ev = harness.PlatformEvents.GetEvent("demo.completed");
        ev.IntegrationId.Should().Be("int-hw");
        ev.RunId.Should().Be("run-hw");
        ev.EventType.Should().Be("apperture.module.sample.helloworld.demo.completed");
        ev.DataJson.Should().Contain("message");

        var logs = ModuleTestHarness.GetOperationalLogs(context);
        logs.OperationalLogs.Should().Contain(l => l.Code == "helloworld.complete.started");
        logs.OperationalLogs.Should().Contain(l => l.Code == "helloworld.demo.completed");
        harness.Telemetry.Counters.Should().ContainSingle(c => c.Name == "helloworld.complete" && c.Value == 1);
    }

    [Fact]
    public async Task Undeclared_signal_throws_validation_exception()
    {
        var harness = ModuleTestHarness.ForModule(new HarnessTestModule(
                Manifest("Mod.Harness", new ModuleSignalDeclaration("known", "K", "D", "info"))))
            .Build();

        var context = harness.CreateStepContext("S1");
        var act = async () => await context.Signals.Events.EmitAsync(context, "missing", new { x = 1 });

        await act.Should().ThrowAsync<PlatformEventValidationException>();
        harness.PlatformEvents.EmittedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_required_payload_property_throws()
    {
        var harness = ModuleTestHarness.ForModule(new HarnessTestModule(
                Manifest(
                    "Mod.Req",
                    new ModuleSignalDeclaration("need.msg", "N", "D", "info", RequiredDataProperties: ["message"]))))
            .Build();

        var context = harness.CreateStepContext("S1");
        var act = async () => await context.Signals.Events.EmitAsync(context, "need.msg", new { other = 1 });

        await act.Should().ThrowAsync<PlatformEventValidationException>();
    }

    [Fact]
    public async Task Disabled_platform_events_skip_without_recording()
    {
        var harness = ModuleTestHarness.ForModule(new HarnessTestModule(
                Manifest("Mod.Off", new ModuleSignalDeclaration("ping", "P", "D", "info"))))
            .WithPlatformEventsEnabled(false)
            .Build();

        var context = harness.CreateStepContext("S1");
        var result = await context.Signals.Events.EmitAsync(context, "ping", new { });

        result.SkippedBecauseDisabled.Should().BeTrue();
        result.Emitted.Should().BeFalse();
        harness.PlatformEvents.EmittedEvents.Should().BeEmpty();
    }

    [Fact]
    public void CreateStepContext_exposes_signals_and_platform_events_alias()
    {
        var harness = ModuleTestHarness.ForModule(new HarnessTestModule(
                Manifest("Mod.Sig", new ModuleSignalDeclaration("a", "A", "D", "info"))))
            .WithRunId("run-1")
            .Build();

        var context = harness.CreateStepContext("StepA");

        context.Signals.Should().NotBeNull();
        ReferenceEquals(context.PlatformEvents, context.Signals.Events).Should().BeTrue();
        context.Signals.Correlation.RunId.Should().Be("run-1");

        context.Signals.Logs.LogOperational(OperationalLogLevel.Information, "test.ok", "msg");
        var logs = ModuleTestHarness.GetOperationalLogs(context);
        logs.OperationalLogs.Should().ContainSingle(l => l.Code == "test.ok" && l.RunId == "run-1");
    }

    [Fact]
    public void Telemetry_recording_does_not_throw()
    {
        var harness = ModuleTestHarness.ForModule(new HarnessTestModule(
                Manifest("Mod.Tel", new ModuleSignalDeclaration("a", "A", "D", "info"))))
            .Build();

        var context = harness.CreateStepContext("S1");
        var act = () =>
        {
            context.Signals.Telemetry.RecordCounter("rows", 3);
            using var _ = context.Signals.Telemetry.MeasureDuration("sync");
        };

        act.Should().NotThrow();
        harness.Telemetry.Counters.Should().ContainSingle(c => c.Name == "rows" && c.Value == 3);
        harness.Telemetry.Durations.Should().ContainSingle(d => d.Name == "sync" && d.CompletedAtUtc.HasValue);
    }
}
