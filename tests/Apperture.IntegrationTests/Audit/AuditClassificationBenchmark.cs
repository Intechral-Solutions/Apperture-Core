using System.Diagnostics;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The OR-14 benchmark (audit hardening design gate §15.6, A1.1-12): one million unclassified rows classified by the real
/// startup step inside the real <c>PersistenceMigrationHostedService</c>, with the <c>migrations</c> readiness check polled
/// throughout. Opt-in: set <c>APPERTURE_AUDIT_BENCHMARK=1</c> (optionally <c>APPERTURE_AUDIT_BENCHMARK_ROWS</c>). Without it
/// the test returns immediately and proves nothing; it is a measurement harness, not a regression test.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AuditClassificationBenchmark
{
    private readonly ITestOutputHelper _output;

    public AuditClassificationBenchmark(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task One_million_unclassified_rows_are_classified_at_startup_with_readiness_withheld_until_done()
    {
        if (Environment.GetEnvironmentVariable("APPERTURE_AUDIT_BENCHMARK") != "1")
        {
            _output.WriteLine("Benchmark not requested (APPERTURE_AUDIT_BENCHMARK != 1).");
            return;
        }

        var rows = int.TryParse(Environment.GetEnvironmentVariable("APPERTURE_AUDIT_BENCHMARK_ROWS"), out var n) ? n : 1_000_000;
        await using var db = await AuditSqlScratch.CreateAsync("Apperture_Aud11Bench_");
        var seed = Stopwatch.StartNew();
        await SeedAsync(db, rows);
        seed.Stop();
        _output.WriteLine($"Seeded {rows:N0} unclassified rows in {seed.Elapsed}.");
        await DescribeWorkloadAsync(db);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(new OutputLoggerProvider(_output)).SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<IHostEnvironment>(new BenchmarkEnvironment());
        services.AddApperturePersistence(db.ConnectionString);
        services.AddApperturePersistenceMigrationsOnStartup();
        await using var provider = services.BuildServiceProvider();
        var health = provider.GetRequiredService<HealthCheckService>();
        var hosted = provider.GetServices<IHostedService>().Single(s => s.GetType().Name == "PersistenceMigrationHostedService");

        var timeline = new List<(TimeSpan At, HealthStatus Status)>();
        var total = Stopwatch.StartNew();
        var start = Task.Run(() => hosted.StartAsync(CancellationToken.None));
        while (!start.IsCompleted)
        {
            var report = await health.CheckHealthAsync(r => r.Name == "migrations");
            if (timeline.Count == 0 || timeline[^1].Status != report.Status)
                timeline.Add((total.Elapsed, report.Status));
            await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(1)));
        }

        await start;
        total.Stop();
        var final = await health.CheckHealthAsync(r => r.Name == "migrations");
        timeline.Add((total.Elapsed, final.Status));

        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE ClassificationState IS NULL;")).Should().Be(0);
        final.Status.Should().Be(HealthStatus.Healthy);
        timeline[0].Status.Should().Be(HealthStatus.Unhealthy, "the host reports not ready while classification runs");

        _output.WriteLine($"Startup step (migrations check, normalization, classification) took {total.Elapsed}.");
        foreach (var (at, status) in timeline)
            _output.WriteLine($"  readiness at {at}: {status}");

        await using (var ctx = db.NewDb())
        {
            var empty = Stopwatch.StartNew();
            var again = await AuditClassification.RunAsync(ctx, NullLogger.Instance);
            empty.Stop();
            again.Read.Should().Be(0);
            _output.WriteLine($"Second start with nothing to classify: {empty.Elapsed.TotalMilliseconds:F1} ms.");
        }

        _output.WriteLine(db.ExecutionMarker);
    }

    /// <summary>
    /// 2,000 users with ledgers (and 500 retired names), 400 Entra links; rows mixing the real action catalog's families:
    /// user ids 40%, literals 35% (scheduler, worker, system, anonymous), current or retired usernames 12%, Entra object
    /// ids 5%, unresolved values 5%, the unknown placeholder 3%; 2% typed connection JSON.
    /// </summary>
    private static async Task SeedAsync(AuditSqlScratch db, int rows)
    {
        await db.ExecuteAsync("""
            SET NOCOUNT ON;
            WITH n AS (SELECT TOP (2000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO Users (Id, Username, DisplayName, IsBreakGlassEligible)
            SELECT RIGHT(REPLICATE('0', 24) + CAST(i AS varchar(10)), 24), CONCAT('user.', i), CONCAT('User ', i), 0 FROM n;
            INSERT INTO UsernameLedger (Username, UserId, AssignedAtUtc)
            SELECT Username, Id, SYSDATETIMEOFFSET() FROM Users;
            INSERT INTO UsernameLedger (Username, UserId, AssignedAtUtc, RetiredAtUtc, RetiredReason)
            SELECT CONCAT('old.user.', RIGHT(Id, 6)), Id, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'rename'
            FROM Users WHERE CAST(RIGHT(Id, 6) AS int) <= 500;
            INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, TenantId, LinkedAt)
            SELECT CONCAT('lk', RIGHT(Id, 6)), Id, 0, LOWER(CONVERT(varchar(36), NEWID())), N'tenant-1', SYSDATETIMEOFFSET()
            FROM Users WHERE CAST(RIGHT(Id, 6) AS int) <= 400;
            """);

        // Set-based, in chunks to keep the log small.
        const int chunk = 250_000;
        for (var offset = 0; offset < rows; offset += chunk)
        {
            var count = Math.Min(chunk, rows - offset);
            await db.ExecuteAsync($$"""
                SET NOCOUNT ON;
                WITH n AS (SELECT TOP ({{count}}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) + {{offset}} AS i
                           FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c)
                INSERT INTO AuditLogs (At, Actor, Action, Outcome, Details)
                SELECT DATEADD(second, -i, SYSDATETIMEOFFSET()),
                    CASE
                        WHEN i % 100 < 40 THEN RIGHT(REPLICATE('0', 24) + CAST(i % 2000 + 1 AS varchar(10)), 24)
                        WHEN i % 100 < 60 THEN N'system:scheduler'
                        WHEN i % 100 < 68 THEN N'system-worker'
                        WHEN i % 100 < 72 THEN N'system'
                        WHEN i % 100 < 75 THEN N'anonymous'
                        WHEN i % 100 < 84 THEN CONCAT(N'user.', i % 2000 + 1)
                        WHEN i % 100 < 87 THEN CONCAT(N'old.user.', RIGHT(REPLICATE('0', 6) + CAST(i % 500 + 1 AS varchar(10)), 6))
                        WHEN i % 100 < 92 THEN (SELECT TOP 1 ExternalId FROM IdentityLinks l WHERE l.Id = CONCAT('lk', RIGHT(REPLICATE('0', 6) + CAST(i % 400 + 1 AS varchar(10)), 6)))
                        WHEN i % 100 < 97 THEN CONCAT(N'departed.person.', i % 300)
                        ELSE N'unknown'
                    END,
                    CASE
                        WHEN i % 100 < 50 THEN N'scheduler.schedule.dispatched'
                        WHEN i % 100 < 56 THEN N'scheduler.schedule.blocked_by_lifecycle'
                        WHEN i % 100 < 64 THEN N'execution.workflow_start.blocked'
                        WHEN i % 100 < 72 THEN N'governance.role.assignment.created'
                        WHEN i % 100 < 80 THEN N'auth.local.login.success'
                        WHEN i % 100 < 85 THEN N'auth.local.login.failed'
                        WHEN i % 100 < 90 THEN N'account.invitation.sent'
                        WHEN i % 100 < 95 THEN N'execution.operation_item.bulk_retry.requested'
                        WHEN i % 100 < 98 THEN N'connection.created'
                        ELSE N'role.assigned'
                    END,
                    CASE WHEN i % 7 = 0 THEN N'failed' WHEN i % 5 = 0 THEN N'blocked' ELSE N'success' END,
                    CASE WHEN i % 100 BETWEEN 95 AND 97
                        THEN CONCAT(N'{"resourceType":"Connection","resourceId":"c', i, N'","displayName":"Conn","connectorDefinitionId":"sql"}')
                        ELSE CONCAT(N'schedule:s', i % 500, N'; runId=r', i) END
                FROM n;
                """);
        }
    }

    private async Task DescribeWorkloadAsync(AuditSqlScratch db)
    {
        _output.WriteLine($"SQL Server: {await db.ScalarAsync<string>("SELECT @@VERSION;")}".Replace('\n', ' '));
        _output.WriteLine($"Edition: {await db.ScalarAsync<string>("SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(128));")}");
        _output.WriteLine($"Users {await db.ScalarAsync<int>("SELECT COUNT(*) FROM Users;")}, ledger {await db.ScalarAsync<int>("SELECT COUNT(*) FROM UsernameLedger;")}, Entra links {await db.ScalarAsync<int>("SELECT COUNT(*) FROM IdentityLinks WHERE Source = 0;")}");
        foreach (var row in await db.RowsAsync("""
            SELECT CASE
                WHEN Actor LIKE N'system%' OR Actor = N'anonymous' THEN 'literal'
                WHEN Actor = N'unknown' THEN 'unknown placeholder'
                WHEN Actor LIKE N'user.%' OR Actor LIKE N'old.user.%' THEN 'username'
                WHEN Actor LIKE N'departed.%' THEN 'unresolvable value'
                WHEN Actor LIKE N'%-%-%-%-%' THEN 'entra object id'
                ELSE 'user id' END AS Mix, COUNT_BIG(*) AS Rows
            FROM AuditLogs GROUP BY CASE
                WHEN Actor LIKE N'system%' OR Actor = N'anonymous' THEN 'literal'
                WHEN Actor = N'unknown' THEN 'unknown placeholder'
                WHEN Actor LIKE N'user.%' OR Actor LIKE N'old.user.%' THEN 'username'
                WHEN Actor LIKE N'departed.%' THEN 'unresolvable value'
                WHEN Actor LIKE N'%-%-%-%-%' THEN 'entra object id'
                ELSE 'user id' END;
            """))
        {
            _output.WriteLine($"  actor mix {row["Mix"]}: {row["Rows"]}");
        }
    }

    private sealed class BenchmarkEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "AuditClassificationBenchmark";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class OutputLoggerProvider(ITestOutputHelper output) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new OutputLogger(output);

        public void Dispose()
        {
        }

        private sealed class OutputLogger(ITestOutputHelper output) : ILogger
        {
            private int _batches;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var message = formatter(state, exception);
                // Every 20th batch line, plus everything else, keeps the output readable.
                if (message.StartsWith("Audit classification batch", StringComparison.Ordinal) && ++_batches % 20 != 1)
                    return;
                output.WriteLine($"[{DateTimeOffset.UtcNow:HH:mm:ss.fff}] {logLevel}: {message}");
            }
        }
    }
}
