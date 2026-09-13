using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.Scheduling;

/// <summary>
/// SWM-3: schedule timezone resolution, create defaults, Cronos DST, and due evaluation.
/// </summary>
public sealed class ScheduleTimezoneSwm3Tests
{
    private static bool TryLosAngeles(out TimeZoneInfo tz) =>
        ScheduleTimeZoneResolver.TryFind("America/Los_Angeles", out tz);

    [Fact]
    public void TryFind_Rejects_Invalid_Ids()
    {
        ScheduleTimeZoneResolver.TryFind("Invalid/NotAZone", out _).Should().BeFalse();
        ScheduleTimeZoneResolver.TryFind(" ", out _).Should().BeFalse();
        ScheduleTimeZoneResolver.TryFind(null, out _).Should().BeFalse();
    }

    [Fact]
    public void TryFind_Accepts_Utc_And_Iana_When_Host_Supports()
    {
        ScheduleTimeZoneResolver.TryFind("UTC", out var utc).Should().BeTrue();
        utc.Should().Be(TimeZoneInfo.Utc);

        if (!TryLosAngeles(out var la))
            return;

        la.Id.Should().Be("America/Los_Angeles");
    }

    [Fact]
    public void ResolvePersistedOrUtc_Logs_Corrupt_Id_Then_Falls_Back()
    {
        var logger = new CapturingLogger();
        var tz = ScheduleTimeZoneResolver.ResolvePersistedOrUtc("Corrupt/Zone", logger);
        tz.Should().Be(TimeZoneInfo.Utc);
        logger.WarningMessages.Should().ContainSingle(m => m.Contains("Corrupt/Zone", StringComparison.Ordinal));
    }

    [Fact]
    public void ToNormalizedFireUtc_Uses_Zero_Offset()
    {
        var fire = ScheduleTimeZoneResolver.ToNormalizedFireUtc(new DateTime(2025, 6, 15, 19, 0, 0, DateTimeKind.Utc));
        fire.Offset.Should().Be(TimeSpan.Zero);
        fire.UtcDateTime.Should().Be(new DateTime(2025, 6, 15, 19, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task CreateDefaults_Omit_Uses_Platform_Setting()
    {
        var sut = new ScheduleCreateTimeZoneDefaults(
            new StubPlatformSettingsResolver("America/Los_Angeles"),
            NullLogger<ScheduleCreateTimeZoneDefaults>.Instance);

        if (!TryLosAngeles(out _))
        {
            // Host cannot resolve IANA LA; setting falls back to UTC after TryFind fails.
            (await sut.ResolveCreateTimeZoneIdAsync(null)).Should().Be("UTC");
            return;
        }

        (await sut.ResolveCreateTimeZoneIdAsync(null)).Should().Be("America/Los_Angeles");
        (await sut.ResolveCreateTimeZoneIdAsync("  ")).Should().Be("America/Los_Angeles");
    }

    [Fact]
    public async Task CreateDefaults_Explicit_Is_Not_Silently_Normalized()
    {
        var sut = new ScheduleCreateTimeZoneDefaults(
            new StubPlatformSettingsResolver("UTC"),
            NullLogger<ScheduleCreateTimeZoneDefaults>.Instance);

        (await sut.ResolveCreateTimeZoneIdAsync("Invalid/StillReturned")).Should().Be("Invalid/StillReturned");
    }

    [Fact]
    public async Task CreateDefaults_Invalid_Setting_Falls_Back_To_Utc()
    {
        var logger = new CapturingLogger<ScheduleCreateTimeZoneDefaults>();
        var sut = new ScheduleCreateTimeZoneDefaults(
            new StubPlatformSettingsResolver("Not/ARealZone"),
            logger);

        (await sut.ResolveCreateTimeZoneIdAsync(null)).Should().Be("UTC");
        logger.WarningMessages.Should().ContainSingle(m => m.Contains(PlatformSettingKeys.SchedulingDefaultTimeZoneId, StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_Rejects_Invalid_Timezone_On_Create_And_Update()
    {
        var sut = new ScheduleValidationService();

        var create = sut.ValidateCreate("x", "0 * * * *", "Invalid/TZ", true, 0, "{\"workflowDefinitionId\":\"wf1\"}");
        create.IsValid.Should().BeFalse();
        create.Errors.Should().Contain(e => e.Contains("time zone", StringComparison.OrdinalIgnoreCase));

        var update = sut.ValidateUpdate(null, null, "Invalid/TZ", null);
        update.IsValid.Should().BeFalse();
        update.Errors.Should().Contain(e => e.Contains("time zone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExpectedFireCollector_Noon_Local_Maps_To_Utc_In_Named_Zone()
    {
        if (!TryLosAngeles(out _))
            return;

        var schedule = new ScheduleDefinition(
            new ScheduleId("s1"),
            "Noon LA",
            "0 12 * * *",
            "America/Los_Angeles",
            true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{\"workflowDefinitionId\":\"wf1\"}"),
            null,
            1,
            new IntegrationId("i1"));

        var sink = new HashSet<DateTime>();
        ScheduleExpectedFireCollector.TryCollectFires(
            schedule,
            new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 6, 16, 0, 0, 0, DateTimeKind.Utc),
            sink);

        sink.Should().ContainSingle()
            .Which.Should().Be(new DateTime(2025, 6, 15, 19, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ExpectedFireCollector_SpringForward_Adjusts_Invalid_Local_Cron_Time()
    {
        // Cronos: 2:30 America/Los_Angeles on spring-forward day is adjusted to the next valid
        // local time (03:00 PDT = 10:00 UTC on 2025-03-09), not skipped entirely.
        if (!TryLosAngeles(out _))
            return;

        var schedule = new ScheduleDefinition(
            new ScheduleId("s1"),
            "Spring gap",
            "30 2 * * *",
            "America/Los_Angeles",
            true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{\"workflowDefinitionId\":\"wf1\"}"),
            null,
            1,
            new IntegrationId("i1"));

        var sink = new HashSet<DateTime>();
        ScheduleExpectedFireCollector.TryCollectFires(
            schedule,
            new DateTime(2025, 3, 9, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            sink);

        sink.Should().ContainSingle()
            .Which.Should().Be(new DateTime(2025, 3, 9, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ExpectedFireCollector_FallBack_Emits_Single_Ambiguous_Occurrence()
    {
        // Cronos picks one occurrence in the fall-back overlap (1:30 first pass = 08:30 UTC on 2025-11-02).
        if (!TryLosAngeles(out _))
            return;

        var schedule = new ScheduleDefinition(
            new ScheduleId("s1"),
            "Fall overlap",
            "30 1 * * *",
            "America/Los_Angeles",
            true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{\"workflowDefinitionId\":\"wf1\"}"),
            null,
            1,
            new IntegrationId("i1"));

        var sink = new HashSet<DateTime>();
        ScheduleExpectedFireCollector.TryCollectFires(
            schedule,
            new DateTime(2025, 11, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 3, 0, 0, 0, DateTimeKind.Utc),
            sink);

        sink.Should().ContainSingle()
            .Which.Should().Be(new DateTime(2025, 11, 2, 8, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Evaluator_Named_Timezone_Produces_Normalized_Utc_Fire()
    {
        if (!TryLosAngeles(out _))
            return;

        await using var db = NewDb();
        db.ScheduleDefinitions.Add(new ScheduleDefinitionEntity
        {
            Id = "sched-la",
            IntegrationId = "i1",
            Name = "Noon LA",
            RecurrenceExpression = "0 12 * * *",
            TimeZone = "America/Los_Angeles",
            Enabled = true,
            TargetJobType = (int)ScheduledJobType.WorkflowExecution,
            TargetJobConfigurationJson = "{\"workflowDefinitionId\":\"wf1\"}",
            Version = 1
        });
        db.ScheduleAssignments.Add(new ScheduleAssignmentEntity
        {
            Id = "a-la",
            ScheduleId = "sched-la",
            RuntimePlaneId = "local",
            DesiredVersion = 1,
            AppliedVersion = 1
        });
        // From just before noon LA on 2025-06-15 (19:00 UTC) with empty state → first-run lookback.
        // Seed last attempted so the lookback includes the noon fire.
        db.ScheduleStates.Add(new ScheduleStateEntity
        {
            AssignmentId = "a-la",
            LastAttemptedUtc = new DateTimeOffset(2025, 6, 15, 18, 0, 0, TimeSpan.Zero)
        });
        await db.SaveChangesAsync();

        var evaluator = new CronScheduleEvaluator(new SqlScheduleRepository(db));
        var asOf = new DateTimeOffset(2025, 6, 15, 19, 30, 0, TimeSpan.Zero);
        var due = await evaluator.GetDueAsync(new RuntimePlaneId("local"), asOf);

        due.Should().ContainSingle();
        due[0].FireTimeUtc.Offset.Should().Be(TimeSpan.Zero);
        due[0].FireTimeUtc.UtcDateTime.Should().Be(new DateTime(2025, 6, 15, 19, 0, 0, DateTimeKind.Utc));
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private sealed class StubPlatformSettingsResolver : IPlatformSettingsResolver
    {
        private readonly object? _value;

        public StubPlatformSettingsResolver(object? value) => _value = value;

        public Task<IReadOnlyList<PlatformSettingResolvedValue>> ListResolvedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlatformSettingResolvedValue>>([]);

        public Task<PlatformSettingResolvedValue?> GetResolvedAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<PlatformSettingResolvedValue?>(null);

        public Task<object?> ResolveRawValueAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_value);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> WarningMessages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                WarningMessages.Add(formatter(state, exception));
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> WarningMessages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                WarningMessages.Add(formatter(state, exception));
        }
    }
}
