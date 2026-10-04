using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// SQL Server evidence that concurrent <see cref="SqlUserRepository.UpdateLastAuthenticatedAtAsync"/>
/// and user resolution do not fail requests due to optimistic concurrency on identity links.
/// Skips when SQL Server / LocalDB is unavailable.
/// </summary>
public sealed class SqlUserRepositoryLastAuthenticatedAtSqlServerTests
{
    private const string LocalDbConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=Apperture_IntegrationTests;Trusted_Connection=True;";

    private static async Task<(AppertureDbContext Db, DbContextOptions<AppertureDbContext> Options)?> TryCreateMigratedDbAsync()
    {
        var configured = SqlServerTestConfiguration.GetConnectionString();
        var candidates = new List<string>();
        if (configured is not null)
            candidates.Add(configured);
        candidates.Add(LocalDbConnectionString);

        foreach (var connectionString in candidates)
        {
            AppertureDbContext? db = null;
            try
            {
                if (connectionString == configured)
                    SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

                var options = new DbContextOptionsBuilder<AppertureDbContext>()
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                    .UseSqlServer(connectionString)
                    .Options;

                db = new AppertureDbContext(options);
                if (!await db.Database.CanConnectAsync())
                {
                    await db.DisposeAsync();
                    continue;
                }

                await AppertureDatabaseMigrator.MigrateAsync(db);
                return (db, options);
            }
            catch
            {
                if (db is not null)
                    await db.DisposeAsync();
            }
        }

        return null;
    }

    [Fact]
    public async Task UpdateLastAuthenticatedAtAsync_parallel_calls_do_not_throw()
    {
        var context = await TryCreateMigratedDbAsync();
        if (context is null)
            return;

        var (db, options) = context.Value;
        await using (db)
        {
            var externalId = $"last-auth-{Guid.NewGuid():N}";
            var userId = $"user-{Guid.NewGuid():N}"[..24];
            var linkId = $"link-{Guid.NewGuid():N}"[..24];
            await SeedIdentityLinkAsync(db, userId, linkId, externalId, lastAuthenticatedAt: null);

            var at = DateTimeOffset.UtcNow;
            var tasks = Enumerable.Range(0, 20).Select(async _ =>
            {
                await using var taskDb = new AppertureDbContext(options);
                var repo = new SqlUserRepository(taskDb, NullLogger<SqlUserRepository>.Instance);
                await repo.UpdateLastAuthenticatedAtAsync(IdentitySource.Local, externalId, at, CancellationToken.None);
            });

            var act = () => Task.WhenAll(tasks);
            await act.Should().NotThrowAsync();

            var updated = await db.IdentityLinks.AsNoTracking()
                .SingleAsync(l => l.Source == (int)IdentitySource.Local && l.ExternalId == externalId);
            updated.LastAuthenticatedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ResolveUserIdAsync_parallel_calls_do_not_throw()
    {
        var context = await TryCreateMigratedDbAsync();
        if (context is null)
            return;

        var (db, options) = context.Value;
        await using (db)
        {
            var externalId = $"resolve-{Guid.NewGuid():N}";
            var userId = $"user-{Guid.NewGuid():N}"[..24];
            var linkId = $"link-{Guid.NewGuid():N}"[..24];
            await SeedIdentityLinkAsync(db, userId, linkId, externalId, lastAuthenticatedAt: null);

            var actor = new HumanActor(new ActorId(externalId), externalId, preferredIdentitySource: IdentitySource.Local);
            var tasks = Enumerable.Range(0, 20).Select(async _ =>
            {
                await using var taskDb = new AppertureDbContext(options);
                var repo = new SqlUserRepository(taskDb, NullLogger<SqlUserRepository>.Instance);
                var resolver = new SqlPrincipalToUserResolver(repo);
                await resolver.ResolveUserIdAsync(actor, CancellationToken.None);
            });

            var act = () => Task.WhenAll(tasks);
            await act.Should().NotThrowAsync();

            await using var verifyDb = new AppertureDbContext(options);
            var verifyRepo = new SqlUserRepository(verifyDb, NullLogger<SqlUserRepository>.Instance);
            var verifyResolver = new SqlPrincipalToUserResolver(verifyRepo);
            var resolved = await verifyResolver.ResolveUserIdAsync(actor, CancellationToken.None);
            resolved.Should().Be(new UserId(userId));
        }
    }

    [Fact]
    public async Task UpdateLastAuthenticatedAtAsync_throttles_writes_within_interval()
    {
        var context = await TryCreateMigratedDbAsync();
        if (context is null)
            return;

        await using var db = context.Value.Db;

        var externalId = $"throttle-{Guid.NewGuid():N}";
        var userId = $"user-{Guid.NewGuid():N}"[..24];
        var linkId = $"link-{Guid.NewGuid():N}"[..24];
        var initial = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        await SeedIdentityLinkAsync(db, userId, linkId, externalId, initial);

        var repo = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
        await repo.UpdateLastAuthenticatedAtAsync(
            IdentitySource.Local,
            externalId,
            initial.AddSeconds(30),
            CancellationToken.None);

        var row = await db.IdentityLinks.AsNoTracking()
            .SingleAsync(l => l.Source == (int)IdentitySource.Local && l.ExternalId == externalId);
        row.LastAuthenticatedAt.Should().Be(initial);
    }

    private static async Task SeedIdentityLinkAsync(
        AppertureDbContext db,
        string userId,
        string linkId,
        string externalId,
        DateTimeOffset? lastAuthenticatedAt)
    {
        db.AddUsersWithCurrentUsernames(new UserEntity
        {
            Id = userId,
            Username = externalId,
            DisplayName = "Operator"
        });
        db.IdentityLinks.Add(new IdentityLinkEntity
        {
            Id = linkId,
            UserId = userId,
            Source = (int)IdentitySource.Local,
            ExternalId = externalId,
            LastAuthenticatedAt = lastAuthenticatedAt
        });
        await db.SaveChangesAsync();
    }
}
