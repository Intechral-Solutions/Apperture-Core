using Apperture.ModuleSdk.Testing;
using FluentAssertions;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.UnitTests.Sample;

/// <summary>Epic 103E: HelloWorld prefers <see cref="IStepExecutionSignals"/>; <see cref="IStepExecutionContext.PlatformEvents"/> remains compatible.</summary>
public sealed class HelloWorldSignalsAdoptionTests
{
    [Fact]
    public async Task Complete_emits_demo_completed_by_default()
    {
        var harness = ModuleTestHarness.ForModule(new HelloWorldModule())
            .WithIntegrationId("int-hw")
            .WithRunId("run-hw-default")
            .WithSlotResolver(slot =>
                string.Equals(slot, "Db", StringComparison.Ordinal) ? new HarnessStubDbConnection() : null)
            .Build();

        var context = harness.CreateStepContext("Complete");
        await new HelloWorldCompleteStepHandler().ExecuteAsync(context, new Dictionary<string, object?>());

        harness.PlatformEvents.ShouldContain("demo.completed");
    }

    [Fact]
    public async Task Complete_skips_emit_when_flag_explicitly_false()
    {
        var harness = ModuleTestHarness.ForModule(new HelloWorldModule())
            .WithIntegrationId("int-hw")
            .WithRunId("run-hw-off")
            .WithSlotResolver(slot =>
                string.Equals(slot, "Db", StringComparison.Ordinal) ? new HarnessStubDbConnection() : null)
            .Build();

        var context = harness.CreateStepContext("Complete");
        await new HelloWorldCompleteStepHandler().ExecuteAsync(
            context,
            new Dictionary<string, object?> { ["emitDemoPlatformEvent"] = false });

        harness.PlatformEvents.EmittedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Complete_uses_Signals_Events_and_Logs_when_emit_flag_set()
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
        var logs = ModuleTestHarness.GetOperationalLogs(context);
        logs.OperationalLogs.Should().Contain(l => l.Code == "helloworld.complete.started");
        logs.OperationalLogs.Should().Contain(l => l.Code == "helloworld.demo.completed");
    }

    [Fact]
    public async Task PlatformEvents_alias_emits_same_as_Signals_Events()
    {
        var harness = ModuleTestHarness.ForModule(new HelloWorldModule())
            .WithRunId("run-alias")
            .WithSlotResolver(slot =>
                string.Equals(slot, "Db", StringComparison.Ordinal) ? new HarnessStubDbConnection() : null)
            .Build();

        var context = harness.CreateStepContext("Complete");
        var result = await context.PlatformEvents.EmitAsync(
            context,
            "demo.completed",
            new Dictionary<string, object?> { ["message"] = "via PlatformEvents alias" });

        result.Emitted.Should().BeTrue();
        harness.PlatformEvents.ShouldContain("demo.completed");
    }
}

/// <summary>Minimal IDbConnection stub for harness slot resolution (shared with ModuleTestHarnessTests pattern).</summary>
internal sealed class HarnessStubDbConnection : System.Data.IDbConnection
{
    public string ConnectionString { get; set; } = "";
    public int ConnectionTimeout => 0;
    public string Database => "";
    public System.Data.ConnectionState State { get; private set; } = System.Data.ConnectionState.Closed;
    public System.Data.IDbTransaction BeginTransaction() => throw new NotImplementedException();
    public System.Data.IDbTransaction BeginTransaction(System.Data.IsolationLevel il) => throw new NotImplementedException();
    public void ChangeDatabase(string databaseName) => throw new NotImplementedException();
    public System.Data.IDbCommand CreateCommand() => throw new NotImplementedException();
    public void Open() => State = System.Data.ConnectionState.Open;
    public void Close() => State = System.Data.ConnectionState.Closed;
    public void Dispose() => State = System.Data.ConnectionState.Closed;
}
