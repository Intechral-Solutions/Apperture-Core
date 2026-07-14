using Apperture.Core.Application.Execution;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.ModuleSdk.Testing;
using Apperture.ModuleSdk.Testing.Signals;
using Apperture.RuntimePlane.Engine.PlatformEvents;
using FluentAssertions;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.UnitTests.Sample;

/// <summary>
/// Guarantees Complete cannot succeed (or skip) without writing demo.completed when emit is requested.
/// Root cause for empty PlatformEvents rows: prior handler returned null when Db was unbound before emit.
/// </summary>
public sealed class HelloWorldCompleteEmitGuaranteesTests
{
    [Fact]
    public async Task Complete_emits_demo_completed_even_when_Db_slot_is_missing_before_failing()
    {
        var harness = ModuleTestHarness.ForModule(new HelloWorldModule())
            .WithIntegrationId("int-hw")
            .WithRunId("run-no-db")
            .WithSlotResolver(_ => null)
            .Build();

        var context = harness.CreateStepContext("Complete");
        var act = async () => await new HelloWorldCompleteStepHandler()
            .ExecuteAsync(context, new Dictionary<string, object?>());

        await act.Should().ThrowAsync<OperatorExecutionFailureException>()
            .Where(ex => ex.Message.Contains("Db slot", StringComparison.OrdinalIgnoreCase));

        harness.PlatformEvents.ShouldContain("demo.completed");
    }

    [Fact]
    public async Task Complete_fails_when_platform_event_emitter_is_null()
    {
        var module = new HelloWorldModule();
        var logs = new InMemoryOperationalLogSink(module.GetManifest().Id, "int-hw", "run-null-emitter");
        var context = new HarnessStepExecutionContext(
            module.GetManifest().Id,
            NullModulePlatformEventEmitter.Instance,
            logs,
            NoOpModuleTelemetry.Instance,
            integrationId: "int-hw",
            runId: "run-null-emitter",
            slotResolver: slot =>
                string.Equals(slot, "Db", StringComparison.Ordinal) ? new HarnessStubDbConnection() : null);

        var act = async () => await new HelloWorldCompleteStepHandler()
            .ExecuteAsync(context, new Dictionary<string, object?>());

        await act.Should().ThrowAsync<OperatorExecutionFailureException>()
            .Where(ex => ex.Message.Contains("demo.completed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Complete_fails_when_platform_events_disabled()
    {
        var harness = ModuleTestHarness.ForModule(new HelloWorldModule())
            .WithIntegrationId("int-hw")
            .WithRunId("run-disabled")
            .WithPlatformEventsEnabled(false)
            .WithSlotResolver(slot =>
                string.Equals(slot, "Db", StringComparison.Ordinal) ? new HarnessStubDbConnection() : null)
            .Build();

        var context = harness.CreateStepContext("Complete");
        var act = async () => await new HelloWorldCompleteStepHandler()
            .ExecuteAsync(context, new Dictionary<string, object?>());

        await act.Should().ThrowAsync<OperatorExecutionFailureException>()
            .Where(ex =>
                ex.Message.Contains("demo.completed", StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("PlatformEvents:Enabled=false", StringComparison.OrdinalIgnoreCase));
    }
}
