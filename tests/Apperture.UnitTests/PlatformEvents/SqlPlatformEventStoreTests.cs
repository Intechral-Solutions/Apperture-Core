using Apperture.Core.Application.PlatformEvents;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.PlatformEvents;

public sealed class SqlPlatformEventStoreTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private static PlatformEventEnvelope SampleEnvelope(
        string id,
        string type,
        string integrationId,
        DateTimeOffset occurred,
        string? runId = null,
        string? moduleId = null) =>
        new(
            new PlatformEventId(id),
            type,
            PlatformEventSources.PlatformReadiness,
            "1.0",
            "application/json",
            """{"evaluationStatus":"MissingExpectedRun","operatorSummaryLine":"x"}""",
            occurred,
            occurred.AddSeconds(1),
            PlatformEventSeverities.Warning,
            Subject: $"{integrationId}:expected_run_missing",
            DedupeKey: null,
            IntegrationId: integrationId,
            ModuleId: moduleId,
            ProcedureId: null,
            RunId: runId,
            ScheduleId: "sch1",
            ActorId: null,
            CorrelationId: null,
            CausationId: null);

    [Fact]
    public async Task Append_and_query_by_integration_and_type()
    {
        await using var db = CreateDb();
        var sut = new SqlPlatformEventStore(db);
        var t0 = DateTimeOffset.Parse("2026-05-15T10:00:00Z");
        await sut.AppendAsync(SampleEnvelope("pevt_a", PlatformEventTypes.ScheduleExpectedRunMissing, "int1", t0));
        await sut.AppendAsync(SampleEnvelope("pevt_b", PlatformEventTypes.ScheduleExpectedRunCleared, "int1", t0.AddHours(1)));
        await sut.AppendAsync(SampleEnvelope("pevt_c", PlatformEventTypes.ScheduleExpectedRunMissing, "int2", t0));

        var missing = await sut.QueryAsync(
            new PlatformEventQuery(IntegrationId: "int1", EventType: PlatformEventTypes.ScheduleExpectedRunMissing, Take: 10));
        missing.Items.Should().ContainSingle();
        missing.Items[0].Id.Value.Should().Be("pevt_a");

        var range = await sut.QueryAsync(
            new PlatformEventQuery(
                IntegrationId: "int1",
                FromOccurredAtUtc: t0.AddMinutes(30),
                ToOccurredAtUtc: t0.AddHours(2),
                Take: 10));
        range.Items.Should().ContainSingle();
        range.Items[0].Type.Should().Be(PlatformEventTypes.ScheduleExpectedRunCleared);
    }

    [Fact]
    public async Task Query_filters_by_runId_and_moduleId()
    {
        await using var db = CreateDb();
        var sut = new SqlPlatformEventStore(db);
        var t0 = DateTimeOffset.Parse("2026-05-15T10:00:00Z");
        await sut.AppendAsync(SampleEnvelope("a", "t1", "int1", t0, runId: "run-a", moduleId: "Mod.A"));
        await sut.AppendAsync(SampleEnvelope("b", "t1", "int1", t0.AddMinutes(1), runId: "run-b", moduleId: "Mod.A"));
        await sut.AppendAsync(SampleEnvelope("c", "t1", "int1", t0.AddMinutes(2), runId: "run-b", moduleId: "Mod.B"));

        var byRun = await sut.QueryAsync(new PlatformEventQuery(RunId: "run-b", Take: 10));
        byRun.Items.Should().HaveCount(2);
        byRun.Items.Should().OnlyContain(e => e.RunId == "run-b");

        var byModule = await sut.QueryAsync(new PlatformEventQuery(ModuleId: "Mod.A", Take: 10));
        byModule.Items.Should().HaveCount(2);
        byModule.Items.Should().OnlyContain(e => e.ModuleId == "Mod.A");
    }
}
