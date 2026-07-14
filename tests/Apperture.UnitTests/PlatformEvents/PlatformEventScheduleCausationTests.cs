using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Engine.PlatformEvents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.PlatformEvents;

/// <summary>SWM-6 hotfix: schedule fire keys must not be written to PlatformEvents.CausationId.</summary>
public sealed class PlatformEventScheduleCausationTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    /// <summary>Matches the observed Aspire failure key (schedule id + fire UTC round-trip).</summary>
    private static string SampleScheduleFireIdempotencyKey() =>
        "schedule:e3a76ff4dac24679b192305b:2026-07-14T16:22:00.0000000+00:00";

    [Fact]
    public void Schedule_fire_idempotency_key_exceeds_causation_column_limit()
    {
        var fireKey = SampleScheduleFireIdempotencyKey();
        fireKey.Length.Should().BeGreaterThan(PlatformEventScopeCorrelation.CausationIdMaxLength);
        fireKey.Length.Should().BeLessThanOrEqualTo(PlatformEventScopeCorrelation.StartRequestIdempotencyKeyMaxLength);

        using var db = CreateDb();
        var max = db.Model
            .FindEntityType(typeof(PlatformEventEntity))!
            .FindProperty(nameof(PlatformEventEntity.CausationId))!
            .GetMaxLength();
        max.Should().Be(PlatformEventScopeCorrelation.CausationIdMaxLength);
        fireKey.Length.Should().BeGreaterThan(max!.Value);
    }

    [Fact]
    public void CausationFromRunId_uses_bounded_run_id_not_fire_key()
    {
        const string runId = "e9a098aef15c4b03a370779c";
        var causation = PlatformEventScopeCorrelation.CausationFromRunId(runId);
        causation.Should().Be(runId);
        causation!.Length.Should().BeLessThanOrEqualTo(PlatformEventScopeCorrelation.CausationIdMaxLength);

        var wrong = SampleScheduleFireIdempotencyKey();
        wrong.Should().NotBe(causation);
        wrong.Length.Should().BeGreaterThan(PlatformEventScopeCorrelation.CausationIdMaxLength);
    }

    [Fact]
    public void CausationFromRunId_rejects_oversized_values_without_silent_truncation()
    {
        var oversized = new string('x', PlatformEventScopeCorrelation.CausationIdMaxLength + 1);
        var act = () => PlatformEventScopeCorrelation.CausationFromRunId(oversized);
        act.Should().Throw<ArgumentException>().WithMessage("*exceeds*");
    }

    [Fact]
    public async Task Platform_event_append_succeeds_with_run_causation_and_preserves_fire_key_on_start_request()
    {
        await using var db = CreateDb();
        var sut = new SqlPlatformEventStore(db);
        var runId = "e9a098aef15c4b03a370779c";
        var scheduleId = "e3a76ff4dac24679b192305b";
        var correlation = $"schedule:{scheduleId}";
        var fireKey = SampleScheduleFireIdempotencyKey();
        var now = DateTimeOffset.Parse("2026-07-14T16:22:05Z");

        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = runId,
            WorkflowInstanceId = "inst1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            IdempotencyKey = fireKey,
            CorrelationId = correlation,
            CreatedAt = now,
            Status = WorkflowLifecycleStatusNames.Completed
        });
        await db.SaveChangesAsync();

        await sut.AppendAsync(new PlatformEventEnvelope(
            new PlatformEventId("pevt_sched_hw"),
            "Sample.HelloWorld.demo.completed",
            "/modules/sample.helloworld",
            "1.0",
            "application/json",
            """{"outcome":"success"}""",
            now,
            now,
            PlatformEventSeverities.Info,
            Subject: $"Sample.HelloWorld:demo.completed:{runId}",
            DedupeKey: $"{runId}:demo.completed",
            IntegrationId: "int1",
            ModuleId: "Sample.HelloWorld",
            ProcedureId: null,
            RunId: runId,
            ScheduleId: scheduleId,
            ActorId: null,
            CorrelationId: correlation,
            CausationId: PlatformEventScopeCorrelation.CausationFromRunId(runId)));

        var stored = await db.PlatformEvents.AsNoTracking().SingleAsync();
        stored.CausationId.Should().Be(runId);
        stored.CausationId!.Length.Should().BeLessThanOrEqualTo(PlatformEventScopeCorrelation.CausationIdMaxLength);
        stored.CorrelationId.Should().Be(correlation);
        stored.ScheduleId.Should().Be(scheduleId);
        stored.RunId.Should().Be(runId);

        var start = await db.WorkflowStartRequests.AsNoTracking().SingleAsync(x => x.RunId == runId);
        start.IdempotencyKey.Should().Be(fireKey);
        start.IdempotencyKey!.Length.Should().BeGreaterThan(PlatformEventScopeCorrelation.CausationIdMaxLength);
    }
}
