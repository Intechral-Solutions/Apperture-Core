using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sample.ApiPolling.Contracts;
using Sample.ApiPolling.Persistence;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// SDK-5B: SQL round-trip for Sample.ApiPolling durable last-seen / watermark state.
/// Without a reachable APPERTURE_TEST_SQL_* endpoint the tests fail (see <see cref="SqlTestExecution"/>).
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ApiPollingDurableSyncStateSqlServerTests
{
    private static async Task<(IDbContextFactory<SampleApiPollingDbContext> Factory, SampleApiPollingDbContext Db)> CreateAsync()
    {
        var cs = SqlServerTestConfiguration.RequireConnectionString();
        SqlServerTestConfiguration.EnsureTestDatabaseExists(cs);

        var factory = new ConnectionStringDbContextFactory(cs);
        var db = factory.CreateDbContext();
        if (!await db.Database.CanConnectAsync())
        {
            await db.DisposeAsync();
            throw SqlTestExecution.Unavailable("the test catalog was created but cannot be connected to");
        }

        await db.Database.MigrateAsync();
        return (factory, db);
    }

    [Fact]
    public async Task Sql_store_round_trips_last_seen_scoped_by_integration_and_reset()
    {
        var (factory, db) = await CreateAsync();
        await using (db)
        {
            IApiPollingDurableLastSeenStore store = new SqlApiPollingDurableLastSeenStore(factory);

            var scopeA = store.ResolveScopeKey("int-sdk5b-a-" + Guid.NewGuid().ToString("N")[..8]);
            var scopeB = store.ResolveScopeKey("int-sdk5b-b-" + Guid.NewGuid().ToString("N")[..8]);
            var now = DateTimeOffset.UtcNow;

            store.UpsertLastSeen(scopeA,
            [
                new SeenAccountFingerprint("C-001", "Acme", "a@x.example", "Active", "Standard", "2026-07-01T00:00:00Z", 1, false, now),
                new SeenAccountFingerprint("C-002", "Beta", "b@x.example", "Active", "Standard", "2026-07-02T00:00:00Z", 1, false, now),
            ]);

            store.GetLastSeen(scopeA).Should().HaveCount(2);
            store.GetLastSeen(scopeB).Should().BeEmpty();

            store.UpsertLastSeen(scopeA,
            [
                new SeenAccountFingerprint("C-001", "Acme", "billing@x.example", "Active", "Standard", "2026-07-10T00:00:00Z", 2, false, now),
            ]);
            store.GetLastSeen(scopeA)["C-001"].Version.Should().Be(2);
            store.GetLastSeen(scopeA)["C-001"].Email.Should().Be("billing@x.example");

            store.RemoveLastSeen(scopeA, ["C-002"]);
            store.GetLastSeen(scopeA).Keys.Should().BeEquivalentTo(["C-001"]);

            store.SetLastSummary(scopeA, new SyncSummary(
                "snapshot-changes",
                "sync-1",
                SyncBatchStatus.Succeeded.ToString(),
                1, 1, 0, 1, 0, 0,
                now));
            store.GetLastSummary(scopeA)!.SnapshotId.Should().Be("snapshot-changes");

            // Restart-like: new store instance over same DB
            IApiPollingDurableLastSeenStore store2 = new SqlApiPollingDurableLastSeenStore(factory);
            store2.GetLastSeen(scopeA).Should().ContainKey("C-001");
            store2.GetLastSeen(scopeA).Should().NotContainKey("C-002");

            store2.ResetScope(scopeA);
            store2.GetLastSeen(scopeA).Should().BeEmpty();
            store2.GetLastSummary(scopeA).Should().BeNull();

            var state = await db.ApiPollingSyncStates.AsNoTracking().FirstOrDefaultAsync(s => s.ScopeKey == scopeA);
            state.Should().BeNull();
        }
    }

    [Fact]
    public async Task Sql_known_records_store_hash_not_unbounded_payload()
    {
        var (factory, db) = await CreateAsync();
        await using (db)
        {
            var store = new SqlApiPollingDurableLastSeenStore(factory);
            var scope = store.ResolveScopeKey("int-sdk5b-hash-" + Guid.NewGuid().ToString("N")[..8]);
            var now = DateTimeOffset.UtcNow;

            store.UpsertLastSeen(scope,
            [
                new SeenAccountFingerprint("C-001", "Acme", "ops@acme.example", "Active", "Standard", "2026-07-01T00:00:00Z", 1, false, now),
            ]);

            var stateId = await db.ApiPollingSyncStates.AsNoTracking()
                .Where(s => s.ScopeKey == scope)
                .Select(s => s.StateId)
                .SingleAsync();
            var record = await db.ApiPollingKnownRecords.AsNoTracking()
                .SingleAsync(r => r.StateId == stateId && r.ExternalId == "C-001");

            record.RecordHash.Should().HaveLength(64);
            record.MetadataJson.Should().BeNull();
            record.Email.Should().Be("ops@acme.example");
        }
    }

    private sealed class ConnectionStringDbContextFactory : IDbContextFactory<SampleApiPollingDbContext>
    {
        private readonly string _connectionString;

        public ConnectionStringDbContextFactory(string connectionString) =>
            _connectionString = connectionString;

        public SampleApiPollingDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<SampleApiPollingDbContext>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .UseSqlServer(
                    _connectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_SampleApiPolling"))
                .Options;
            return new SampleApiPollingDbContext(options);
        }
    }
}
